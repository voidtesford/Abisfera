using Robust.Shared.Console;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

internal static class PulsarCommandHelpers
{
    /// <summary>Resolves the optional core argument (a NetEntity id); without it the first Pulsar core is used.</summary>
    public static bool TryGetCore(IEntityManager entMan, IConsoleShell shell, string? arg, out Entity<PulsarAutonomousAiComponent> core)
    {
        core = default;

        if (arg != null)
        {
            if (!NetEntity.TryParse(arg, out var net) || !entMan.TryGetEntity(net, out var uid)
                || !entMan.TryGetComponent(uid, out PulsarAutonomousAiComponent? comp))
            {
                shell.WriteError(Loc.GetString("cmd-pulsar-no-core", ("arg", arg)));
                return false;
            }

            core = (uid.Value, comp);
            return true;
        }

        var query = entMan.EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        if (query.MoveNext(out var first, out var firstComp))
        {
            core = (first, firstComp);
            return true;
        }

        shell.WriteError(Loc.GetString("cmd-pulsar-none"));
        return false;
    }

    public static string FormatDecision(IEntityManager entMan, PulsarDecision d, TimeSpan now)
    {
        return Loc.GetString("cmd-pulsar-decision-line",
            ("ago", (int) (now - d.Time).TotalSeconds),
            ("name", d.RequesterName),
            ("job", d.Job ?? "-"),
            ("line", d.Line),
            ("target", d.Target ?? "-"),
            ("door", d.Door is { } door && entMan.EntityExists(door) ? entMan.GetNetEntity(door).ToString() : "-"),
            ("outcome", Loc.GetString($"pulsar-outcome-{d.Outcome.ToString().ToLowerInvariant()}")),
            ("reason", d.Reason),
            ("reply", d.Reply ?? "-"));
    }

    public static CompletionResult CoreCompletion(IEntityManager entMan, string hint)
    {
        var options = new List<CompletionOption>();
        var query = entMan.EntityQueryEnumerator<PulsarAutonomousAiComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
            options.Add(new CompletionOption(entMan.GetNetEntity(uid).ToString(), meta.EntityName));

        return CompletionResult.FromHintOptions(options, hint);
    }
}
