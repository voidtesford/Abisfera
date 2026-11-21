// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;

namespace Content.Shared.Body.Events;

/// <summary>
/// Raised on an entity just before its natural blood volume is regulated.
/// Handlers may change <see cref="Amount"/>.
/// </summary>
[ByRefEvent]
public record struct BloodRefreshAttemptEvent(FixedPoint2 Amount, FixedPoint2 MaxVolume);
