using System.Linq;
using System.Text;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>pulsar_status: state of every Pulsar core: who runs it, map size, people seen, channels, hearing.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarStatusCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PulsarAutonomousAiSystem _pulsar = default!;

    public override string Command => "pulsar_status";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var any = false;
        var now = _timing.CurTime;
        var query = EntityManager.EntityQueryEnumerator<PulsarAutonomousAiComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var comp, out var meta))
        {
            any = true;
            var sb = new StringBuilder();
            sb.AppendLine(Loc.GetString("cmd-pulsar-status-core",
                ("name", meta.EntityName),
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("state", Loc.GetString($"pulsar-state-{comp.State.ToString().ToLowerInvariant()}")),
                ("player", _pulsar.IsPlayerControlled(uid) ? Loc.GetString("pulsar-yes") : Loc.GetString("pulsar-no"))));

            if (comp.State == PulsarState.Standby && comp.VacantSince is { } vacant)
            {
                var left = comp.VacancyGrace - (now - vacant);
                sb.AppendLine(Loc.GetString("cmd-pulsar-status-grace", ("seconds", Math.Max(0, (int) left.TotalSeconds))));
            }

            foreach (var (grid, mem) in comp.Grids)
            {
                sb.AppendLine(Loc.GetString("cmd-pulsar-status-grid",
                    ("grid", EntityManager.GetNetEntity(grid).ToString()),
                    ("known", mem.KnownTiles),
                    ("visible", mem.VisibleTiles),
                    ("doors", mem.Doors.Count),
                    ("chunks", mem.Chunks.Count),
                    ("window", mem.WindowCursor),
                    ("windows", mem.Windows.Count),
                    ("sweeps", mem.Sweeps)));
            }

            var crew = comp.Mobs.Values.Count(m => m.Kind == PulsarMobKind.Crew);
            var humanoids = comp.Mobs.Values.Count(m => m.Kind == PulsarMobKind.Humanoid);
            var enemies = comp.Mobs.Values.Count(m => m.Kind == PulsarMobKind.Enemy);
            var inView = comp.Mobs.Values.Count(m => m.InView);
            sb.AppendLine(Loc.GetString("cmd-pulsar-status-mobs",
                ("crew", crew), ("humanoids", humanoids), ("enemies", enemies), ("inView", inView)));

            foreach (var mob in comp.Mobs.Values.Where(m => m.Kind == PulsarMobKind.Enemy))
            {
                sb.AppendLine(Loc.GetString("cmd-pulsar-status-enemy",
                    ("name", _pulsar.VisualName(mob)), ("x", mob.Tile.X), ("y", mob.Tile.Y),
                    ("ago", (int) (now - mob.LastSeen).TotalSeconds)));
            }

            sb.AppendLine(Loc.GetString("cmd-pulsar-status-hearing",
                ("channels", comp.Channels.Count == 0 ? "-" : string.Join(", ", comp.Channels.OrderBy(c => c))),
                ("holopads", comp.Holopads.Count),
                ("heard", comp.Heard.Count)));

            sb.AppendLine(Loc.GetString("cmd-pulsar-status-requests",
                ("decisions", comp.Decisions.Count),
                ("pending", comp.PendingArmory.Count),
                ("talking", comp.Conversations.Values.Count(t => t >= now)),
                ("beacons", comp.Beacons.Count)));

            sb.AppendLine(Loc.GetString("cmd-pulsar-status-stage3",
                ("policy", comp.Policy.Kind.ToString()),
                ("alerts", comp.Alerts.Count),
                ("bolts", comp.Bolts.Count)));

            sb.AppendLine(Loc.GetString("cmd-pulsar-status-stage4",
                ("announcements", comp.Announcements.Count),
                ("clarifications", comp.Clarifications.Values.Count(c => c.Expires >= now)),
                ("points", comp.RecentPoints.Count)));

            sb.AppendLine(Loc.GetString("cmd-pulsar-status-greetings",
                ("count", comp.Greetings.Count),
                ("last", comp.Greetings.Count == 0 ? "-" : $"{comp.Greetings[^1].Kind} ({comp.Greetings[^1].Channel ?? "announcement"})")));

            for (var i = Math.Max(0, comp.Decisions.Count - 5); i < comp.Decisions.Count; i++)
                sb.AppendLine(PulsarCommandHelpers.FormatDecision(EntityManager, comp.Decisions[i], now));

            shell.WriteLine(sb.ToString().TrimEnd());
        }

        if (!any)
            shell.WriteLine(Loc.GetString("cmd-pulsar-none"));
    }
}
