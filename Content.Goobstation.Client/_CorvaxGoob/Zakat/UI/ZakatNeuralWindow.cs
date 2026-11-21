using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Goobstation.Client._CorvaxGoob.Zakat.UI;

/// <summary>
/// Window around a <see cref="ZakatNeuralMapControl"/>: the biomass line and the map.
/// </summary>
public sealed class ZakatNeuralWindow : DefaultWindow
{
    public readonly Label Header;
    public readonly ZakatNeuralMapControl Map;

    public ZakatNeuralWindow()
    {
        MinSize = new(900, 660);
        SetSize = new(1000, 720);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };

        Header = new Label { FontColorOverride = Color.FromHex("#ffb3b0"), HorizontalAlignment = HAlignment.Center };
        Map = new ZakatNeuralMapControl { VerticalExpand = true, HorizontalExpand = true };

        box.AddChild(Header);
        box.AddChild(Map);
        Contents.AddChild(box);
    }
}
