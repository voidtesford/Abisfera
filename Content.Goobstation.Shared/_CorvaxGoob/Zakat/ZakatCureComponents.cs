using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// A swab remembers whom it was taken from and whether «Закат» was in the sample.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZakatSwabComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Subject;

    [ViewVariables, AutoNetworkedField]
    public string SubjectName = string.Empty;

    /// <summary>
    /// The sample carries the strain of this disease.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Zakat;

    /// <summary>
    /// The swab also caught some other (ordinary) disease: leave the sample to the regular virology.
    /// </summary>
    [ViewVariables]
    public bool OtherDisease;
}

/// <summary>
/// The diagnoser and the vaccinator recognise «Закат» samples.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatVirologyComponent : Component
{
    [DataField]
    public EntProtoId VaccinePrototype = "ZakatVaccine";

    [DataField]
    public EntProtoId PaperPrototype = "DiagnosisReportPaper";
}

/// <summary>
/// An individual vaccine against «Закат»: works only on the person it was made from.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZakatVaccineComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Subject;

    [ViewVariables, AutoNetworkedField]
    public string SubjectName = string.Empty;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? Zakat;

    [ViewVariables, AutoNetworkedField]
    public bool Used;

    /// <summary>
    /// How many symptoms one dose rolls back.
    /// </summary>
    [DataField]
    public int Rollback = 2;
}
