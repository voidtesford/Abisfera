namespace Content.Shared._CorvaxGoob.Zakat;

/// <summary>
/// Abisfera - «Закат»: the entity cannot breathe until <see cref="Until"/> (real suffocation, see RespiratorSystem).
/// </summary>
[RegisterComponent]
public sealed partial class ZakatBreathBlockedComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}

/// <summary>
/// Abisfera - «Закат» «подавление иммунитета»: passive damage that heals (natural regeneration) does nothing.
/// </summary>
[RegisterComponent]
public sealed partial class PassiveRegenSuppressedComponent : Component;

/// <summary>
/// Abisfera - «Закат» «гемофилия»: bleeding never stops by itself (no clotting, wounds don't stop bleeding on their own).
/// </summary>
[RegisterComponent]
public sealed partial class ZakatHemophiliaComponent : Component;
