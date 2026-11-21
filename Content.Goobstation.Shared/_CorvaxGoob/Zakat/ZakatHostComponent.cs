namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// An organism that carries «Закат». Holds the symptoms bought for THIS host only, and their timers.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatHostComponent : Component
{
    [ViewVariables]
    public EntityUid Zakat;

    [ViewVariables]
    public HashSet<ZakatSymptom> Symptoms = new();

    /// <summary>
    /// «порабощение»: the host is a «раб улья».
    /// </summary>
    [ViewVariables]
    public bool Enslaved;

    [ViewVariables]
    public HashSet<ZakatMutation> Mutations = new();

    /// <summary>
    /// Next time each periodic action of a bought symptom fires.
    /// </summary>
    [ViewVariables]
    public Dictionary<ZakatTimer, TimeSpan> Next = new();

    /// <summary>
    /// Last time each periodic action fired (for logs and tests).
    /// </summary>
    [ViewVariables]
    public Dictionary<ZakatTimer, TimeSpan> LastFired = new();

    /// <summary>
    /// Epileptic seizure in progress: sleep starts at this time.
    /// </summary>
    [ViewVariables]
    public TimeSpan? SeizureSleepAt;

    /// <summary>
    /// The post-seizure sleep ends at this time.
    /// </summary>
    [ViewVariables]
    public TimeSpan? SeizureWakeAt;
}
