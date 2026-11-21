using System.Text;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Doors.Components;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._CorvaxGoob.Pulsar.Commands;

/// <summary>
/// pulsar_map [radius] [core] [grid]: ASCII dump of what Pulsar remembers around the core (or the grid centre).
/// Top row is north.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class PulsarMapCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SharedMapSystem _maps = default!;

    public override string Command => "pulsar_map";

    private const int MaxRadius = 100;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var radius = 30;
        if (args.Length > 0 && (!int.TryParse(args[0], out radius) || radius <= 0))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar_map-help"));
            return;
        }

        radius = Math.Min(radius, MaxRadius);

        if (!PulsarCommandHelpers.TryGetCore(EntityManager, shell, args.Length > 1 ? args[1] : null, out var core))
            return;

        var coreXform = EntityManager.GetComponent<TransformComponent>(core);
        EntityUid? gridUid = coreXform.GridUid;
        if (args.Length > 2)
        {
            if (!NetEntity.TryParse(args[2], out var net) || !EntityManager.TryGetEntity(net, out gridUid))
            {
                shell.WriteError(Loc.GetString("cmd-pulsar-no-grid", ("arg", args[2])));
                return;
            }
        }

        if (gridUid is not { } grid || !core.Comp.Grids.TryGetValue(grid, out var mem)
            || !EntityManager.TryGetComponent(grid, out MapGridComponent? gridComp))
        {
            shell.WriteError(Loc.GetString("cmd-pulsar-no-grid", ("arg", gridUid?.ToString() ?? "-")));
            return;
        }

        Vector2i center;
        if (coreXform.GridUid == grid)
            center = _maps.LocalToTile(grid, gridComp, coreXform.Coordinates);
        else
            center = (Vector2i) gridComp.LocalAABB.Center.Floored();

        var mobs = new Dictionary<Vector2i, PulsarMobKind>();
        foreach (var mob in core.Comp.Mobs.Values)
        {
            if (mob.Grid != grid)
                continue;
            // Enemies win over crew on the same tile.
            if (!mobs.TryGetValue(mob.Tile, out var existing) || mob.Kind == PulsarMobKind.Enemy || existing == PulsarMobKind.Humanoid)
                mobs[mob.Tile] = mob.Kind;
        }

        // Stage 5.2: units linked to this core (their sensors feed the map) and bodies.
        var units = new Dictionary<Vector2i, EntityUid>();
        var linked = EntityManager.EntityQueryEnumerator<Units.PulsarLinkedComponent, TransformComponent>();
        while (linked.MoveNext(out var unitUid, out var link, out var unitXform))
        {
            if (link.Core != core.Owner || unitXform.GridUid != grid)
                continue;
            units[_maps.LocalToTile(grid, gridComp, unitXform.Coordinates)] = unitUid;
        }

        var bodies = new HashSet<Vector2i>();
        foreach (var mob in core.Comp.Mobs.Values)
        {
            if (mob.Grid == grid && mob.Dead)
                bodies.Add(mob.Tile);
        }

        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("cmd-pulsar-map-header",
            ("grid", EntityManager.GetNetEntity(grid).ToString()), ("x", center.X), ("y", center.Y), ("radius", radius)));
        sb.AppendLine(Loc.GetString("cmd-pulsar-map-legend"));

        for (var y = center.Y + radius; y >= center.Y - radius; y--)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                var tile = new Vector2i(x, y);
                if (tile == center && coreXform.GridUid == grid)
                {
                    sb.Append('@');
                    continue;
                }

                if (units.ContainsKey(tile))
                {
                    sb.Append('U');
                    continue;
                }

                if (bodies.Contains(tile))
                {
                    sb.Append('x');
                    continue;
                }

                if (mobs.TryGetValue(tile, out var kind))
                {
                    sb.Append(kind switch
                    {
                        PulsarMobKind.Enemy => 'E',
                        PulsarMobKind.Crew => 'C',
                        _ => 'H',
                    });
                    continue;
                }

                if (mem.Hazards.TryGetValue(tile, out var hazard))
                {
                    sb.Append(hazard.Kind == PulsarHazardKind.Fire ? 'F' : 'P');
                    continue;
                }

                if (mem.Blocked.ContainsKey(tile))
                {
                    sb.Append('X');
                    continue;
                }

                sb.Append(TileChar(mem, tile));
            }

            sb.AppendLine();
        }

        // Doors in the shown area with the access they need.
        var doorsHeader = false;
        foreach (var (tile, door) in mem.Doors)
        {
            if (Math.Abs(tile.X - center.X) > radius || Math.Abs(tile.Y - center.Y) > radius)
                continue;

            if (!doorsHeader)
            {
                sb.AppendLine(Loc.GetString("cmd-pulsar-map-doors"));
                doorsHeader = true;
            }

            var access = door.Access.Count == 0
                ? Loc.GetString("cmd-pulsar-map-door-free")
                : string.Join(" | ", door.Access.ConvertAll(set => string.Join(" + ", set)));

            sb.AppendLine(Loc.GetString("cmd-pulsar-map-door",
                ("name", EntityManager.TryGetComponent(door.Door, out MetaDataComponent? meta) ? meta.EntityName : "?"),
                ("x", tile.X), ("y", tile.Y),
                ("state", door.State.ToString()),
                ("bolted", door.Bolted ? Loc.GetString("pulsar-yes") : Loc.GetString("pulsar-no")),
                ("access", access)));
        }

        // Stage 5.2: the units, where they are and what they do.
        if (units.Count > 0)
        {
            var unitSystem = EntityManager.System<Units.PulsarUnitsSystem>();
            sb.AppendLine(Loc.GetString("cmd-pulsar-map-units", ("count", units.Count), ("seen", mem.UnitSeenTiles)));
            foreach (var (tile, unitUid) in units)
            {
                var link = EntityManager.GetComponent<Units.PulsarLinkedComponent>(unitUid);
                sb.AppendLine(Loc.GetString("cmd-pulsar-map-unit",
                    ("name", EntityManager.GetComponent<MetaDataComponent>(unitUid).EntityName),
                    ("x", tile.X), ("y", tile.Y),
                    ("task", unitSystem.TaskText((unitUid, link))),
                    ("doors", link.DoorsOpened)));
            }
        }

        shell.WriteLine(sb.ToString().TrimEnd());
    }

    private static char TileChar(PulsarGridMemory mem, Vector2i tile)
    {
        var kind = mem.GetTile(tile);
        switch (kind)
        {
            case PulsarTileKind.Space:
                return '~';
            case PulsarTileKind.Floor:
                return mem.IsVisible(tile) ? '.' : mem.IsUnitSeen(tile) ? ':' : ',';
            case PulsarTileKind.Wall:
                return '#';
            case PulsarTileKind.Window:
                return '=';
            case PulsarTileKind.Table:
                return 'T';
            case PulsarTileKind.Airlock:
            case PulsarTileKind.Door:
                var airlock = kind == PulsarTileKind.Airlock;
                if (mem.Doors.TryGetValue(tile, out var door))
                {
                    if (door.Bolted)
                        return 'B';
                    if (door.State == DoorState.Open)
                        return airlock ? 'a' : 'd';
                }

                return airlock ? 'A' : 'D';
            default:
                return ' ';
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-pulsar-hint-radius")),
            2 => PulsarCommandHelpers.CoreCompletion(EntityManager, Loc.GetString("cmd-pulsar-hint-core")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-pulsar-hint-grid")),
            _ => CompletionResult.Empty,
        };
    }
}
