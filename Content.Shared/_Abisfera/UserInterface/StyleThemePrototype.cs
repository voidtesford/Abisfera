using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Abisfera.UserInterface;

[Prototype]
public sealed partial class StyleThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public int Order;

    [DataField]
    public bool Light;

    [DataField]
    public Color? Accent;

    [DataField]
    public float BorderThickness = 1f;

    [DataField]
    public float SecondaryBorderThickness = 1f;

    [DataField]
    public StyleThemeBox? Window;

    [DataField]
    public StyleThemeBox? Header;

    [DataField]
    public StyleThemeBox? Button;

    [DataField]
    public bool ClassicDecor;

    [DataField]
    public ProtoId<StyleThemePrototype>? Hud;

    [DataField]
    public Color Background;

    [DataField]
    public Color Surface;

    [DataField]
    public Color? SurfaceInspect;

    [DataField]
    public Color SurfaceRaised;

    [DataField]
    public Color Element;

    [DataField]
    public Color ElementHovered;

    [DataField]
    public Color ElementDisabled;

    [DataField]
    public Color Border;

    [DataField]
    public Color Text;

    [DataField]
    public Color TextMuted;

    [DataField]
    public Color TextDisabled;

    [DataField]
    public Color? TextPlaceholder;

    [DataField]
    public Color Positive;

    [DataField]
    public Color Negative;

    [DataField]
    public float AccentTint = 0.35f;

    [DataField]
    public Color? WindowBackground;

    [DataField]
    public bool RawColors;

    [DataField]
    public bool LiquidGlass;

    [DataField]
    public Color? SecondaryAccent;

    [DataField]
    public Color? TextTooltip;
}

[DataDefinition]
public sealed partial class StyleThemeBox
{
    [DataField(required: true)]
    public ResPath Texture;

    [DataField]
    public float[] Patch = [0f];

    [DataField]
    public bool Tile;

    [DataField]
    public float Scale = 1f;

    [DataField]
    public float Expand;

    [DataField]
    public float? Content;

    [DataField]
    public Color Modulate = Color.White;
}
