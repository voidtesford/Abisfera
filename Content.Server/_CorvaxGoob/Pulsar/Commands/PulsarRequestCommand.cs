using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>
/// pulsar_request &lt;speaker&gt; [:Channel] &lt;text&gt;: makes Pulsar handle a line as if the speaker said it (by radio
/// on Channel, otherwise at a holopad). The rules are the same as for real speech; the decision shows in
/// pulsar_decisions on the next tick.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarRequestCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PulsarAutonomousAiSystem _pulsar = default!;

    public override string Command => "pulsar_request";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || !NetEntity.TryParse(args[0], out var net) || !EntityManager.TryGetEntity(net, out var speaker))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_request-help"));
            return;
        }

        string? channel = null;
        var first = 1;
        if (args[1].StartsWith(':') && args[1].Length > 1)
        {
            channel = args[1][1..];
            first = 2;
        }

        if (first >= args.Length)
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_request-help"));
            return;
        }

        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, null, out var core))
            return;

        if (core.Comp.State != PulsarState.Active)
        {
            shell.WriteError(Loc.GetString("cmd-pulsar-request-inactive"));
            return;
        }

        var text = string.Join(' ', args[first..]);
        _pulsar.HandleSpeech(core, speaker.Value, text, channel);
        shell.WriteLine(Loc.GetString("cmd-pulsar-request-queued", ("text", text)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-pulsar-hint-speaker")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-pulsar-hint-text")),
            _ => CompletionResult.Empty,
        };
    }
}
