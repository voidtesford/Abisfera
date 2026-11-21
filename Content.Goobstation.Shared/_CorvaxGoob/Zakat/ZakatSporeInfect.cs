using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// upd1: «вирусный газ заката» breathed in: a chance to catch «Закат» (after masks and bio hoods, like any airborne
/// spread; whoever breathes from internals never breathes the gas at all).
/// </summary>
public sealed partial class ZakatSporeInfect : EntityEffectBase<ZakatSporeInfect>
{
    /// <summary>Chance per metabolism tick, before protective gear.</summary>
    [DataField]
    public float Chance = 0.2f;

    public override string? EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;
}
