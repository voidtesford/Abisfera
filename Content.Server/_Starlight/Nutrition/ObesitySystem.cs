using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Jittering;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Nutrition;

/// <summary>
/// Snack obesity: fat on ObesityTrackerComponent. Molecule 5u → 1 fat with side effects.
/// Overdose (≥20u Molecule in blood): jitter, strong hunger, 30 poison total once per OD episode.
/// </summary>
public sealed class ObesitySystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;

    private static readonly ProtoId<ReagentPrototype> ExcessFatId = "ExcessFat";
    private static readonly ProtoId<ReagentPrototype> MoleculeId = "Molecule";
    private static readonly ProtoId<TagPrototype> SnackTag = "FoodSnack";

    private const float FatSlowPerUnit = 0.05f;
    private const float FatPerProc = 3f;
    private static readonly TimeSpan FatDecayInterval = TimeSpan.FromMinutes(10);
    private const float FatDecayAmount = 1f;

    /// <summary>5 units of Molecule remove 1 unit of fat.</summary>
    private const float MoleculePerFat = 5f;

    private const float SlashPerMol = 0.5f;
    private const float PoisonPerMol = 0.1f;
    private const float HungerPerMol = -3f;

    private const float OverdoseThreshold = 20f;
    private const float OverdosePoisonTotal = 30f;
    private static readonly TimeSpan OverdoseJitterTime = TimeSpan.FromSeconds(6);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TagComponent, FullyEatenEvent>(OnFullyEaten);
        SubscribeLocalEvent<ObesityTrackerComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ObesityTrackerComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ObesityTrackerComponent>();
        while (query.MoveNext(out var uid, out var tracker))
        {
            ProcessMolecule(uid, tracker);

            if (tracker.FatAmount <= 0)
                continue;

            if (_timing.CurTime < tracker.NextDecay)
                continue;

            tracker.NextDecay = _timing.CurTime + FatDecayInterval;
            DecayFat(uid, tracker);
        }
    }

    private void OnFullyEaten(Entity<TagComponent> food, ref FullyEatenEvent args)
    {
        if (!_tag.HasTag(food.Owner, SnackTag))
            return;

        var eater = args.User;
        if (!HasComp<HungerComponent>(eater))
            return;

        var tracker = EnsureComp<ObesityTrackerComponent>(eater);
        if (tracker.NextDecay == default)
            tracker.NextDecay = _timing.CurTime + FatDecayInterval;

        tracker.SnacksEaten++;

        var chance = tracker.SnacksEaten switch
        {
            <= 1 => 0f,
            2 => 0.20f,
            3 => 0.40f,
            4 => 0.70f,
            _ => 1f
        };

        if (!_random.Prob(chance))
            return;

        tracker.SnacksEaten = 0;
        AddFat(eater, tracker, FatPerProc);
    }

    private void OnExamined(EntityUid uid, ObesityTrackerComponent component, ExaminedEvent args)
    {
        if (component.FatAmount <= 0)
            return;

        args.PushMarkup(Loc.GetString("obesity-examine-fat"));
    }

    private void OnRefreshSpeed(EntityUid uid, ObesityTrackerComponent component, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (component.FatAmount <= 0)
            return;

        var slow = 1f - Math.Clamp(component.FatAmount * FatSlowPerUnit, 0f, 0.5f);
        args.ModifySpeed(slow, slow);
    }

    private void AddFat(EntityUid uid, ObesityTrackerComponent tracker, float amount)
    {
        tracker.FatAmount += amount;
        Dirty(uid, tracker);

        TryMirrorFatToBlood(uid, amount);
        _movement.RefreshMovementSpeedModifiers(uid);

        _popup.PopupEntity(Loc.GetString("obesity-fat-slow"), uid, uid, PopupType.MediumCaution);
    }

    private void DecayFat(EntityUid uid, ObesityTrackerComponent tracker)
    {
        var remove = Math.Min(tracker.FatAmount, FatDecayAmount * tracker.DecayMultiplier);
        if (remove <= 0)
            return;

        tracker.FatAmount = Math.Max(0f, tracker.FatAmount - remove);
        Dirty(uid, tracker);
        TryRemoveFatFromBlood(uid, remove);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    /// 5u Molecule → 1 fat. Applies treatment side effects; OD at ≥20u.
    /// </summary>
    private void ProcessMolecule(EntityUid uid, ObesityTrackerComponent tracker)
    {
        if (!TryGetBloodSolution(uid, out var solEnt, out var solution))
            return;

        var molecule = solution.GetTotalPrototypeQuantity(MoleculeId);
        if (molecule <= 0)
        {
            // Reset OD tracker when molecule is gone
            if (tracker.OdPoisonApplied > 0f)
            {
                tracker.OdPoisonApplied = 0f;
                Dirty(uid, tracker);
            }
            return;
        }

        var molFloat = molecule.Float();
        var overdose = molFloat >= OverdoseThreshold;

        // How much fat we can clear with available molecule (5 mol : 1 fat).
        var fatCanClear = molFloat / MoleculePerFat;
        if (fatCanClear <= 0)
            return;

        var fatRemove = Math.Min(tracker.FatAmount, fatCanClear);
        float molUsedFloat;

        if (fatRemove > 0)
        {
            tracker.FatAmount = Math.Max(0f, tracker.FatAmount - fatRemove);
            Dirty(uid, tracker);

            molUsedFloat = fatRemove * MoleculePerFat;
            var molUsed = FixedPoint2.New(molUsedFloat);
            solution.RemoveReagent(new ReagentId(MoleculeId, null), FixedPoint2.Min(molecule, molUsed));

            TryRemoveFatFromBlood(uid, fatRemove);
            _movement.RefreshMovementSpeedModifiers(uid);

            // Treatment side effects (while removing fat)
            ApplyTreatmentEffects(uid, molUsedFloat, overdose);
        }
        else
        {
            // No component fat — clear leftover ExcessFat reagent in blood (5:1), no treatment damage
            var bloodFat = solution.GetTotalPrototypeQuantity(ExcessFatId);
            if (bloodFat <= 0)
            {
                if (overdose)
                    ApplyOverdoseOnly(uid, tracker);
                Dirty(solEnt);
                return;
            }

            var bloodRemove = FixedPoint2.Min(bloodFat, molFloat / MoleculePerFat);
            molUsedFloat = Math.Min(molFloat, bloodRemove.Float() * MoleculePerFat);
            var molUsed = FixedPoint2.New(molUsedFloat);
            solution.RemoveReagent(new ReagentId(ExcessFatId, null), bloodRemove);
            solution.RemoveReagent(new ReagentId(MoleculeId, null), FixedPoint2.Min(molecule, molUsed));
        }

        if (overdose)
            ApplyOverdoseOnly(uid, tracker);

        Dirty(solEnt);
    }

    /// <summary>Slash / poison / hunger while molecule burns fat.</summary>
    private void ApplyTreatmentEffects(EntityUid uid, float molUsed, bool overdose)
    {
        if (molUsed <= 0)
            return;

        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                ["Slash"] = molUsed * SlashPerMol,
                ["Poison"] = molUsed * PoisonPerMol,
            }
        };
        _damageable.TryChangeDamage(uid, damage, interruptsDoAfters: false);

        if (TryComp<HungerComponent>(uid, out var hunger))
            _hunger.ModifyHunger(uid, HungerPerMol * molUsed, hunger);
    }

    /// <summary>
    /// OD (≥20u): jitter, strong hunger, up to 30 poison total per OD episode.
    /// </summary>
    private void ApplyOverdoseOnly(EntityUid uid, ObesityTrackerComponent tracker)
    {
        _jitter.DoJitter(uid, OverdoseJitterTime, refresh: true, amplitude: 15f, frequency: 5f);

        if (TryComp<HungerComponent>(uid, out var hunger))
            _hunger.ModifyHunger(uid, -15f, hunger);

        var poisonLeft = OverdosePoisonTotal - tracker.OdPoisonApplied;
        if (poisonLeft > 0)
        {
            var chunk = poisonLeft; // apply remaining OD poison (up to 30 total)
            var damage = new DamageSpecifier
            {
                DamageDict = { ["Poison"] = chunk }
            };
            _damageable.TryChangeDamage(uid, damage, interruptsDoAfters: false);
            tracker.OdPoisonApplied += chunk;
            Dirty(uid, tracker);

            _popup.PopupEntity(Loc.GetString("obesity-molecule-overdose"), uid, uid, PopupType.MediumCaution);
        }
    }

    private bool TryGetBloodSolution(EntityUid uid, out Entity<SolutionComponent> solEnt, out Solution solution)
    {
        solEnt = default;
        solution = default!;

        if (!TryComp<BloodstreamComponent>(uid, out var blood))
            return false;

        Entity<SolutionComponent>? bloodSol = blood.BloodSolution;
        if (!_solution.ResolveSolution(uid, blood.BloodSolutionName, ref bloodSol, out var bloodSolution)
            || bloodSolution == null)
            return false;

        solEnt = bloodSol!.Value;
        solution = bloodSolution;
        return true;
    }

    private void TryMirrorFatToBlood(EntityUid uid, float amount)
    {
        if (!TryGetBloodSolution(uid, out var solEnt, out var solution))
            return;

        solution.AddReagent(ExcessFatId, amount);
        Dirty(solEnt);
    }

    private void TryRemoveFatFromBlood(EntityUid uid, float amount)
    {
        if (!TryGetBloodSolution(uid, out var solEnt, out var solution))
            return;

        var have = solution.GetTotalPrototypeQuantity(ExcessFatId);
        if (have <= 0)
            return;

        var remove = FixedPoint2.Min(have, amount);
        solution.RemoveReagent(new ReagentId(ExcessFatId, null), remove);
        Dirty(solEnt);
    }
}

[RegisterComponent]
public sealed partial class ObesityTrackerComponent : Component
{
    [DataField]
    public int SnacksEaten;

    /// <summary>Source of truth for slow / examine.</summary>
    [DataField]
    public float FatAmount;

    [DataField]
    public TimeSpan NextDecay;

    [DataField]
    public float DecayMultiplier = 1f;

    /// <summary>Poison already dealt from current OD episode (cap 30).</summary>
    [DataField]
    public float OdPoisonApplied;
}
