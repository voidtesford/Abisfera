using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Nutrition;

/// <summary>
/// Snack obesity: eating snacks can add ExcessFat which slows movement.
/// Fat decays over time. Molecule medicine removes it.
/// </summary>
public sealed class ObesitySystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private static readonly ProtoId<ReagentPrototype> ExcessFatId = "ExcessFat";
    private static readonly ProtoId<TagPrototype> SnackTag = "FoodSnack";

    private const float FatSlowPerUnit = 0.05f;
    private static readonly TimeSpan FatDecayInterval = TimeSpan.FromMinutes(10);
    private static readonly FixedPoint2 FatDecayAmount = 1;

    private static readonly string[] SolutionPriority = { "bloodstream", "chemicals", "stomach" };

    public override void Initialize()
    {
        base.Initialize();
        // Cannot use EdibleComponent + FullyEatenEvent: IngestionSystem already owns that pair.
        // TagComponent is present on snack entities; filter by FoodSnack inside the handler.
        SubscribeLocalEvent<TagComponent, FullyEatenEvent>(OnFullyEaten);
        SubscribeLocalEvent<ObesityTrackerComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ObesityTrackerComponent>();
        while (query.MoveNext(out var uid, out var tracker))
        {
            if (_timing.CurTime < tracker.NextDecay)
                continue;

            tracker.NextDecay = _timing.CurTime + FatDecayInterval;
            DecayFat(uid, tracker.DecayMultiplier);
        }
    }

    private void OnFullyEaten(Entity<TagComponent> food, ref FullyEatenEvent args)
    {
        // Only snacks — pass EntityUid to avoid HasTag overload ambiguity
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

        if (_random.Prob(chance))
        {
            AddFat(eater, 3f);
            tracker.SnacksEaten = 0;
        }
    }

    private void OnRefreshSpeed(EntityUid uid, ObesityTrackerComponent component, ref RefreshMovementSpeedModifiersEvent args)
    {
        var fat = GetFatAmount(uid);
        if (fat <= 0)
            return;

        var slow = 1f - Math.Clamp(fat.Float() * FatSlowPerUnit, 0f, 0.5f);
        args.ModifySpeed(slow, slow);
    }

    private bool TryGetFatSolution(EntityUid uid, out Entity<SolutionComponent>? solEnt, out Solution? solution)
    {
        solEnt = null;
        solution = null;

        foreach (var name in SolutionPriority)
        {
            if (_solution.TryGetSolution(uid, name, out solEnt, out solution))
                return true;
        }

        return false;
    }

    private void AddFat(EntityUid uid, float amount)
    {
        if (!TryGetFatSolution(uid, out var solEnt, out var solution))
            return;

        solution!.AddReagent(ExcessFatId, amount);
        Dirty(solEnt!.Value);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void DecayFat(EntityUid uid, float multiplier)
    {
        if (!TryGetFatSolution(uid, out var solEnt, out var solution))
            return;

        var fat = solution!.GetTotalPrototypeQuantity(ExcessFatId);
        if (fat <= 0)
            return;

        var remove = FixedPoint2.Min(fat, FatDecayAmount * multiplier);
        solution.RemoveReagent(new ReagentId(ExcessFatId, null), remove);
        Dirty(solEnt!.Value);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private FixedPoint2 GetFatAmount(EntityUid uid)
    {
        if (!TryGetFatSolution(uid, out _, out var solution))
            return FixedPoint2.Zero;

        return solution!.GetTotalPrototypeQuantity(ExcessFatId);
    }
}

[RegisterComponent]
public sealed partial class ObesityTrackerComponent : Component
{
    [DataField]
    public int SnacksEaten;

    [DataField]
    public TimeSpan NextDecay;

    [DataField]
    public float DecayMultiplier = 1f;
}
