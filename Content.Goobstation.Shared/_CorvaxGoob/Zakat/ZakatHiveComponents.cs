using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// Objective «заражать как можно больше органики»: progress is the share of the crew the hive owns.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatSlaveConditionComponent : Component;

/// <summary>
/// «биошмат»: what crawls out of a dead slave with «сердце паразит».
/// </summary>
[RegisterComponent]
public sealed partial class ZakatBioshmatComponent : Component
{
    [ViewVariables]
    public EntityUid Zakat;

    [DataField]
    public TimeSpan GrowTime = TimeSpan.FromMinutes(5);

    [ViewVariables]
    public TimeSpan GrowAt;

    [DataField]
    public EntProtoId InfestActionProto = "ActionZakatBioshmatInfest";

    [ViewVariables]
    public EntityUid? InfestAction;

    [DataField]
    public EntProtoId GrownPrototype = "MobHuman";

    [DataField]
    public Color GrownSkin = Color.FromHex("#8a2a2a");
}
