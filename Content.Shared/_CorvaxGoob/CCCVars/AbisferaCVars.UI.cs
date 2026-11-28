using Robust.Shared.Configuration;

namespace Content.Shared._CorvaxGoob.CCCVars;

public sealed partial class AbisferaCVars
{
    public static readonly CVarDef<string> StyleTheme =
        CVarDef.Create("abisfera.ui.style_theme", "NanoTrasen", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> StyleAccent =
        CVarDef.Create("abisfera.ui.style_accent", "#D4D4D8", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> StyleCustomAccent =
        CVarDef.Create("abisfera.ui.style_custom_accent", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> StyleSecondaryAccent =
        CVarDef.Create("abisfera.ui.style_secondary_accent", "#D4D4D8", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> StyleCustomSecondaryAccent =
        CVarDef.Create("abisfera.ui.style_custom_secondary_accent", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> StyleClassicDecor =
        CVarDef.Create("abisfera.ui.style_classic_decor", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> StyleClassicDecorExplicit =
        CVarDef.Create("abisfera.ui.style_classic_decor_explicit", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
