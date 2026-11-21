// Goob port stub — FarHorizons IPC not present on Abisfera.
using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Compat;

/// <summary>
/// Marker used by vampire drain logic to skip silicon/IPC targets without blood.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IPCBatteryComponent : Component
{
}
