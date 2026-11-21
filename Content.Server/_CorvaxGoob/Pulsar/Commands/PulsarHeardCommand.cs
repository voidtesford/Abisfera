using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>pulsar_heard [count] [core]: the last heard lines (radio and holopads).</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarHeardCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override string Command => "pulsar_heard";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = 20;
        if (args.Length > 0 && (!int.TryParse(args[0], out count) || count <= 0))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_heard-help"));
            return;
        }

        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, args.Length > 1 ? args[1] : null, out var core))
            return;

        var heard = core.Comp.Heard;
        if (heard.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-pulsar-heard-empty"));
            return;
        }

        var now = _timing.CurTime;
        for (var i = Math.Max(0, heard.Count - count); i < heard.Count; i++)
        {
            var line = heard[i];
            var where = line.Kind == PulsarHeardKind.Radio
                ? Loc.GetString("cmd-pulsar-heard-radio", ("channel", line.Channel ?? "?"))
                : Loc.GetString("cmd-pulsar-heard-holopad", ("holopad", line.Holopad is { } pad ? EntityManager.GetNetEntity(pad).ToString() : "?"));

            shell.WriteLine(Loc.GetString("cmd-pulsar-heard-line",
                ("ago", (int) (now - line.Time).TotalSeconds),
                ("where", where),
                ("name", line.SpeakerName),
                ("message", line.Message)));
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
