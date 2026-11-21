using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._CorvaxGoob.ProcAnim;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._CorvaxGoob.ProcAnim;

/// <summary>Marks a dummy spawned by <c>procanim preview</c>: deleted (with the items given to it) after its scene.</summary>
[RegisterComponent]
public sealed partial class ProcAnimPreviewDummyComponent : Component
{
    /// <summary>Items spawned for the scene's required items; deleted with the dummy.</summary>
    [DataField]
    public List<EntityUid> Items = new();

    /// <summary>When set: the scene is over and the dummy goes away at this time.</summary>
    [DataField]
    public TimeSpan? DeleteAt;
}

/// <summary>
/// <c>procanim preview</c>: spawns throwaway humanoids of the requested species next to an admin, gives them the scene's
/// required items and plays the scene on them; cleans them up a few seconds after it ends. Used to check scenes made in
/// the animation editor (Tools/_CorvaxGoob/AnimEditor) without real players.
/// </summary>
public sealed class ProcAnimPreviewSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ProcAnimSystem _procAnim = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    /// <summary>How long the dummies stay after the scene so its final pose can be looked at.</summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);

    public const string DefaultSpecies = "Human";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ProcAnimPreviewDummyComponent, ProcAnimEndedEvent>(OnEnded);
        SubscribeLocalEvent<ProcAnimPreviewDummyComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnEnded(Entity<ProcAnimPreviewDummyComponent> ent, ref ProcAnimEndedEvent args)
    {
        ent.Comp.DeleteAt = _timing.CurTime + Linger;
    }

    private void OnShutdown(Entity<ProcAnimPreviewDummyComponent> ent, ref ComponentShutdown args)
    {
        foreach (var item in ent.Comp.Items)
        {
            if (!TerminatingOrDeleted(item))
                QueueDel(item);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ProcAnimPreviewDummyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.DeleteAt is { } at && now >= at)
                QueueDel(uid);
        }
    }

    /// <summary>Entity prototype for a species id (or an entity prototype id given directly).</summary>
    public bool TryResolveSpecies(string id, out EntProtoId entity)
    {
        if (_proto.TryIndex<SpeciesPrototype>(id, out var species))
        {
            entity = species.Prototype;
            return true;
        }

        entity = default;
        if (!_proto.HasIndex<EntityPrototype>(id))
            return false;
        entity = id;
        return true;
    }

    /// <summary>
    /// Spawns one dummy per actor of <paramref name="scene"/> near <paramref name="at"/> (species by role order, the last
    /// one repeated, <see cref="DefaultSpecies"/> when none) and plays it. False with a locale id in <paramref name="error"/>.
    /// </summary>
    public bool TryPreview(string scene, MapCoordinates at, IReadOnlyList<string> species, out List<EntityUid> dummies,
        [NotNullWhen(false)] out string? error, out string? badSpecies)
    {
        dummies = new List<EntityUid>();
        badSpecies = null;
        if (!_proto.TryIndex<ProcAnimScenePrototype>(scene, out var proto) || proto.Actors.Count == 0)
        {
            error = "procanim-error-no-proto";
            return false;
        }

        var kinds = new List<EntProtoId>();
        for (var i = 0; i < proto.Actors.Count; i++)
        {
            var id = species.Count == 0 ? DefaultSpecies : species[Math.Min(i, species.Count - 1)];
            if (!TryResolveSpecies(id, out var entity))
            {
                badSpecies = id;
                error = "cmd-procanim-bad-species";
                return false;
            }

            kinds.Add(entity);
        }

        for (var i = 0; i < proto.Actors.Count; i++)
        {
            // Lined up along +X at their start spacing (the scene turns the stage itself).
            var x = 1f + MathF.Max(i * 0.7f, proto.Actors[i].Move.Count > 0 ? proto.Actors[i].Move[0].X : 0f);
            var dummy = Spawn(kinds[i], at.Offset(new Vector2(x, 0f)));
            var comp = EnsureComp<ProcAnimPreviewDummyComponent>(dummy);
            GiveItems(dummy, comp, proto.Actors[i]);
            dummies.Add(dummy);
        }

        if (_procAnim.TryPlay(scene, dummies, out error))
            return true;

        foreach (var d in dummies)
            QueueDel(d);
        dummies.Clear();
        return false;
    }

    /// <summary>Required scene items the dummy would otherwise lack: into the slot they are drawn from, else a hand.</summary>
    private void GiveItems(EntityUid dummy, ProcAnimPreviewDummyComponent comp, ProcAnimActor actor)
    {
        foreach (var item in actor.Items)
        {
            if (!item.Required || item.Match.Prototypes.Count == 0 || _procAnim.FindItem(dummy, item, comp.Items) != null)
                continue;
            var spawned = Spawn(item.Match.Prototypes[0], Transform(dummy).Coordinates);
            comp.Items.Add(spawned);
            var placed = item.From.Where(s => s != "hands").Any(slot => _inventory.TryEquip(dummy, spawned, slot, silent: true, force: true))
                         || _hands.TryPickupAnyHand(dummy, spawned, checkActionBlocker: false, animate: false);
            if (!placed)
                Log.Warning($"procanim preview: could not give {item.Match.Prototypes[0]} to {ToPrettyString(dummy)}");
        }
    }
}
