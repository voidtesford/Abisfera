using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Doors.Components;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 3: camera alerts. Built on what the vision already collects, nothing scans the map on its own:
/// <list type="bullet">
/// <item>people (1 s refresh of the mobs on camera): an enemy (syndicate suit) coming into view → Security;
/// a body coming into view / someone dying on camera → Medical;</item>
/// <item>atmosphere: sampled on the visible floor tiles of the scan window the vision step looks at anyway
/// (one window per 0.5 s); a place (nearest beacon) getting new depressurised tiles → Engineering (+ Common when
/// large) and the open doors around it are closed; new burning tiles → Engineering. "New" = the same tile was
/// seen fine at the previous look (a bit per tile), so revealed vacuum and permanent fires never alert.</item>
/// </list>
/// Every alert has a key (the entity or the place) and <see cref="PulsarAutonomousAiComponent.AlertCooldown"/>.
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;

    // ---- saying alerts ----

    /// <summary>Says an alert unless its key is cooling down. Returns whether it was said.</summary>
    private bool Announce(Entity<PulsarAutonomousAiComponent> ent, PulsarAlertKind kind, string key, string text, TimeSpan now, params string[] channels)
    {
        var comp = ent.Comp;
        if (comp.AlertCooldowns.TryGetValue(key, out var next) && next > now)
            return false;

        comp.AlertCooldowns[key] = now + comp.AlertCooldown;

        var said = new List<string>();
        foreach (var channel in channels)
        {
            if (!comp.Channels.Contains(channel) || said.Contains(channel))
                continue;
            _radio.SendRadioMessage(ent, text, channel, ent);
            said.Add(channel);
        }

        if (said.Count == 0)
        {
            _chat.TrySendInGameICMessage(ent, text, InGameICChatType.Speak, ChatTransmitRange.Normal,
                checkRadioPrefix: false, ignoreActionBlocker: true);
        }

        comp.Alerts.Add(new PulsarAlert(now, kind, key, text, said.ToArray()));
        var excess = comp.Alerts.Count - Math.Max(1, comp.AlertLimit);
        if (excess > 0)
            comp.Alerts.RemoveRange(0, excess);

        // Forget expired cooldowns now and then.
        if (comp.AlertCooldowns.Count > 256)
        {
            foreach (var (k, t) in comp.AlertCooldowns.ToArray())
            {
                if (t <= now)
                    comp.AlertCooldowns.Remove(k);
            }
        }

        return true;
    }

    /// <summary>Name of the place: the nearest station beacon, or "unmarked area".</summary>
    public string PlaceName(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i tile)
    {
        return NearestBeacon(ent.Comp, GetLexicon(ent.Comp), grid, tile) is { } beacon
            ? beacon.Name
            : Loc.GetString("pulsar-place-unknown");
    }

    private string PlaceKey(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i tile)
    {
        return NearestBeacon(ent.Comp, GetLexicon(ent.Comp), grid, tile) is { } beacon
            ? beacon.Beacon.ToString()
            : $"{grid}:{tile.X >> 3}:{tile.Y >> 3}";
    }

    // ---- people ----

    private void CheckMobAlerts(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var serves = comp.Policy.ServesCrew;

        foreach (var (uid, info) in comp.Mobs)
        {
            comp.MobAlertStates.TryGetValue(uid, out var before);
            var state = new PulsarMobAlertState
            {
                InView = info.InView,
                Enemy = info.Kind == PulsarMobKind.Enemy && !info.Dead,
                Dead = info.Dead,
            };
            comp.MobAlertStates[uid] = state;

            if (!state.InView || !serves)
                continue;

            // An enemy that came into view (or put the suit on in view). Seen again later somewhere else after the
            // cooldown: a new alert.
            if (state.Enemy && (!before.InView || !before.Enemy))
            {
                Announce(ent, PulsarAlertKind.Enemy, $"enemy:{uid}",
                    Loc.GetString("pulsar-alert-enemy", ("place", PlaceName(ent, info.Grid, info.Tile))),
                    now, comp.SecurityChannel);
            }

            // A body that came into view or someone who died on camera.
            // Name comes from the worn ID; without one Pulsar cannot identify the body.
            if (state.Dead && info.Kind != PulsarMobKind.Enemy && (!before.InView || !before.Dead))
            {
                var place = PlaceName(ent, info.Grid, info.Tile);
                var text = string.IsNullOrWhiteSpace(info.Name)
                    ? Loc.GetString("pulsar-alert-body-unknown", ("place", place))
                    : Loc.GetString("pulsar-alert-body", ("name", info.Name), ("place", place));
                Announce(ent, PulsarAlertKind.Body, $"body:{uid}", text, now, comp.MedicalChannel);
            }
        }

        if (comp.MobAlertStates.Count > comp.Mobs.Count)
        {
            foreach (var uid in comp.MobAlertStates.Keys.ToArray())
            {
                if (!comp.Mobs.ContainsKey(uid))
                    comp.MobAlertStates.Remove(uid);
            }
        }
    }

    // ---- atmosphere ----

    private sealed class AtmosSample
    {
        public EntityUid Grid;
        public GridAtmosphereComponent Atmos = default!;
        public PulsarGridMemory Memory = default!;
        public PulsarAtmosMask Mask = default!;
        public Vector2i Origin;
        public int Size;
        public float LowPressure;
        public float FireTemperature;
        public readonly List<Vector2i> LowTiles = new();
        public readonly List<Vector2i> GoodTiles = new();
        public readonly List<Vector2i> NewLow = new();
        public readonly List<Vector2i> NewHot = new();
    }

    private AtmosSample? _atmosScratch;

    private AtmosSample? BeginAtmosSample(Entity<PulsarAutonomousAiComponent> ent, EntityUid grid, Vector2i origin, int size)
    {
        // Grids without simulated air say nothing.
        if (!TryComp<GridAtmosphereComponent>(grid, out var atmos) || !ent.Comp.Grids.TryGetValue(grid, out var mem))
            return null;

        var key = (grid, origin);
        if (!ent.Comp.AtmosMasks.TryGetValue(key, out var mask))
        {
            mask = new PulsarAtmosMask(size * size);
            ent.Comp.AtmosMasks[key] = mask;
        }

        var sample = _atmosScratch ??= new AtmosSample();
        sample.Grid = grid;
        sample.Atmos = atmos;
        sample.Memory = mem;
        sample.Mask = mask;
        sample.Origin = origin;
        sample.Size = size;
        sample.LowPressure = ent.Comp.LowPressure;
        sample.FireTemperature = ent.Comp.FireTemperature;
        sample.LowTiles.Clear();
        sample.GoodTiles.Clear();
        sample.NewLow.Clear();
        sample.NewHot.Clear();
        return sample;
    }

    private void SampleAtmos(AtmosSample sample, Vector2i tile)
    {
        var mixture = _atmos.GetTileMixture((sample.Grid, sample.Atmos, null), null, tile);
        if (mixture == null)
            return;

        var i = (tile.X - sample.Origin.X) * sample.Size + (tile.Y - sample.Origin.Y);
        if (i < 0 || i >= sample.Size * sample.Size)
            return;

        var low = mixture.Pressure < sample.LowPressure;
        if (low)
        {
            sample.LowTiles.Add(tile);
            if (PulsarAtmosMask.Get(sample.Mask.Good, i))
                sample.NewLow.Add(tile);
        }
        else
        {
            sample.GoodTiles.Add(tile);
        }

        PulsarAtmosMask.Set(sample.Mask.Good, i, !low);

        var hot = !low && (mixture.Temperature > sample.FireTemperature || _atmos.IsHotspotActive(sample.Grid, tile));
        if (hot && PulsarAtmosMask.Get(sample.Mask.NotHot, i))
            sample.NewHot.Add(tile);
        PulsarAtmosMask.Set(sample.Mask.NotHot, i, !hot);
    }

    private void EndAtmosSample(Entity<PulsarAutonomousAiComponent> ent, AtmosSample sample, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!comp.Policy.ServesCrew)
            return;

        // upd1: breaches are tracked as a whole (see PulsarBreachTracker); what to say is decided in FlushBreaches.
        if (sample.NewLow.Count > 0 || comp.Breaches.Breaches.Count > 0)
        {
            var grid = sample.Grid;
            comp.Breaches.Observe(grid, sample.NewLow, sample.GoodTiles, t => PlaceKey(ent, grid, t), now);
        }

        if (sample.NewLow.Count == 0 && sample.NewHot.Count == 0)
            return;

        if (sample.NewLow.Count >= comp.DepressurisedTiles && comp.IsolateDepressurisation)
        {
            foreach (var group in sample.NewLow.GroupBy(t => PlaceKey(ent, sample.Grid, t)))
            {
                if (group.Count() < comp.DepressurisedTiles)
                    continue;
                Isolate(sample, sample.LowTiles.Where(t => PlaceKey(ent, sample.Grid, t) == group.Key));
            }
        }

        if (sample.NewHot.Count >= comp.FireTiles)
        {
            foreach (var group in sample.NewHot.GroupBy(t => PlaceKey(ent, sample.Grid, t)))
            {
                if (group.Count() < comp.FireTiles)
                    continue;
                Announce(ent, PulsarAlertKind.Fire, $"fire:{group.Key}",
                    Loc.GetString("pulsar-alert-fire", ("place", PlaceName(ent, sample.Grid, group.First()))), now,
                    comp.EngineeringChannel);
                // Stage 5: firebots to the fire.
                _units.OnHazard(ent, Units.PulsarNeed.Fire, sample.Grid, group.First(), PlaceName(ent, sample.Grid, group.First()), $"fire:{group.Key}");
            }
        }
    }

    /// <summary>
    /// upd1: says what the breach tracker wants said: a new breach once (several found meanwhile together), a large
    /// one once more as a station announcement, one reminder for a breach nobody repaired.
    /// </summary>
    private void FlushBreaches(Entity<PulsarAutonomousAiComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!comp.Policy.ServesCrew || comp.Breaches.Breaches.Count == 0)
            return;

        if (comp.Breaches.Poll(now, comp.DepressurisedTiles, comp.LargeDepressurisation, comp.BreachLineCooldown,
                comp.BreachReminder, comp.BreachRepairGrace) is not { } line)
            return;

        var places = line.Breaches
            .Select(b => PlaceName(ent, b.Grid, b.Origin))
            .Distinct()
            .ToList();
        var place = places.Count <= 3
            ? string.Join(", ", places)
            : Loc.GetString("pulsar-alert-places-more", ("places", string.Join(", ", places.Take(3))), ("count", places.Count - 3));

        var first = line.Breaches[0];
        var key = $"breach:{line.Kind}:{first.Id}";
        string text;
        switch (line.Kind)
        {
            case PulsarBreachLineKind.Reminder:
                text = Loc.GetString("pulsar-alert-depressurisation-reminder", ("place", place));
                Announce(ent, PulsarAlertKind.Depressurisation, key, text, now, comp.EngineeringChannel);
                return;
            default:
                text = Loc.GetString(line.Large ? "pulsar-alert-depressurisation-large" : "pulsar-alert-depressurisation",
                    ("place", place));
                break;
        }

        var said = Announce(ent, PulsarAlertKind.Depressurisation, key, text, now, comp.EngineeringChannel);

        // Stage 5: an engineering unit to each new breach.
        if (line.Kind == PulsarBreachLineKind.New)
        {
            foreach (var b in line.Breaches)
                _units.OnHazard(ent, Units.PulsarNeed.Breach, b.Grid, b.Origin, PlaceName(ent, b.Grid, b.Origin), $"breach:{b.Id}");
        }

        // Stage 4: a large one is a station announcement too.
        if (said && line.Large)
            PulsarAnnounce(ent, text, "depressurisation");
    }

    /// <summary>Closes (never bolts) the open doors the AI controls next to the depressurised tiles.</summary>
    private void Isolate(AtmosSample sample, IEnumerable<Vector2i> lowTiles)
    {
        var low = lowTiles.ToHashSet();
        foreach (var info in sample.Memory.Doors.Values)
        {
            if (info.State is not (DoorState.Open or DoorState.Opening))
                continue;

            var t = info.Tile;
            if (!low.Contains(t + new Vector2i(1, 0)) && !low.Contains(t + new Vector2i(-1, 0))
                && !low.Contains(t + new Vector2i(0, 1)) && !low.Contains(t + new Vector2i(0, -1)) && !low.Contains(t))
                continue;

            if (TerminatingOrDeleted(info.Door) || !TryComp<StationAiWhitelistComponent>(info.Door, out var wl) || !wl.Enabled)
                continue;

            _doors.TryClose(info.Door);
        }
    }
}
