using System.Linq;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// upd1: one depressurisation = one breach, however many tiles and scan windows it covers. New low tiles next to a
/// known breach (or in the same place) join it silently; a new breach is said once; all depressurisation lines share
/// one rate limit (breaches found meanwhile are said together in one line); an unrepaired breach gets one reminder
/// after a long time, then nothing until its state changes (it grows large, or it is repaired and breaks again).
/// Pure bookkeeping (no entity system calls), so it is easy to test.
/// </summary>
public sealed class PulsarBreachTracker
{
    /// <summary>Tiles within this distance (Chebyshev) of a breach's tiles belong to it.</summary>
    public int MergeDistance = 2;

    public readonly List<PulsarBreach> Breaches = new();

    /// <summary>No depressurisation line before this time (global rate limit).</summary>
    public TimeSpan NextLine;

    private int _nextId = 1;

    /// <summary>
    /// What one look at the air found: <paramref name="newLow"/> tiles seen fine before and depressurised now,
    /// <paramref name="good"/> tiles with normal pressure.
    /// </summary>
    public void Observe(EntityUid grid, IEnumerable<Vector2i> newLow, IEnumerable<Vector2i> good, Func<Vector2i, string> placeKey, TimeSpan now)
    {
        foreach (var tile in good)
        {
            foreach (var breach in Breaches)
            {
                if (breach.Grid != grid || !breach.Low.Remove(tile))
                    continue;
                if (breach.Low.Count == 0)
                    breach.RepairedAt = now;
            }
        }

        foreach (var tile in newLow)
        {
            var breach = Find(grid, tile, placeKey);
            if (breach == null)
            {
                breach = new PulsarBreach(_nextId++, grid, tile, placeKey(tile));
                Breaches.Add(breach);
            }

            breach.Low.Add(tile);
            breach.Seen.Add(tile);
            breach.RepairedAt = null;
        }
    }

    private PulsarBreach? Find(EntityUid grid, Vector2i tile, Func<Vector2i, string> placeKey)
    {
        string? key = null;
        foreach (var breach in Breaches)
        {
            if (breach.Grid != grid)
                continue;

            for (var dx = -MergeDistance; dx <= MergeDistance; dx++)
            {
                for (var dy = -MergeDistance; dy <= MergeDistance; dy++)
                {
                    if (breach.Seen.Contains(tile + new Vector2i(dx, dy)))
                        return breach;
                }
            }

            key ??= placeKey(tile);
            if (breach.PlaceKey == key)
                return breach;
        }

        return null;
    }

    /// <summary>
    /// The next line to say, if any (at most one per <paramref name="cooldown"/>). Also forgets breaches repaired
    /// longer than <paramref name="repairGrace"/> ago.
    /// </summary>
    public PulsarBreachLine? Poll(TimeSpan now, int minTiles, int largeTiles, TimeSpan cooldown, TimeSpan reminderAfter, TimeSpan repairGrace)
    {
        Breaches.RemoveAll(b => b.RepairedAt is { } at && now - at >= repairGrace);

        if (now < NextLine)
            return null;

        PulsarBreachLine? line = null;

        // New breaches (all of them in one line).
        var fresh = Breaches.Where(b => !b.Announced && b.Low.Count > 0 && b.Seen.Count >= minTiles).ToList();
        if (fresh.Count > 0)
        {
            foreach (var b in fresh)
            {
                b.Announced = true;
                b.AnnouncedAt = now;
                if (b.Seen.Count >= largeTiles)
                    b.LargeAnnounced = true;
            }

            line = new PulsarBreachLine(PulsarBreachLineKind.New, fresh, fresh.Any(b => b.LargeAnnounced));
        }
        else if (Breaches.FirstOrDefault(b => b.Announced && !b.LargeAnnounced && b.Low.Count > 0 && b.Seen.Count >= largeTiles) is { } grown)
        {
            grown.LargeAnnounced = true;
            line = new PulsarBreachLine(PulsarBreachLineKind.Large, new List<PulsarBreach> { grown }, true);
        }
        else
        {
            var stale = Breaches.Where(b => b.Announced && !b.Reminded && b.Low.Count > 0 && now - b.AnnouncedAt >= reminderAfter).ToList();
            if (stale.Count > 0)
            {
                foreach (var b in stale)
                    b.Reminded = true;
                line = new PulsarBreachLine(PulsarBreachLineKind.Reminder, stale, false);
            }
        }

        if (line != null)
            NextLine = now + cooldown;
        return line;
    }
}

public sealed class PulsarBreach
{
    public readonly int Id;
    public readonly EntityUid Grid;
    public readonly Vector2i Origin;
    public readonly string PlaceKey;

    /// <summary>Tiles depressurised right now (as last seen).</summary>
    public readonly HashSet<Vector2i> Low = new();

    /// <summary>Every tile that was ever part of this breach.</summary>
    public readonly HashSet<Vector2i> Seen = new();

    public bool Announced;
    public bool LargeAnnounced;
    public bool Reminded;
    public TimeSpan AnnouncedAt;

    /// <summary>When the last low tile was seen fine again (null while still depressurised).</summary>
    public TimeSpan? RepairedAt;

    public PulsarBreach(int id, EntityUid grid, Vector2i origin, string placeKey)
    {
        Id = id;
        Grid = grid;
        Origin = origin;
        PlaceKey = placeKey;
    }
}

public enum PulsarBreachLineKind : byte
{
    New,
    Large,
    Reminder,
}

public sealed record PulsarBreachLine(PulsarBreachLineKind Kind, List<PulsarBreach> Breaches, bool Large);
