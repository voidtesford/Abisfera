using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Slime;

/// <summary>
/// Makes slime people temporarily change their skin color to match
/// reagents currently present in their bloodstream.
/// Original skin color is remembered and restored when foreign reagents are gone.
/// </summary>
public sealed class SlimeColorSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeColorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SlimeColorComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnStartup(EntityUid uid, SlimeColorComponent component, ComponentStartup args)
    {
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            component.OriginalSkinColor = humanoid.SkinColor;
            component.HasOriginal = true;
        }
    }

    private void OnSolutionChanged(EntityUid uid, SlimeColorComponent component, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != "bloodstream" && args.SolutionId != "blood")
            return;

        UpdateColor(uid, component);
    }

    private void UpdateColor(EntityUid uid, SlimeColorComponent component)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        if (!component.HasOriginal)
        {
            component.OriginalSkinColor = humanoid.SkinColor;
            component.HasOriginal = true;
        }

        if (!_solution.TryGetSolution(uid, "bloodstream", out _, out var blood) &&
            !_solution.TryGetSolution(uid, "blood", out _, out blood))
        {
            RestoreOriginal(uid, component, humanoid);
            return;
        }

        Color? mixed = null;
        FixedPoint2 total = FixedPoint2.Zero;

        foreach (var (reagentId, quantity) in blood.Contents)
        {
            if (quantity <= 0)
                continue;

            if (reagentId.Prototype == "Slime" || reagentId.Prototype == "Blood")
                continue;

            if (!_proto.TryIndex<ReagentPrototype>(reagentId.Prototype, out var proto))
                continue;

            var color = proto.SubstanceColor;
            if (color.A <= 0)
                continue;

            if (mixed == null)
            {
                mixed = color;
                total = quantity;
            }
            else
            {
                var t = (quantity / (total + quantity)).Float();
                mixed = Color.InterpolateBetween(mixed.Value, color, t);
                total += quantity;
            }
        }

        if (mixed == null || total < 1)
        {
            RestoreOriginal(uid, component, humanoid);
            return;
        }

        if (humanoid.SkinColor != mixed.Value)
        {
            _humanoid.SetSkinColor(uid, mixed.Value, false, false, humanoid);
        }
    }

    private void RestoreOriginal(EntityUid uid, SlimeColorComponent component, HumanoidAppearanceComponent humanoid)
    {
        if (!component.HasOriginal)
            return;

        if (humanoid.SkinColor != component.OriginalSkinColor)
        {
            _humanoid.SetSkinColor(uid, component.OriginalSkinColor, false, false, humanoid);
        }
    }
}

[RegisterComponent]
public sealed partial class SlimeColorComponent : Component
{
    [DataField]
    public Color OriginalSkinColor = Color.White;

    [DataField]
    public bool HasOriginal;
}
