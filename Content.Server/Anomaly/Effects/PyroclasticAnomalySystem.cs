using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Shared.Anomaly.Components;
using Content.Shared.Anomaly.Effects.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server.Anomaly.Effects;

/// <summary>
/// This handles <see cref="PyroclasticAnomalyComponent"/> and the events from <seealso cref="AnomalySystem"/>
/// </summary>
public sealed class PyroclasticAnomalySystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PyroclasticAnomalyComponent, AnomalyPulseEvent>(OnPulse);
        SubscribeLocalEvent<PyroclasticAnomalyComponent, AnomalySupercriticalEvent>(OnSupercritical);
    }

    private void OnPulse(EntityUid uid, PyroclasticAnomalyComponent component, ref AnomalyPulseEvent args)
    {
        var xform = Transform(uid);
        var ignitionRadius = component.MaximumIgnitionRadius * args.Stability * args.PowerModifier;
        IgniteNearby(uid, xform.Coordinates, args.Severity, ignitionRadius);

        // Шанс заспавнить 1 огонь в радиусе FireRadius тайлов
        if (_random.Prob(component.PulseFireChance))
            SpawnFires(uid, component, 1);
    }

    private void OnSupercritical(EntityUid uid, PyroclasticAnomalyComponent component, ref AnomalySupercriticalEvent args)
    {
        var xform = Transform(uid);
        IgniteNearby(uid, xform.Coordinates, 1, component.MaximumIgnitionRadius * 2 * args.PowerModifier);

        // При взрыве спавним несколько огней
        SpawnFires(uid, component, component.SupercriticalFireCount);
    }

    public void IgniteNearby(EntityUid uid, EntityCoordinates coordinates, float severity, float radius)
    {
        var flammables = new HashSet<Entity<FlammableComponent>>();
        _lookup.GetEntitiesInRange(coordinates, radius, flammables);

        foreach (var flammable in flammables)
        {
            var ent = flammable.Owner;
            var stackAmount = 1 + (int) (severity / 0.15f);
            _flammable.AdjustFireStacks(ent, stackAmount, flammable);
            _flammable.Ignite(ent, uid, flammable);
        }
    }

    /// <summary>
    /// Спавнит до <paramref name="count"/> огней на случайных подходящих тайлах
    /// в радиусе <see cref="PyroclasticAnomalyComponent.FireRadius"/> от аномалии.
    /// Тайлы без пола, со стенами или без кислорода пропускаются,
    /// потому что огонь на них сразу бы погас.
    /// </summary>
    private void SpawnFires(EntityUid uid, PyroclasticAnomalyComponent comp, int count)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var radius = comp.FireRadius;
        var spawned = 0;

        for (var attempt = 0; attempt < count * 10 && spawned < count; attempt++)
        {
            var dx = _random.Next(-radius, radius + 1);
            var dy = _random.Next(-radius, radius + 1);
            if (dx * dx + dy * dy > radius * radius)
                continue;

            var tile = center + new Vector2i(dx, dy);

            if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            // Пропускаем тайлы со стенами и другими герметичными блоками
            var blocked = false;
            var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
            while (anchored.MoveNext(out var ent))
            {
                if (TryComp<AirtightComponent>(ent, out var airtight) && airtight.AirBlocked)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
                continue;

            // Тот же порог кислорода, что у самого огня (BrokenTechFireSpreadSystem)
            var mixture = _atmosphere.GetTileMixture(gridUid, null, tile, true);
            if (mixture == null || mixture.GetMoles(Gas.Oxygen) < 0.1f)
                continue;

            Spawn(comp.CustomFirePrototype, _map.GridTileToLocal(gridUid, grid, tile));
            spawned++;
        }
    }
}
