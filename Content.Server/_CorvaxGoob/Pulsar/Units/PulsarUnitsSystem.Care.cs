using System.Linq;
using Content.Server.NPC.Components;
using Content.Server.StationRecords.Systems;
using Content.Shared.Bed.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Medical;
using Content.Shared.Morgue.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.PowerCell;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.StationRecords;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Power.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._CorvaxGoob.Pulsar.Units;

/// <summary>
/// Stage 5.3: the bodies (crew from the manifest to medbay and revived with the defibrillator and the first aid
/// items of the modules; anyone else into a morgue tray) and the cyborg recharging stations (at 20% charge).
/// </summary>
public sealed partial class PulsarUnitsSystem
{
    [Dependency] private readonly PowerCellSystem _cells = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly Content.Shared.Buckle.SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly Content.Shared.Mobs.Systems.MobThresholdSystem _thresholds = default!;

    /// <summary>At or below this charge a cyborg goes to recharge…</summary>
    public const float LowCharge = 0.2f;
    /// <summary>…and comes out at this charge.</summary>
    public const float FullCharge = 0.95f;
    /// <summary>A body this close to a medical bed or in a morgue tray is where it should be.</summary>
    public const float SettledRange = 2.5f;
    /// <summary>Revive when the damage the defibrillator has to beat is this far under the death threshold.</summary>
    public const float ReviveMargin = 15f;

    // ---- charge ----

    /// <summary>Charge level of the cyborg's power cell (1 without a cell slot: nothing to charge).</summary>
    public float ChargeLevel(EntityUid uid)
    {
        // No cell at all: nothing a station can charge.
        if (!_cells.TryGetBatteryFromSlot(uid, out var battery))
            return 1f;
        return _battery.GetChargeLevel(battery.Value.AsNullable());
    }

    /// <summary>Low on charge: drop the odd job (the order is kept) and go to the nearest free recharging station.</summary>
    private bool CheckCharge(Entity<PulsarLinkedComponent> ent, TimeSpan now)
    {
        if (ent.Comp.Task is { Kind: PulsarBorgTaskKind.Recharge })
            return true;
        if (ChargeLevel(ent) > LowCharge || now < ent.Comp.NextChargeSearch)
            return false;

        ent.Comp.NextChargeSearch = now + TimeSpan.FromSeconds(10);
        if (FindCharger(ent) is not { } charger)
            return false;

        if (ent.Comp.Task != null)
            EndTask(ent, now, failed: false);
        StartTask(ent, PulsarBorgTaskKind.Recharge, charger, now);
        return true;
    }

    private EntityUid? FindCharger(Entity<PulsarLinkedComponent> unit)
    {
        var pos = _transform.GetMapCoordinates(unit);
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var query = EntityQueryEnumerator<ChargerComponent, EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var storage, out var xform))
        {
            if (xform.MapID != pos.MapId || !xform.Anchored || storage.Contents.ContainedEntities.Count > 0)
                continue;
            if (_entityStorage.CanInsert(unit, uid, storage) is false)
                continue;
            if (Claimed(unit, uid))
                continue;
            var d = (_transform.GetWorldPosition(xform) - pos.Position).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = uid;
            }
        }

        return best;
    }

    private void RunRecharge(Entity<PulsarLinkedComponent> ent, PulsarBorgTask task, TimeSpan now)
    {
        var charger = task.Target;
        if (!TryComp<EntityStorageComponent>(charger, out var storage))
        {
            EndTask(ent, now, failed: true);
            return;
        }

        var inside = _containers.TryGetContainingContainer(ent.Owner, out var container) && container.Owner == charger;
        if (task.Stage == PulsarBorgTaskStage.Charge)
        {
            if (!inside)
            {
                EndTask(ent, now, failed: false);
                return;
            }

            if (ChargeLevel(ent) < FullCharge && now - task.StageStarted < TimeSpan.FromMinutes(5))
                return;

            // Charged: out where it came in and back to work (the order it had is still there).
            _entityStorage.Remove(ent, charger, storage);
            if (task.ApeSpot is { } back)
                _transform.SetCoordinates(ent, back);
            EndTask(ent, now, failed: false);
            return;
        }

        if (inside)
        {
            SetStage(task, PulsarBorgTaskStage.Charge, now);
            return;
        }

        var dist = DistanceTo(ent, Transform(charger).Coordinates) ?? float.MaxValue;
        if (dist > 1.3f)
        {
            if (!Progressing(ent, dist, now) || SteerTo(ent, Transform(charger).Coordinates, 1.1f) == SteeringStatus.NoPath)
                EndTask(ent, now, failed: true);
            return;
        }

        _steering.Unregister(ent);
        if (storage.Open)
            _entityStorage.CloseStorage(charger, storage);
        var from = Transform(ent).Coordinates;
        if (_entityStorage.Insert(ent, charger, storage))
        {
            task.ApeSpot = from;
            SetStage(task, PulsarBorgTaskStage.Charge, now);
        }
        else if (++task.Attempts > 3)
            EndTask(ent, now, failed: true);
    }

    // ---- bodies ----

    /// <summary>A body a medical cyborg should take care of: dead, humanoid, loose, not already in medbay or the morgue.</summary>
    public bool BodyNeedsCare(EntityUid body)
    {
        if (!HasComp<HumanoidAppearanceComponent>(body) || !_mobState.IsDead(body) || _containers.IsEntityInContainer(body))
            return false;
        return !NearMedicalBed(body);
    }

    public bool CanCarry(Entity<BorgChassisComponent> chassis)
    {
        return HasComp<PullerComponent>(chassis);
    }

    private bool NearMedicalBed(EntityUid body)
    {
        var pos = _transform.GetMapCoordinates(body);
        foreach (var bed in MedicalBeds(pos.MapId))
        {
            if ((_transform.GetWorldPosition(bed) - pos.Position).Length() <= SettledRange)
                return true;
        }

        return false;
    }

    private IEnumerable<EntityUid> MedicalBeds(MapId map)
    {
        var stasis = EntityQueryEnumerator<StasisBedComponent, TransformComponent>();
        while (stasis.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                yield return uid;
        }

        var heal = EntityQueryEnumerator<HealOnBuckleComponent, TransformComponent>();
        while (heal.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map)
                yield return uid;
        }
    }

    /// <summary>A member of this station's crew: their name is in the crew manifest of the core's station.</summary>
    public bool IsCrew(EntityUid core, EntityUid body)
    {
        if (_station.GetOwningStation(core) is not { } station)
            return false;
        var name = MetaData(body).EntityName;
        foreach (var (_, record) in _records.GetRecordsOfType<Content.Shared.StationRecords.GeneralStationRecord>(station))
        {
            if (string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private EntityUid? Nearest(IEnumerable<EntityUid> things, EntityUid from)
    {
        var pos = _transform.GetMapCoordinates(from);
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        foreach (var uid in things)
        {
            var at = _transform.GetMapCoordinates(uid);
            if (at.MapId != pos.MapId)
                continue;
            var d = (at.Position - pos.Position).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = uid;
            }
        }

        return best;
    }

    private IEnumerable<EntityUid> Morgues(EntityUid from)
    {
        var map = Transform(from).MapID;
        var query = EntityQueryEnumerator<MorgueComponent, EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var storage, out var xform))
        {
            if (xform.MapID == map && storage.Contents.ContainedEntities.Count < storage.Capacity)
                yield return uid;
        }
    }

    private void StartCorpse(Entity<PulsarLinkedComponent> ent, EntityUid body, TimeSpan now)
    {
        StartTask(ent, PulsarBorgTaskKind.Corpse, body, now);
        var task = ent.Comp.Task!;
        task.Crew = ent.Comp.Core is { } core && IsCrew(core, body);
        task.Destination = task.Crew ? Nearest(MedicalBeds(Transform(body).MapID), body) : Nearest(Morgues(body), body);
        task.Deadline = now + TimeSpan.FromMinutes(6);
    }

    private void RunCorpse(Entity<PulsarLinkedComponent> ent, Entity<BorgChassisComponent> chassis, PulsarBorgTask task, TimeSpan now)
    {
        var body = task.Target;

        // Revived (by the unit or by someone else): no longer a body.
        if (!_mobState.IsDead(body))
        {
            StopPulling(ent, body);
            if (task.Crew && task.Stage == PulsarBorgTaskStage.Revive)
                CorpseReport(ent, task, "pulsar-unit-report-revived");
            EndTask(ent, now, failed: false);
            return;
        }

        if (_containers.IsEntityInContainer(body))
        {
            StopPulling(ent, body);
            EndTask(ent, now, failed: false);
            return;
        }

        switch (task.Stage)
        {
            case PulsarBorgTaskStage.Approach:
            {
                if (!_interaction.InRangeUnobstructed(ent.Owner, body))
                {
                    var dist = DistanceTo(ent, Transform(body).Coordinates) ?? float.MaxValue;
                    if (!Progressing(ent, dist, now) || SteerTo(ent, Transform(body).Coordinates, 1f) == SteeringStatus.NoPath)
                        EndTask(ent, now, failed: true);
                    return;
                }

                _steering.Unregister(ent);
                // Crew already where a doctor can work on them / nowhere to take them: revive here.
                if (task.Crew && (task.Destination == null || NearMedicalBed(body)))
                {
                    SetStage(task, PulsarBorgTaskStage.Revive, now);
                    return;
                }

                if (task.Destination == null)
                {
                    EndTask(ent, now, failed: true);
                    return;
                }

                if (_pulling.TryStartPull(ent, body))
                {
                    SetStage(task, PulsarBorgTaskStage.Haul, now);
                    ent.Comp.BestDistance = float.MaxValue;
                    ent.Comp.BestDistanceAt = now;
                }
                else if (++task.Attempts > 3)
                {
                    EndTask(ent, now, failed: true);
                }

                return;
            }
            case PulsarBorgTaskStage.Haul:
            {
                if (task.Destination is not { } dest || TerminatingOrDeleted(dest))
                {
                    StopPulling(ent, body);
                    EndTask(ent, now, failed: true);
                    return;
                }

                if (!TryComp<PullableComponent>(body, out var pullable) || pullable.Puller != ent.Owner)
                {
                    SetStage(task, PulsarBorgTaskStage.Approach, now);
                    return;
                }

                var destCoords = Transform(dest).Coordinates;
                var dist = DistanceTo(ent, destCoords) ?? float.MaxValue;
                if (dist > 1.4f)
                {
                    if (!Progressing(ent, dist, now) || SteerTo(ent, destCoords, 1.2f) == SteeringStatus.NoPath)
                    {
                        StopPulling(ent, body);
                        EndTask(ent, now, failed: true);
                    }

                    return;
                }

                _steering.Unregister(ent);
                StopPulling(ent, body);
                if (task.Crew)
                {
                    // Onto the bed when it takes them, else next to it.
                    if (TryComp<Content.Shared.Buckle.Components.StrapComponent>(dest, out _))
                        _buckle.TryBuckle(body, ent, dest, popup: false);
                    SetStage(task, PulsarBorgTaskStage.Revive, now);
                    return;
                }

                // Into the morgue tray.
                if (TryComp<EntityStorageComponent>(dest, out var morgue))
                {
                    if (morgue.Open)
                        _entityStorage.CloseStorage(dest, morgue);
                    if (_entityStorage.Insert(body, dest, morgue))
                    {
                        CorpseReport(ent, task, "pulsar-unit-report-morgue");
                        EndTask(ent, now, failed: false);
                        return;
                    }
                }

                EndTask(ent, now, failed: true);
                return;
            }
            case PulsarBorgTaskStage.Revive:
            {
                if (!_interaction.InRangeUnobstructed(ent.Owner, body))
                {
                    var dist = DistanceTo(ent, Transform(body).Coordinates) ?? float.MaxValue;
                    if (!Progressing(ent, dist, now) || SteerTo(ent, Transform(body).Coordinates, 1f) == SteeringStatus.NoPath)
                        EndTask(ent, now, failed: true);
                    return;
                }

                _steering.Unregister(ent);
                if (now - task.StageStarted < ActWait && task.Attempts > 0)
                    return;
                if (task.Attempts >= 16)
                {
                    CorpseReport(ent, task, "pulsar-unit-report-revive-failed");
                    EndTask(ent, now, failed: true);
                    return;
                }

                task.Attempts++;
                task.StageStarted = now;

                // First the wounds (the defibrillator only brings back a body that is not too broken), then the shock.
                if (ReviveDamageLeft(body) > 0f && PickHealItem(chassis, body) is { } kit)
                {
                    _interaction.InteractUsing(ent, kit, body, Transform(body).Coordinates);
                    return;
                }

                if (TakeItem(chassis, uid => HasComp<DefibrillatorComponent>(uid), p => p.TryGetComponent<DefibrillatorComponent>(out _, _factory)) is { } defib)
                {
                    if (!_toggle.IsActivated(defib))
                        _toggle.TryActivate(defib, ent.Owner, predicted: false, showPopup: false);
                    _interaction.InteractUsing(ent, defib, body, Transform(body).Coordinates);
                    return;
                }

                // No defibrillator: leave the body to the doctors.
                CorpseReport(ent, task, "pulsar-unit-report-revive-failed");
                EndTask(ent, now, failed: true);
                return;
            }
            default:
                EndTask(ent, now, failed: true);
                return;
        }
    }

    /// <summary>How much more damage has to go before the defibrillator can bring the body back.</summary>
    private float ReviveDamageLeft(EntityUid body)
    {
        if (!TryComp<DamageableComponent>(body, out var dmg)
            || !_thresholds.TryGetThresholdForState(body, Content.Shared.Mobs.MobState.Dead, out var dead))
            return 0f;
        return Math.Max(0f, dmg.TotalDamage.Float() - (dead.Value.Float() - ReviveMargin));
    }

    private void StopPulling(EntityUid unit, EntityUid body)
    {
        if (TryComp<PullableComponent>(body, out var pullable) && pullable.Puller == unit)
            _pulling.TryStopPull(body, pullable, unit);
    }

    private void CorpseReport(Entity<PulsarLinkedComponent> ent, PulsarBorgTask task, string loc)
    {
        if (task.Reported || ent.Comp.Core is not { } core)
            return;
        task.Reported = true;
        UnitReport(core, Loc.GetString(loc, ("unit", Name(ent)), ("name", MetaData(task.Target).EntityName)), ReportChannel.Medical);
    }
}
