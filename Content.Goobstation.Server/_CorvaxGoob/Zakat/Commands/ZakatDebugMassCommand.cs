using System.Globalization;
using System.Linq;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat.Commands;

/// <summary>
/// upd2 debug: <c>zakatdebugmass &lt;zakat&gt; &lt;amount&gt;</c> adds biomass to a «Закат» disease
/// (negative takes it away, never below zero).
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class ZakatDebugMassCommand : LocalizedEntityCommands
{
    [Dependency] private ZakatSystem _zakat = default!;

    public override string Command => "zakatdebugmass";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-zakatdebugmass-usage"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.TryGetComponent<ZakatComponent>(uid, out var zakat))
        {
            shell.WriteError(Loc.GetString("cmd-zakatdebugmass-not-zakat", ("uid", args[0])));
            return;
        }

        if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || !float.IsFinite(amount))
        {
            shell.WriteError(Loc.GetString("cmd-zakatdebugmass-bad-amount", ("amount", args[1])));
            return;
        }

        var before = zakat.Biomass;
        var after = Math.Max(0f, before + amount);
        _zakat.AddBiomass((uid.Value, zakat), after - before);
        shell.WriteLine(Loc.GetString("cmd-zakatdebugmass-done",
            ("zakat", EntityManager.ToPrettyString(uid.Value)),
            ("before", before.ToString("0.##", CultureInfo.InvariantCulture)),
            ("after", zakat.Biomass.ToString("0.##", CultureInfo.InvariantCulture))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = new List<CompletionOption>();
            var query = EntityManager.EntityQueryEnumerator<ZakatComponent>();
            while (query.MoveNext(out var uid, out var zakat))
            {
                options.Add(new CompletionOption(EntityManager.GetNetEntity(uid).ToString(),
                    $"{EntityManager.ToPrettyString(uid)} ({zakat.Biomass:0.#})"));
            }

            return CompletionResult.FromHintOptions(options.OrderBy(o => o.Value), Loc.GetString("cmd-zakatdebugmass-hint-zakat"));
        }

        return args.Length == 2
            ? CompletionResult.FromHint(Loc.GetString("cmd-zakatdebugmass-hint-amount"))
            : CompletionResult.Empty;
    }
}
