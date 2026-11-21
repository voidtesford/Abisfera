// Goob port stub — approximate flash immunity check.
using Content.Shared.Flash.Components;

namespace Content.Shared._Starlight.Overlay.Systems;

public sealed class FlashImmunitySystem : EntitySystem
{
    public bool HasFlashImmunityVisionBlockers(EntityUid uid)
        => HasComp<FlashImmunityComponent>(uid);
}
