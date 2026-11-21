namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Stage 5.3: an anomaly that belongs to science, so «Пульсар» does not send units to neutralize it: one made by
/// the anomaly generator (<see cref="Generated"/>), or one a scientist asked to leave alone (<see cref="Protected"/>).
/// Anomalies of station events have neither and are neutralized as before.
/// </summary>
[RegisterComponent]
public sealed partial class PulsarScienceAnomalyComponent : Component
{
    /// <summary>Spawned by the anomaly generator of the science department.</summary>
    [DataField]
    public bool Generated;

    /// <summary>A scientist (science department, research director or captain) asked not to neutralize it.</summary>
    [DataField]
    public bool Protected;

    /// <summary>Who asked, for the logs.</summary>
    [DataField]
    public string? ProtectedBy;
}
