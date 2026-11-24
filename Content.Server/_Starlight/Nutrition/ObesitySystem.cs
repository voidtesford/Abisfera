using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Nutrition;

/// <summary>
/// Snack obesity: fat is tracked on ObesityTrackerComponent (source of truth).
/// Mirrored into bloodstream for the analyzer. Molecule medicine: 5u → −1 fat.
/// </summary>
public sealed class ObesitySystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly ProtoId<ReagentPrototype> ExcessFatId = "ExcessFat";
    private static readonly ProtoId<ReagentPrototype> MoleculeId = "Molecule";
    private static readonly ProtoId<TagPrototype> SnackTag = "FoodSnack";

    private const float FatSlowPerUnit = 0.05f;
    private const float FatPerProc = 3f;
    private static readonly TimeSpan FatDecayInterval = TimeSpan.FromMinutes(10);
    private const float FatDecayAmount = 1f;

    /// <summary>5 units of Molecule remove 1 unit of fat.</summary>
    private const float MoleculePerFat = 5f;

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
            // Medicine first: Molecule in bloodstream burns component fat (5:1).
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

        // Single red warning only.
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
    /// 5u Molecule in bloodstream → −1 FatAmount. Consumes Molecule from blood.
    /// Works even if ExcessFat reagent already vanished.
    /// </summary>
    private void ProcessMolecule(EntityUid uid, ObesityTrackerComponent tracker)
    {
        if (!TryGetBloodSolution(uid, out var solEnt, out var solution))
            return;

        var molecule = solution.GetTotalPrototypeQuantity(MoleculeId);
        if (molecule <= 0)
            return;

        // How much fat we can clear with available molecule (5 mol : 1 fat).
        var fatCanClear = molecule.Float() / MoleculePerFat;
        if (fatCanClear <= 0)
            return;

        var fatRemove = Math.Min(tracker.FatAmount, fatCanClear);
        // Even with 0 component fat, still consume molecule vs blood ExcessFat below.

        if (fatRemove > 0)
        {
            tracker.FatAmount = Math.Max(0f, tracker.FatAmount - fatRemove);
            Dirty(uid, tracker);

            var molUsed = FixedPoint2.New(fatRemove * MoleculePerFat);
            solution.RemoveReagent(new ReagentId(MoleculeId, null), FixedPoint2.Min(molecule, molUsed));

            // Keep analyzer reagent roughly in sync.
            TryRemoveFatFromBlood(uid, fatRemove);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
        else
        {
            // No component fat — still let molecule eat leftover ExcessFat reagent in blood (5:1).
            var bloodFat = solution.GetTotalPrototypeQuantity(ExcessFatId);
            if (bloodFat <= 0)
                return;

            var bloodRemove = FixedPoint2.Min(bloodFat, molecule.Float() / MoleculePerFat);
            var molUsed = FixedPoint2.Min(molecule, bloodRemove * MoleculePerFat);
            solution.RemoveReagent(new ReagentId(ExcessFatId, null), bloodRemove);
            solution.RemoveReagent(new ReagentId(MoleculeId, null), molUsed);
        }

        Dirty(solEnt);
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
}
