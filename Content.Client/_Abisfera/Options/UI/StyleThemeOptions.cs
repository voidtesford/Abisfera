using System.Linq;
using Content.Shared._CorvaxGoob.CCCVars;
using Content.Shared.Abisfera.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Options.UI;

public sealed class StyleThemePicker : GridContainer
{
    private readonly Dictionary<string, ContainerButton> _cards = new();

    public event Action? OnPicked;
    public string? Selected { get; private set; }
    public int ThemeCount => _cards.Count;

    public StyleThemePicker()
    {
        Columns = 3;
        HSeparationOverride = 6;
        VSeparationOverride = 6;

        var proto = IoCManager.Resolve<IPrototypeManager>();
        var themes = proto.EnumeratePrototypes<StyleThemePrototype>().OrderBy(t => t.Order).ToList();

        foreach (var theme in themes)
        {
            var card = new ContainerButton { ToggleMode = true, ToolTip = Loc.GetString(theme.Name) };
            card.AddChild(new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                MinWidth = 90,
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Horizontal,
                        HorizontalAlignment = HAlignment.Center,
                        Children =
                        {
                            Swatch(theme.Background, 24, 28),
                            Swatch(theme.SurfaceRaised, 24, 28),
                            Swatch(theme.ElementHovered, 24, 28),
                        },
                    },
                    new Label
                    {
                        Text = Loc.GetString(theme.Name),
                        HorizontalAlignment = HAlignment.Center,
                    },
                },
            });

            var id = theme.ID;
            card.OnPressed += _ =>
            {
                Selected = id;
                UpdatePressed();
                OnPicked?.Invoke();
            };
            _cards[id] = card;
            AddChild(card);
        }
    }

    public void Select(string id)
    {
        Selected = id;
        UpdatePressed();
    }

    private void UpdatePressed()
    {
        foreach (var (id, card) in _cards)
            card.Pressed = id == Selected;
    }

    public static PanelContainer Swatch(Color color, float width, float height)
        => new()
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = color },
            MinSize = new(width, height),
        };
}

public sealed class StyleThemeOptionsPanel : BoxContainer
{
    private readonly IConfigurationManager _cfg;
    private readonly StyleThemePicker _picker;

    public StyleThemeOptionsPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        _cfg = IoCManager.Resolve<IConfigurationManager>();

        // Title is already in GraphicsTab.xaml — do not duplicate.

        _picker = new StyleThemePicker();
        if (_picker.ThemeCount == 0)
        {
            AddChild(new Label
            {
                Text = "Нет прототипов styleTheme (проверь Resources/Prototypes/_Abisfera/Interface/style_themes.yml)",
                FontColorOverride = Color.FromHex("#FF6666"),
            });
        }
        else
        {
            _picker.Select(_cfg.GetCVar(AbisferaCVars.StyleTheme));
            _picker.OnPicked += Apply;
            AddChild(_picker);
        }
    }

    private void Apply()
    {
        if (_picker.Selected is not { } id)
            return;

        _cfg.SetCVar(AbisferaCVars.StyleTheme, id);
        _cfg.SaveToFile();
    }
}
