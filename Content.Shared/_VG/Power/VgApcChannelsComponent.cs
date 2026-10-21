using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Power;

/// <summary>
///     vg13 APC power channel states (apc.dm APC_CHANNEL_STATUS_*): a channel runs while it's On or AutoOn.
/// </summary>
[Serializable, NetSerializable]
public enum VgApcChannelState : byte
{
    Off = 0,
    AutoOff = 1,
    On = 2,
    AutoOn = 3,
}

/// <summary>
///     vg13 power_channel: what kind of power a machine uses.
/// </summary>
[Serializable, NetSerializable]
public enum VgPowerChannel : byte
{
    Equipment,
    Lighting,
    Environment,
}

/// <summary>
///     vg13's three APC channels. Everything powered through the APC's network is in its area: each machine uses the
///     equipment, lighting or environment channel, and while that channel is off the machine gets no power. The channels
///     cut themselves automatically as the APC's battery runs down.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VgApcChannelsComponent : Component
{
    [DataField, AutoNetworkedField]
    public VgApcChannelState Equipment = VgApcChannelState.AutoOn;

    [DataField, AutoNetworkedField]
    public VgApcChannelState Lighting = VgApcChannelState.AutoOn;

    [DataField, AutoNetworkedField]
    public VgApcChannelState Environment = VgApcChannelState.AutoOn;

    /// <summary>
    ///     vg13 longtermpower: rises while the APC can charge and falls while it can't (twice as fast); the channels only
    ///     cut themselves while it's below 0, so a brief dip doesn't switch things off.
    /// </summary>
    [DataField]
    public int LongTermPower = 10;
}

/// <summary>
///     Overrides which vg13 power channel a powered machine uses. Without it a machine uses the equipment channel, or
///     lighting for lights and environment for doors, vents and the like.
/// </summary>
[RegisterComponent]
public sealed partial class VgPowerChannelComponent : Component
{
    [DataField(required: true)]
    public VgPowerChannel Channel;
}
