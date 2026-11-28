using Robust.Shared.Configuration;

namespace Content.Shared._CorvaxGoob.CCCVars;

/// <summary>
///     Abisfera console variables (kept out of <see cref="CCCVars"/> so our patches never touch that file).
/// </summary>
[CVarDefs]
public sealed partial class AbisferaCVars
{
    /// <summary>
    /// А.М.Ц.И.И «Пульсар»: the autonomous module runs station AI cores nobody plays. Off = it never takes a core.
    /// </summary>
    public static readonly CVarDef<bool> PulsarEnabled =
        CVarDef.Create("pulsar.enabled", true, CVar.SERVERONLY);
}
