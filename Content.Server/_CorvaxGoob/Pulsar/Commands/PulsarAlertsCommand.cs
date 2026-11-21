using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>pulsar_alerts [count] [core]: camera alerts said and the doors Pulsar keeps bolted.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarAlertsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override string Command => "pulsar_alerts";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = 20;
        if (args.Length > 0 && (!int.TryParse(args[0], out count) || count <= 0))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_alerts-help"));
            return;
        }

        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, args.Length > 1 ? args[1] : null, out var core))
            return;

        var now = _timing.CurTime;
        var alerts = core.Comp.Alerts;
        if (alerts.Count == 0)
            shell.WriteLine(Loc.GetString("cmd-pulsar-alerts-empty"));
        for (var i = Math.Max(0, alerts.Count - count); i < alerts.Count; i++)
        {
            var a = alerts[i];
            shell.WriteLine(Loc.GetString("cmd-pulsar-alert-line",
                ("ago", (int) (now - a.Time).TotalSeconds),
                ("kind", a.Kind.ToString()),
                ("channels", a.Channels.Length == 0 ? "local" : string.Join(",", a.Channels)),
                ("text", a.Text)));
        }

        foreach (var (door, bolt) in core.Comp.Bolts)
        {
            shell.WriteLine(Loc.GetString("cmd-pulsar-bolt-line",
                ("door", EntityManager.GetNetEntity(door).ToString()),
                ("auto", bolt.Auto ? "auto" : "request"),
                ("reason", bolt.Reason),
                ("since", (int) (now - bolt.Since).TotalSeconds)));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-pulsar-hint-count")),
            2 => PulsarCommandHelpers.CoreCompletion(EntityManager, Loc.GetString("cmd-pulsar-hint-core")),
            _ => CompletionResult.Empty,
        };
    }
}
