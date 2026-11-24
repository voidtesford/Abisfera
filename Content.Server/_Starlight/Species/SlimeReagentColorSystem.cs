using Content.Server.Humanoid;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Species;

/// <summary>
/// Slime people change skin color to match the dominant reagent in their bloodstream.
/// Original player-selected skin color is remembered and restored when foreign reagents are gone.
/// </summary>
public sealed class SlimeReagentColorSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly ProtoId<SpeciesPrototype> SlimeSpecies = "SlimePerson";
    private const string ChemicalSolutionId = "chemicals";

    private readonly Dictionary<EntityUid, Color> _originalColors = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<HumanoidAppearanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HumanoidAppearanceComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnMapInit(Entity<HumanoidAppearanceComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Species != SlimeSpecies)
            return;

        _originalColors[ent.Owner] = ent.Comp.SkinColor;
    }

    private void OnSolutionChanged(Entity<HumanoidAppearanceComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != ChemicalSolutionId)
            return;

        var uid = ent.Owner;
        var appearance = ent.Comp;

        if (appearance.Species != SlimeSpecies)
            return;

        Color? tint = null;
        var maxAmount = 0f;

        foreach (var reagent in args.Solution.Contents)
        {
            var amount = reagent.Quantity.Float();
            if (amount <= 0.5f)
                continue;

            if (!_proto.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto))
                continue;

            if (proto.ID == "Slime" || proto.ID == "Blood")
                continue;

            if (amount > maxAmount)
            {
                maxAmount = amount;
                tint = proto.SubstanceColor;
            }
        }

        if (tint is { } color)
        {
            _originalColors.TryAdd(uid, appearance.SkinColor);
            _humanoid.SetSkinColor(uid, color, verify: false, humanoid: appearance);
        }
        else if (_originalColors.TryGetValue(uid, out var original))
        {
            _humanoid.SetSkinColor(uid, original, verify: false, humanoid: appearance);
        }
    }
}