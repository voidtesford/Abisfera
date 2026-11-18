using Robust.Shared.Prototypes;

namespace Content.Shared.Anomaly.Effects.Components;

[RegisterComponent]
public sealed partial class PyroclasticAnomalyComponent : Component
{
    /// <summary>
    /// The maximum distance from which you can be ignited by the anomaly.
    /// </summary>
    [DataField("maximumIgnitionRadius")]
    public float MaximumIgnitionRadius = 5f;

    /// <summary>
    /// </summary>
    [DataField]
    public EntProtoId CustomFirePrototype = "FireNRFHELL";

    /// <summary>
    /// </summary>
    [DataField]
    public float PulseFireChance = 0.3f;

    /// <summary>
    /// </summary>
    [DataField]
    public int FireRadius = 3;

    /// <summary>
    /// </summary>
    [DataField]
    public int SupercriticalFireCount = 3;
}