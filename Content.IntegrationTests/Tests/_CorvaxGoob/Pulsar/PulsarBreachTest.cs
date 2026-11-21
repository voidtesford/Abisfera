// Abisfera - upd1: Pulsar depressurisation lines (no spam).
using System.Collections.Generic;
using System.Linq;
using Content.Server._CorvaxGoob.Pulsar;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Pulsar;

[TestFixture]
public sealed class PulsarBreachTest
{
    private static readonly EntityUid Grid = new(7);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Reminder = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    private static string Room(Vector2i t) => t.Y >= 50 ? "C" : t.X < 100 ? "A" : "B";

    private static IEnumerable<Vector2i> Row(int x0, int x1, int y = 0)
    {
        for (var x = x0; x <= x1; x++)
            yield return new Vector2i(x, y);
    }

    private static PulsarBreachLine? Poll(PulsarBreachTracker t, double seconds)
        => t.Poll(TimeSpan.FromSeconds(seconds), 4, 40, Cooldown, Reminder, Grace);

    [Test]
    public void BigBreachIsOneLineThenOneReminder()
    {
        var t = new PulsarBreachTracker();
        var lines = new List<PulsarBreachLine>();

        // A big depressurisation spreading over 10 minutes, seen window by window, room by room.
        for (var s = 0; s < 600; s++)
        {
            var now = TimeSpan.FromSeconds(s);
            if (s < 60)
                t.Observe(Grid, Row(s * 2, s * 2 + 1), Enumerable.Empty<Vector2i>(), Room, now);
            if (Poll(t, s) is { } line)
                lines.Add(line);
        }

        Assert.That(t.Breaches, Has.Count.EqualTo(1), "one spreading breach split up");
        Assert.That(lines.Select(l => l.Kind),
            Is.EqualTo(new[] { PulsarBreachLineKind.New, PulsarBreachLineKind.Large, PulsarBreachLineKind.Reminder }),
            "expected: said once, once more when large, one reminder");
        Assert.That(lines[2].Breaches.Single(), Is.SameAs(lines[0].Breaches.Single()));

        // Nothing more however long it stays.
        for (var s = 600; s < 3000; s += 5)
            Assert.That(Poll(t, s), Is.Null, "said again with nothing changed");
    }

    [Test]
    public void SeveralBreachesShareTheRateLimit()
    {
        var t = new PulsarBreachTracker();
        t.Observe(Grid, Row(0, 4), Enumerable.Empty<Vector2i>(), Room, TimeSpan.Zero);
        Assert.That(Poll(t, 0)?.Kind, Is.EqualTo(PulsarBreachLineKind.New));

        // Two more far apart (two rooms) within the cooldown: one line with both, after the cooldown.
        t.Observe(Grid, Row(50, 54, 50), Enumerable.Empty<Vector2i>(), Room, TimeSpan.FromSeconds(1));
        t.Observe(Grid, Row(200, 204), Enumerable.Empty<Vector2i>(), Room, TimeSpan.FromSeconds(2));
        Assert.That(Poll(t, 10), Is.Null, "rate limit ignored");
        var line = Poll(t, 31);
        Assert.That(line?.Kind, Is.EqualTo(PulsarBreachLineKind.New));
        Assert.That(line!.Breaches, Has.Count.EqualTo(2), "the waiting breaches weren't said together");
        Assert.That(t.Breaches, Has.Count.EqualTo(3));
    }

    [Test]
    public void RepairedBreachIsForgottenAndCanBeSaidAgain()
    {
        var t = new PulsarBreachTracker();
        t.Observe(Grid, Row(0, 4), Enumerable.Empty<Vector2i>(), Room, TimeSpan.Zero);
        Assert.That(Poll(t, 0), Is.Not.Null);

        // Flapping (refilled and broken again within the grace time): the same breach, silent.
        t.Observe(Grid, Enumerable.Empty<Vector2i>(), Row(0, 4), Room, TimeSpan.FromSeconds(40));
        t.Observe(Grid, Row(0, 4), Enumerable.Empty<Vector2i>(), Room, TimeSpan.FromSeconds(50));
        Assert.That(Poll(t, 60), Is.Null, "a flapping breach was said again");

        // Repaired for good: forgotten; breaking later is a new breach.
        t.Observe(Grid, Enumerable.Empty<Vector2i>(), Row(0, 4), Room, TimeSpan.FromSeconds(70));
        Assert.That(Poll(t, 200), Is.Null);
        Assert.That(t.Breaches, Is.Empty, "a repaired breach stayed");
        t.Observe(Grid, Row(0, 4), Enumerable.Empty<Vector2i>(), Room, TimeSpan.FromSeconds(300));
        Assert.That(Poll(t, 300)?.Kind, Is.EqualTo(PulsarBreachLineKind.New));
    }
}
