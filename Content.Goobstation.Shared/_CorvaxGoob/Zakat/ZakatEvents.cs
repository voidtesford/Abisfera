using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// One of the two free starting picks.
/// </summary>
public sealed partial class ZakatInfectActionEvent : EntityTargetActionEvent;

/// <summary>
/// Opens the disease (strain) neural map.
/// </summary>
public sealed partial class ZakatOpenShopActionEvent : InstantActionEvent;

/// <summary>
/// Opens the host evolution neural map of the clicked host.
/// </summary>
public sealed partial class ZakatHostEvolveActionEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public enum ZakatUiKey : byte
{
    Shop,
    Host,
}

[Serializable, NetSerializable]
public sealed class ZakatShopState(float biomass, int growth, int captured = 0, int hosts = 0) : BoundUserInterfaceState
{
    public readonly float Biomass = biomass;
    public readonly int Growth = growth;

    /// <summary>
    /// Captured floor tiles: Рост 4 stays locked below <see cref="ZakatGrowth.VineTiles"/>.
    /// </summary>
    public readonly int Captured = captured;

    /// <summary>
    /// Living infected hosts: «Рост» 2 and 3 stay locked below <see cref="ZakatGrowth.RequiredHosts"/>.
    /// </summary>
    public readonly int Hosts = hosts;
}

[Serializable, NetSerializable]
public sealed class ZakatHostState(NetEntity host, string hostName, float biomass, int growth, List<ZakatSymptom> bought,
    bool enslaved, List<ZakatMutation> mutations, bool praetorian = false) : BoundUserInterfaceState
{
    public readonly NetEntity Host = host;
    public readonly string HostName = hostName;
    public readonly float Biomass = biomass;
    public readonly int Growth = growth;
    public readonly List<ZakatSymptom> Bought = bought;
    public readonly bool Enslaved = enslaved;
    public readonly List<ZakatMutation> Mutations = mutations;

    /// <summary>
    /// An evolved slave: no more upgrades.
    /// </summary>
    public readonly bool Praetorian = praetorian;
}

/// <summary>
/// Buys the next «Рост».
/// </summary>
[Serializable, NetSerializable]
public sealed class ZakatBuyGrowthMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ZakatBuySymptomMessage(NetEntity host, ZakatSymptom symptom) : BoundUserInterfaceMessage
{
    public readonly NetEntity Host = host;
    public readonly ZakatSymptom Symptom = symptom;
}

/// <summary>
/// The host's central neuron «порабощение».
/// </summary>
[Serializable, NetSerializable]
public sealed class ZakatEnslaveMessage(NetEntity host) : BoundUserInterfaceMessage
{
    public readonly NetEntity Host = host;
}

[Serializable, NetSerializable]
public sealed class ZakatBuyMutationMessage(NetEntity host, ZakatMutation mutation) : BoundUserInterfaceMessage
{
    public readonly NetEntity Host = host;
    public readonly ZakatMutation Mutation = mutation;
}

/// <summary>
/// Рост 1: take control of one of the hosts for a while.
/// </summary>
public sealed partial class ZakatPossessActionEvent : EntityTargetActionEvent;

/// <summary>
/// «мясной гарпун» of a slave.
/// </summary>
public sealed partial class ZakatHarpoonActionEvent : WorldTargetActionEvent;

/// <summary>
/// «пожиратель» of a slave.
/// </summary>
public sealed partial class ZakatDevourActionEvent : EntityTargetActionEvent;

/// <summary>
/// «биошмат» crawls into a corpse.
/// </summary>
public sealed partial class ZakatBioshmatInfestActionEvent : EntityTargetActionEvent;

/// <summary>
/// Рост 3 «рождение»: an infected bursts and the core of flesh appears in its place.
/// </summary>
public sealed partial class ZakatBirthActionEvent : EntityTargetActionEvent;

/// <summary>
/// One of the buildings of the born disease, placed on its flesh.
/// </summary>
public sealed partial class ZakatBuildActionEvent : WorldTargetActionEvent
{
    [DataField(required: true)]
    public ZakatStructure Structure;
}

/// <summary>
/// «освободить от оков»: an infected bursts into a patch of flesh and feeds the disease.
/// </summary>
public sealed partial class ZakatLiberateActionEvent : EntityTargetActionEvent;

/// <summary>
/// «создать преторианца».
/// </summary>
public sealed partial class ZakatPraetorianActionEvent : EntityTargetActionEvent;

/// <summary>
/// Back to the core.
/// </summary>
public sealed partial class ZakatToCoreActionEvent : InstantActionEvent;

/// <summary>
/// Praetorian: swallow a humanoid corpse and take its look.
/// </summary>
public sealed partial class ZakatAbsorbActionEvent : EntityTargetActionEvent;

/// <summary>
/// Praetorian: pick the next swallowed look (the look itself changes only with <see cref="ZakatTransformActionEvent"/>).
/// </summary>
public sealed partial class ZakatShiftActionEvent : InstantActionEvent;

/// <summary>
/// Praetorian: take the picked swallowed look.
/// </summary>
public sealed partial class ZakatTransformActionEvent : InstantActionEvent;

/// <summary>
/// Hive slave: bone claws out of the hand / back in.
/// </summary>
public sealed partial class ZakatSlaveClawsActionEvent : InstantActionEvent;

/// <summary>
/// Praetorian: bone claws out / in.
/// </summary>
public sealed partial class ZakatClawsActionEvent : InstantActionEvent;

/// <summary>
/// Hunter: devour a living creature.
/// </summary>
public sealed partial class ZakatHunterDevourActionEvent : EntityTargetActionEvent;

/// <summary>
/// One stage of the praetorian's absorption: 0 tentacles burst out, 1 they seize the body, 2 they draw it in.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ZakatAbsorbDoAfterEvent : DoAfterEvent
{
    [DataField]
    public int Stage;

    public ZakatAbsorbDoAfterEvent()
    {
    }

    public ZakatAbsorbDoAfterEvent(int stage)
    {
        Stage = stage;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class ZakatHunterDevourDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// upd2: the hive slave's «пожиратель» tearing a corpse apart (interruptible).
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ZakatSlaveDevourDoAfterEvent : SimpleDoAfterEvent;
