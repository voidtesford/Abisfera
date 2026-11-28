using Content.Server.Humanoid;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Species;

/// <summary>
/// Slime people change skin color to match the dominant foreign reagent
/// in their bloodstream or stomach.
/// Original player-selected skin color is remembered on first tint and restored when gone.
/// </summary>
public sealed class SlimeReagentColorSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly ProtoId<SpeciesPrototype> SlimeSpecies = "SlimePerson";

    /// <summary>
    /// bloodstream — injections; stomach — drinking/eating.
    /// </summary>
    private static readonly string[] WatchedSolutions =
    {
        BloodstreamComponent.DefaultBloodSolutionName, // "bloodstream"
        "stomach",
    };

    /// <summary>
    /// Saved only when we first apply a foreign tint — never on MapInit,
    /// otherwise we capture the default gray before the character profile loads.
    /// </summary>
    private readonly Dictionary<EntityUid, Color> _originalColors = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<HumanoidAppearanceComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<HumanoidAppearanceComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<HumanoidAppearanceComponent> ent, ref ComponentShutdown args)
    {
        _originalColors.Remove(ent.Owner);
    }

    private void OnSolutionChanged(Entity<HumanoidAppearanceComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (!WatchedSolutions.Contains(args.SolutionId))
            return;

        if (ent.Comp.Species != SlimeSpecies)
            return;

        ApplyTint(ent);
    }

    private void ApplyTint(Entity<HumanoidAppearanceComponent> ent)
    {
        var uid = ent.Owner;
        var appearance = ent.Comp;

        Color? tint = null;
        var maxAmount = 0f;

        foreach (var solutionId in WatchedSolutions)
        {
            if (!_solutions.TryGetSolution(uid, solutionId, out _, out var solution))
                continue;

            foreach (var reagent in solution.Contents)
            {
                var amount = reagent.Quantity.Float();
                if (amount <= 0.5f)
                    continue;

                if (!_proto.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto))
                    continue;

                // Own slime blood does not tint. Foreign Blood / other reagents do.
                if (proto.ID == "Slime")
                    continue;

                if (amount > maxAmount)
                {
                    maxAmount = amount;
                    tint = proto.SubstanceColor;
                }
            }
        }

        if (tint is { } color)
        {
            // Remember true skin color only the first time we tint
            _originalColors.TryAdd(uid, appearance.SkinColor);
            _humanoid.SetSkinColor(uid, color, verify: false, humanoid: appearance);
        }
        else if (_originalColors.TryGetValue(uid, out var original))
        {
            // Only restore if we previously applied a foreign tint
            _humanoid.SetSkinColor(uid, original, verify: false, humanoid: appearance);
            _originalColors.Remove(uid);
        }
        // else: never tinted — leave profile skin color alone
    }
}
