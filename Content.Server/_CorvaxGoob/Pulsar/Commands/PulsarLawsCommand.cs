using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>pulsar_laws [core]: the laws Pulsar read and the policy it took from them.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarLawsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PulsarAutonomousAiSystem _pulsar = default!;

    public override string Command => "pulsar_laws";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, args.Length > 0 ? args[0] : null, out var core))
            return;

        _pulsar.RefreshLaws(core);
        var policy = core.Comp.Policy;
        shell.WriteLine(Loc.GetString("cmd-pulsar-laws-policy",
            ("kind", policy.Kind.ToString()),
            ("source", policy.Source),
            ("master", policy.Master ?? "-"),
            ("others", policy.OthersNotCrew ? "no" : "yes"),
            ("crew", policy.ServesCrew ? "yes" : "no")));
        foreach (var reason in policy.Reasons)
            shell.WriteLine("  * " + reason);
        for (var i = 0; i < core.Comp.LawTexts.Count; i++)
            shell.WriteLine($"  {i + 1}. {core.Comp.LawTexts[i]}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? PulsarCommandHelpers.CoreCompletion(EntityManager, Loc.GetString("cmd-pulsar-hint-core"))
            : CompletionResult.Empty;
    }
}
