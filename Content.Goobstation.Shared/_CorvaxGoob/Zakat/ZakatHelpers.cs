using Content.Shared.Body.Components;
using Content.Shared.Mobs.Components;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

public static class ZakatHelpers
{
    /// <summary>
    /// An organism «Закат» can live in: a mob with blood (humanoids and animals; no borgs, no ghosts).
    /// The disease's own creatures are its flesh, not organisms to infect.
    /// </summary>
    public static bool IsOrganic(IEntityManager entMan, EntityUid uid)
    {
        return entMan.HasComponent<MobStateComponent>(uid)
               && entMan.HasComponent<BloodstreamComponent>(uid)
               && !entMan.HasComponent<ZakatComponent>(uid)
               && !entMan.HasComponent<ZakatCreatureComponent>(uid);
    }
}
