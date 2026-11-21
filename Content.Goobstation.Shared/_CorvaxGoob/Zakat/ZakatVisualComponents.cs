using Content.Shared.Roles.Components;
using Robust.Shared.GameStates;

namespace Content.Goobstation.Shared._CorvaxGoob.Zakat;

/// <summary>
/// The disease is driving this body right now: blood runs from its eyes. Visual only.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatBleedingEyesComponent : Component;

/// <summary>
/// «гнилые наросты»: flesh growths and tentacles over the body. Visual only.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ZakatRotGrowthsComponent : Component;

/// <summary>
/// Mind role of a «раб улья».
/// </summary>
[RegisterComponent]
public sealed partial class ZakatSlaveRoleComponent : BaseMindRoleComponent;
