using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// The born disease: a 1-tile pile of active biomass. Destroy it and «Закат» is lost.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatCoreComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;
}

/// <summary>
/// One captured tile: flesh tentacles over a floor, or growths over a wall.
/// Only active flesh (connected to a living core) spreads, hosts buildings and slows the healthy.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatFleshTileComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;

    /// <summary>
    /// Growths over a wall: they cover it but never spread further.
    /// </summary>
    [DataField]
    public bool Wall;

    [ViewVariables]
    public bool Active = true;
}

[Serializable, NetSerializable]
public enum ZakatFleshVisuals : byte
{
    Active,
}

/// <summary>
/// A building of the born disease. Works only while its flesh is active.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatStructureComponent : Component
{
    [DataField]
    public ZakatStructure Kind;

    /// <summary>
    /// «изрыгатель массы»: biomass every <see cref="Interval"/>.
    /// </summary>
    [DataField]
    public float Income;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// upd1 «изрыгатель спор»: moles of <see cref="Gas"/> let out on its tile every <see cref="Interval"/>.
    /// </summary>
    [DataField]
    public float GasMoles;

    [DataField]
    public Content.Shared.Atmos.Gas Gas = Content.Shared.Atmos.Gas.ZakatSpores;

    /// <summary>upd2: a short effect spawned on the structure each time it lets the gas out (the spore puff).</summary>
    [DataField]
    public Robust.Shared.Prototypes.EntProtoId? EmitEffect;

    /// <summary>
    /// Creatures it brings out, picked at random.
    /// </summary>
    [DataField]
    public List<EntProtoId> Spawns = new();

    /// <summary>
    /// A nest keeps at most this many of its creatures alive.
    /// </summary>
    [DataField]
    public int MaxAlive = 4;

    /// <summary>
    /// A cocoon: hatches once after <see cref="Interval"/> and bursts back into plain flesh.
    /// </summary>
    [DataField]
    public bool Hatch;

    [ViewVariables]
    public EntityUid? Zakat;

    [ViewVariables]
    public TimeSpan NextAt;

    [ViewVariables]
    public List<EntityUid> Spawned = new();
}

/// <summary>
/// A creature of the born disease (nest bioshmat, hunter, flesh humanoid): hive mind, flesh faction, guards the flesh.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatCreatureComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;
}

/// <summary>
/// Hunter: devours the living to heal.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatHunterComponent : Component
{
    [DataField]
    public EntProtoId DevourActionProto = "ActionZakatHunterDevour";

    [ViewVariables]
    public EntityUid? DevourAction;

    [DataField]
    public float DevourHeal = 70f;

    [DataField]
    public TimeSpan DevourTime = TimeSpan.FromSeconds(3);
}

/// <summary>
/// «преторианец»: an evolved slave. No crit, 400 health, faster, regenerating, swallows corpses and wears their looks.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatPraetorianComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;

    [ViewVariables]
    public EntityUid? AbsorbAction, ShiftAction, ClawsAction, TransformAction;

    /// <summary>
    /// Absorption effects alive right now (tentacles out of the body, around the corpse), removed when it ends.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> AbsorbEffects = new();

    /// <summary>
    /// The swallowed look picked for the next transformation.
    /// </summary>
    [ViewVariables]
    public int SelectedLook = -1;

    /// <summary>
    /// Swallowed bodies, kept inside; their looks can be worn.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Absorbed = new();

    [ViewVariables]
    public int CurrentLook = -1;

    /// <summary>
    /// The copied clothes of the current look: no stats, can't be taken off.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> LookClothes = new();

    [ViewVariables]
    public TimeSpan NextRegen;

    /// <summary>
    /// Death threshold before the evolution, given back if the hive lets go.
    /// </summary>
    [ViewVariables]
    public float? OriginalDead;
}

/// <summary>
/// Praetorian bone claws are out. Visual marker, also switches its bare hands to claws.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatPraetorianClawsComponent : Component;

/// <summary>
/// The born disease's «hand»: every click with it captures a tile next to its flesh.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatFleshControllerComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;
}

/// <summary>
/// A copied piece of clothing a praetorian wears with a swallowed look: no stats, can't be taken off.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatLookClothingComponent : Component;

/// <summary>
/// The flesh lets this one pass without slowing: infected and the hive's creatures.
/// Networked so the client predicts the same speed as the server.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatFleshFriendComponent : Component;

/// <summary>
/// One tile of the predatory vines (Рост 4).
/// </summary>
[RegisterComponent]
public sealed partial class ZakatVineComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;
}

/// <summary>
/// A door of biomass: opens only for the flesh's own.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatFleshDoorComponent : Component
{
    [ViewVariables]
    public TimeSpan? CloseAt;
}
