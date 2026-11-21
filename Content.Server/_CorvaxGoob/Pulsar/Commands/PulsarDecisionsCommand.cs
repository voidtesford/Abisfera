using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>pulsar_decisions [count] [core]: who asked for what and what Pulsar decided, with the reason.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarDecisionsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override string Command => "pulsar_decisions";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var count = 20;
        if (args.Length > 0 && (!int.TryParse(args[0], out count) || count <= 0))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_decisions-help"));
            return;
        }

        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, args.Length > 1 ? args[1] : null, out var core))
            return;

        var decisions = core.Comp.Decisions;
        if (decisions.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-pulsar-decisions-empty"));
            return;
        }

        var now = _timing.CurTime;
        for (var i = Math.Max(0, decisions.Count - count); i < decisions.Count; i++)
            shell.WriteLine(PulsarCommandHelpers.FormatDecision(EntityManager, decisions[i], now));
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
