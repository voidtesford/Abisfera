// Goob port stub — Starlight damage pipeline event.
// Blood-bond damage split will not fire until wired to Goob damage events.
using Content.Shared.Damage;

namespace Content.Shared._Starlight.Medical.Body;

[ByRefEvent]
public struct DamageBeforeApplyEvent
{
    public DamageSpecifier Damage;
    public EntityUid? Origin;
    public bool Cancelled;
}
