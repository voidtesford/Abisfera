namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// «раб улья»: an enslaved host. Mutations live in <see cref="Content.Goobstation.Shared._CorvaxGoob.Zakat.ZakatHostComponent.Mutations"/>.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatSlaveComponent : Component
{
    [ViewVariables]
    public EntityUid Zakat;

    [ViewVariables]
    public EntityUid? HarpoonAction;

    [ViewVariables]
    public EntityUid? DevourAction;

    /// <summary>«когти»: the action and the claws in the hand while they are out.</summary>
    [ViewVariables]
    public EntityUid? ClawsAction, ClawsItem;

    /// <summary>upd2: the gore effect on the corpse while it is being devoured.</summary>
    [ViewVariables]
    public EntityUid? DevourEffect;

    [ViewVariables]
    public TimeSpan NextRegen;

    [ViewVariables]
    public TimeSpan NextSplash;

    /// <summary>
    /// «гнилые наросты» raised the damage thresholds already.
    /// </summary>
    [ViewVariables]
    public bool ThresholdsRaised;
}

/// <summary>
/// Blood splashed from «гнилые наросты». Bare skin that steps in it or touches it catches the strain.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatInfectiousPuddleComponent : Component
{
    [ViewVariables]
    public EntityUid Zakat;

    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public TimeSpan NextCheck;
}

/// <summary>
/// Рост 4: where the disease's vines have crept (lives on the disease).
/// </summary>
[RegisterComponent]
public sealed partial class ZakatVinesComponent : Component
{
    [ViewVariables]
    public EntityUid? Grid;

    [ViewVariables]
    public HashSet<Vector2i> Covered = new();

    [ViewVariables]
    public Queue<Vector2i> Frontier = new();

    /// <summary>
    /// Walls, closed armored doors, windows being smashed: tried again from time to time.
    /// </summary>
    [ViewVariables]
    public HashSet<Vector2i> Blocked = new();

    [ViewVariables]
    public TimeSpan NextStep, NextDamage, NextRetry;
}

/// <summary>
/// A host under the vaccine: symptoms recede one by one, then (with none left) the disease goes.
/// </summary>
[RegisterComponent]
public sealed partial class ZakatVaccinatedComponent : Component
{
    [ViewVariables]
    public EntityUid? Zakat;

    /// <summary>
    /// Symptoms still to roll back.
    /// </summary>
    [ViewVariables]
    public int Rollback;

    /// <summary>
    /// The host had no symptoms: the dose cures.
    /// </summary>
    [ViewVariables]
    public bool Cure;

    [ViewVariables]
    public TimeSpan NextAt;
}
