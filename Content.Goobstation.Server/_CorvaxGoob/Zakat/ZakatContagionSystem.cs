using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Disease;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// How «Закат» moves between organisms. Works at full strength from the first second of an infection
/// (no infection-progress scaling, unlike the base Goob spread effects).
/// Aerial: sneeze/cough cones and vomit splashes (masks, hoods and internals protect).
/// Contact: touching, hitting, shoving or pulling (gloves and bio suits protect), much lower chance.
/// </summary>
public sealed partial class ZakatContagionSystem : EntitySystem
{
    [Dependency] private ZakatSystem _zakat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    public static readonly ProtoId<DiseaseSpreadPrototype> Aerial = "Aerial";
    public static readonly ProtoId<DiseaseSpreadPrototype> Contact = "Contact";

    public static readonly Angle SneezeArc = Angle.FromDegrees(120);
    public const float SneezeRange = 3f;
    public static readonly Angle CoughArc = Angle.FromDegrees(180);
    public const float CoughRange = 2.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatContactComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<ZakatContactComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<ZakatContactComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<ZakatContactComponent, InteractHandEvent>(OnInteractHand);
    }

    #region contact

    private void OnAttacked(EntityUid uid, ZakatContactComponent comp, AttackedEvent args)
    {
        Touch(args.User, uid);
    }

    private void OnDisarmed(Entity<ZakatContactComponent> ent, ref DisarmedEvent args)
    {
        Touch(args.Source, ent);
    }

    private void OnPullStarted(EntityUid uid, ZakatContactComponent comp, PullStartedMessage args)
    {
        // raised on both sides; handle once
        if (uid == args.PulledUid)
            Touch(args.PullerUid, args.PulledUid);
    }

    private void OnInteractHand(EntityUid uid, ZakatContactComponent comp, InteractHandEvent args)
    {
        Touch(args.User, uid);
    }

    /// <summary>
    /// Two organisms touched: the strain may cross either way.
    /// </summary>
    public void Touch(EntityUid a, EntityUid b)
    {
        if (a == b)
            return;

        if (TryComp<ZakatHostComponent>(a, out var hostA))
            TrySpread((a, hostA), b, Contact, ContactChance(hostA));
        if (TryComp<ZakatHostComponent>(b, out var hostB))
            TrySpread((b, hostB), a, Contact, ContactChance(hostB));
    }

    private float ContactChance(ZakatHostComponent host)
    {
        return TryComp<ZakatComponent>(host.Zakat, out var zakat) ? zakat.ContactChance : 0f;
    }

    #endregion

    #region aerial

    public void Sneeze(Entity<ZakatHostComponent> ent)
    {
        _chat.TryEmoteWithChat(ent, "Sneeze", forceEmote: true);
        Cone(ent, SneezeArc, SneezeRange);
    }

    public void Cough(Entity<ZakatHostComponent> ent)
    {
        _chat.TryEmoteWithChat(ent, "Cough", forceEmote: true);
        Cone(ent, CoughArc, CoughRange);
    }

    private void Cone(Entity<ZakatHostComponent> ent, Angle arc, float range)
    {
        if (!TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
            return;

        var xform = Transform(ent);
        var (pos, rot) = _transform.GetWorldPositionRotation(xform);
        foreach (var target in _melee.ArcRayCast(pos, rot, arc, range, xform.MapID, ent))
            TrySpread(ent, target, Aerial, zakat.AerialChance);
    }

    /// <summary>
    /// Vomit splashes everyone standing right next to the host.
    /// </summary>
    public void VomitSplash(Entity<ZakatHostComponent> ent)
    {
        if (!TryComp<ZakatComponent>(ent.Comp.Zakat, out var zakat))
            return;

        foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(ent).Coordinates, zakat.VomitRange))
            TrySpread(ent, target, Aerial, zakat.VomitChance);
    }

    #endregion

    /// <summary>
    /// Chance (0..1) that this host passes the strain to the target by this route right now.
    /// Base chance x host infectivity, then the target's and source's protective gear; internals stop anything airborne.
    /// </summary>
    public float GetSpreadProbability(Entity<ZakatHostComponent> source, EntityUid target, ProtoId<DiseaseSpreadPrototype> type, float baseChance)
    {
        if (source.Owner == target || !TryComp<ZakatComponent>(source.Comp.Zakat, out var zakat))
            return 0f;

        if (!ZakatHelpers.IsOrganic(EntityManager, target) || !_mobState.IsAlive(target)
            || _zakat.IsInfected(zakat, target) || _zakat.IsImmune(zakat, target))
            return 0f;

        if (_proto.TryIndex(type, out var proto) && proto.BlockedByInternals
            && (_internals.AreInternalsWorking(target) || _internals.AreInternalsWorking(source)))
            return 0f;

        var power = 1f;
        var chance = baseChance * _zakat.GetHostInfectivity((source.Comp.Zakat, zakat), source.Comp);

        var outgoing = new DiseaseOutgoingSpreadAttemptEvent(power, chance, type);
        RaiseLocalEvent(source, ref outgoing);
        var incoming = new DiseaseIncomingSpreadAttemptEvent(outgoing.Power, outgoing.Chance, type);
        RaiseLocalEvent(target, ref incoming);

        if (incoming.Power <= 0f || incoming.Chance <= 0f)
            return 0f;

        return Math.Clamp(incoming.Power * incoming.Chance, 0f, 1f);
    }

    public bool TrySpread(Entity<ZakatHostComponent> source, EntityUid target, ProtoId<DiseaseSpreadPrototype> type, float baseChance)
    {
        var p = GetSpreadProbability(source, target, type, baseChance);
        if (p <= 0f || !_random.Prob(p))
            return false;

        return TryComp<ZakatComponent>(source.Comp.Zakat, out var zakat)
               && _zakat.InfectForced((source.Comp.Zakat, zakat), target);
    }

    /// <summary>
    /// Infectious blood of «гнилые наросты»: bare skin catches it for sure, gloves, boots and bio suits lower the odds.
    /// </summary>
    public bool TryPuddleInfect(EntityUid zakatUid, EntityUid target)
    {
        if (!TryComp<ZakatComponent>(zakatUid, out var zakat))
            return false;

        if (!ZakatHelpers.IsOrganic(EntityManager, target) || !_mobState.IsAlive(target)
            || _zakat.IsInfected(zakat, target) || _zakat.IsImmune(zakat, target))
            return false;

        var incoming = new DiseaseIncomingSpreadAttemptEvent(1f, 1f, Contact);
        RaiseLocalEvent(target, ref incoming);
        var p = Math.Clamp(incoming.Power * incoming.Chance, 0f, 1f);
        if (p <= 0f || !_random.Prob(p))
            return false;

        return _zakat.InfectForced((zakatUid, zakat), target);
    }

    /// <summary>
    /// upd1: «вирусный газ заката» breathed in. Same protection as the airborne route (masks, bio hoods; internals
    /// keep the gas out of the lungs in the first place). The strain is the living disease's (the oldest born one).
    /// </summary>
    public bool TrySporeInfect(EntityUid target, float chance)
    {
        if (chance <= 0f || !ZakatHelpers.IsOrganic(EntityManager, target) || !_mobState.IsAlive(target)
            || _internals.AreInternalsWorking(target))
            return false;

        Entity<ZakatComponent>? source = null;
        var query = EntityQueryEnumerator<ZakatComponent>();
        while (query.MoveNext(out var uid, out var zakat))
        {
            if (zakat.Defeated)
                continue;
            if (source == null || zakat.Born && !source.Value.Comp.Born)
                source = (uid, zakat);
        }

        if (source is not { } z || _zakat.IsInfected(z.Comp, target) || _zakat.IsImmune(z.Comp, target))
            return false;

        var incoming = new DiseaseIncomingSpreadAttemptEvent(1f, chance, Aerial);
        RaiseLocalEvent(target, ref incoming);
        var p = Math.Clamp(incoming.Power * incoming.Chance, 0f, 1f);
        if (p <= 0f || !_random.Prob(p))
            return false;

        return _zakat.InfectForced(z, target);
    }
}
