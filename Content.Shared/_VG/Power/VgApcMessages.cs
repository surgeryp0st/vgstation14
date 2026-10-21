using Robust.Shared.Serialization;

namespace Content.Shared._VG.Power;

/// <summary>
///     What the APC interface sets a channel to (vg13 apc.dm setsubsystem()): off, on, or auto (the APC cuts the channel
///     itself when its battery runs low).
/// </summary>
[Serializable, NetSerializable]
public enum VgApcChannelMode : byte
{
    Off,
    On,
    Auto,
}

[Serializable, NetSerializable]
public sealed class VgApcSetChannelMessage(VgPowerChannel channel, VgApcChannelMode mode) : BoundUserInterfaceMessage
{
    public readonly VgPowerChannel Channel = channel;
    public readonly VgApcChannelMode Mode = mode;
}
