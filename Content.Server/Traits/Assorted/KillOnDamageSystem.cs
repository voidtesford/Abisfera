// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server.Traits.Assorted;

public sealed class KillOnDamageSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KillOnDamageComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(EntityUid uid, KillOnDamageComponent component, DamageChangedEvent args)
    {
        if (!TryComp<MobStateComponent>(uid, out var mobState) || _mob.IsDead(uid, mobState))
            return;

        if (args.DamageDelta is null)
            return;

        if (!args.DamageDelta.DamageDict.TryGetValue(component.DamageType, out FixedPoint2 value) || value < component.Threshold)
            return;

        var popup = Loc.GetString(component.Popup, ("name", Identity.Name(uid, EntityManager)));
        _popup.PopupEntity(popup, uid, PopupType.LargeCaution);
        _mob.ChangeMobState(uid, MobState.Dead, mobState);
    }
}
