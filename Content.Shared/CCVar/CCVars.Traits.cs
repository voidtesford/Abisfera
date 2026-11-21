// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>Whether characters may select traits.</summary>
    public static readonly CVarDef<bool> GameTraitsEnabled =
        CVarDef.Create("game.traits_enabled", true, CVar.REPLICATED);

    /// <summary>How many trait slots a character may fill. Each trait uses <c>Slots</c> (usually 1).</summary>
    public static readonly CVarDef<int> GameTraitsMax =
        CVarDef.Create("game.traits_max", 14, CVar.REPLICATED);

    /// <summary>Starting trait points. Advantages spend these; disadvantages refund them. The total may not go below zero.</summary>
    public static readonly CVarDef<int> GameTraitsDefaultPoints =
        CVarDef.Create("game.traits_default_points", 10, CVar.REPLICATED);
}
