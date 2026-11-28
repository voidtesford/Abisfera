using System.Linq;
using Content.Client.Stylesheets.Stylesheets;
using Content.Shared._CorvaxGoob.CCCVars;
using Content.Shared.Abisfera.UserInterface;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Stylesheets;

public sealed partial class StylesheetManager
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private bool _styleThemeRebuildQueued;
    private StyleTheme? _activeTheme;

    public StyleTheme? ActiveTheme => _activeTheme;

    private void InitializeStyleTheme()
    {
        _cfg.OnValueChanged(AbisferaCVars.StyleTheme, _ =>
        {
            SyncClassicDecorToTheme();
            QueueStyleThemeRebuild();
        });
        _cfg.OnValueChanged(AbisferaCVars.StyleClassicDecor, _ => QueueStyleThemeRebuild());
        _cfg.OnValueChanged(AbisferaCVars.StyleAccent, _ => QueueStyleThemeRebuild());
        _cfg.OnValueChanged(AbisferaCVars.StyleCustomAccent, _ => QueueStyleThemeRebuild());
        _cfg.OnValueChanged(AbisferaCVars.StyleSecondaryAccent, _ => QueueStyleThemeRebuild());
        _cfg.OnValueChanged(AbisferaCVars.StyleCustomSecondaryAccent, _ => QueueStyleThemeRebuild());

        _prototype.PrototypesReloaded += args =>
        {
            if (args.WasModified<StyleThemePrototype>())
                QueueStyleThemeRebuild();
        };

        SyncClassicDecorToTheme();
        QueueStyleThemeRebuild();
    }

    private StyleTheme? ResolveStyleTheme()
    {
        if (!_prototype.TryIndex<StyleThemePrototype>(_cfg.GetCVar(AbisferaCVars.StyleTheme), out var proto))
        {
            proto = _prototype.EnumeratePrototypes<StyleThemePrototype>().MinBy(p => p.Order);
            if (proto == null)
                return null;
        }

        return new StyleTheme(proto, ResolveAccent(proto), ResolveSecondaryAccent(proto), ResolveClassicDecor());
    }

    private void SyncClassicDecorToTheme()
    {
        if (_cfg.GetCVar(AbisferaCVars.StyleClassicDecorExplicit))
            return;

        if (!_prototype.TryIndex<StyleThemePrototype>(_cfg.GetCVar(AbisferaCVars.StyleTheme), out var proto))
            proto = _prototype.EnumeratePrototypes<StyleThemePrototype>().MinBy(p => p.Order);

        if (proto == null)
            return;

        _cfg.SetCVar(AbisferaCVars.StyleClassicDecor, proto.ClassicDecor);
    }

    private bool ResolveClassicDecor()
        => _cfg.GetCVar(AbisferaCVars.StyleClassicDecor);

    private Color ResolveAccent(StyleThemePrototype proto)
    {
        if (_cfg.GetCVar(AbisferaCVars.StyleCustomAccent)
            && Color.TryFromHex(_cfg.GetCVar(AbisferaCVars.StyleAccent), out var custom))
            return custom;

        return proto.Accent ?? Color.FromHex(AbisferaCVars.StyleAccent.DefaultValue);
    }

    private Color? ResolveSecondaryAccent(StyleThemePrototype proto)
    {
        if (_cfg.GetCVar(AbisferaCVars.StyleCustomSecondaryAccent)
            && Color.TryFromHex(_cfg.GetCVar(AbisferaCVars.StyleSecondaryAccent), out var custom))
            return custom;

        return proto.SecondaryAccent;
    }

    private void QueueStyleThemeRebuild()
    {
        if (_styleThemeRebuildQueued)
            return;

        _styleThemeRebuildQueued = true;
        _userInterfaceManager.DeferAction(RebuildStyleTheme);
    }

    private void RebuildStyleTheme()
    {
        _styleThemeRebuildQueued = false;
        _activeTheme = ResolveStyleTheme();

        object config = _activeTheme is { } theme
            ? theme
            : new BaseStylesheet.NoConfig();

        var sheet = new NanotrasenStylesheet(config, this);
        Stylesheets[sheet.StylesheetName] = sheet.Stylesheet;
        SheetNanotrasen = sheet.Stylesheet;
        _userInterfaceManager.Stylesheet = SheetNanotrasen;
    }
}
