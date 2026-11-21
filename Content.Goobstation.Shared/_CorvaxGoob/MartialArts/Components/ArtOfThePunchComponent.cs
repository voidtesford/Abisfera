using Content.Goobstation.Common.MartialArts;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared._CorvaxGoob.MartialArts.Components;

/// <summary>
/// Abisfera - «Искусство панча» (The Art of the Punch): the passives of the art. Added when the art is learned,
/// removed with the martial-arts knowledge. Unarmed = fists or worn gloves (boxing gloves).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArtOfThePunchComponent : Component
{
    /// <summary>Wind-up multiplier of unarmed swings (light, wide and «Тяжёлая атака»), on top of the mass multiplier.</summary>
    [DataField, AutoNetworkedField]
    public float WindupMultiplier = 0.8f;

    /// <summary>Attack rate multiplier of unarmed swings.</summary>
    [DataField, AutoNetworkedField]
    public float AttackRateMultiplier = 1.2f;

    /// <summary>«Маятник»: dodge cooldown multiplier.</summary>
    [DataField, AutoNetworkedField]
    public float DodgeCooldownMultiplier = 0.5f;

    /// <summary>«Долой зубы» knocks out a target whose total damage (this hit and the combo bonus included) is at least this.</summary>
    [DataField, AutoNetworkedField]
    public float KnockoutDamage = 80f;

    /// <summary>Forced sleep of a knocked out target.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan KnockoutTime = TimeSpan.FromSeconds(5);

    /// <summary>The «Маятник» alert (a swinging punching bag) shown while the art is known.</summary>
    [DataField]
    public ProtoId<AlertPrototype> PendulumAlert = "ArtOfThePunchPendulum";
}

/// <summary>Teaches «Искусство панча». On a mob (e.g. a job special) it is learned at once.</summary>
[RegisterComponent]
public sealed partial class GrantArtOfThePunchComponent : GrantMartialArtKnowledgeComponent
{
    [DataField]
    public override MartialArtsForms MartialArtsForm { get; set; } = MartialArtsForms.ArtOfThePunch;

    public override LocId? LearnMessage { get; set; } = "art-of-the-punch-success-learned";
}
