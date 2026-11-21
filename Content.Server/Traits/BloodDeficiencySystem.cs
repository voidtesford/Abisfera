// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Server.Traits.Assorted;
using Content.Shared.Body.Events;

namespace Content.Server.Traits;

public sealed class BloodDeficiencySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BloodDeficiencyComponent, BloodRefreshAttemptEvent>(OnBloodRegen);
    }

    private void OnBloodRegen(Entity<BloodDeficiencyComponent> ent, ref BloodRefreshAttemptEvent args)
    {
        if (!ent.Comp.Active)
            return;

        args.Amount = FixedPoint2.Min(args.Amount, 0) - args.MaxVolume * ent.Comp.BloodLossPercentage;
    }
}
