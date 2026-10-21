using Robust.Shared.Serialization;

namespace Content.Shared._VG.Fire;

/// <summary>
///     A vg13 fire (obj/effect/fire) burning on a tile. The server's VgFireSystem runs it; how it looks follows its level.
/// </summary>
[RegisterComponent]
public sealed partial class VgFireComponent : Component
{
    /// <summary>
    ///     vg13 burn_duration: how many fire ticks it has burnt, which makes it spread better.
    /// </summary>
    [ViewVariables]
    public int BurnDuration;

    /// <summary>
    ///     The last firelevel worked out for it (how intense the gas part of the fire is).
    /// </summary>
    [ViewVariables]
    public float FireLevel;
}

[Serializable, NetSerializable]
public enum VgFireVisuals : byte
{
    /// <summary>
    ///     1, 2 or 3 (vg13 fire.dmi key1 to key3): the bigger the firelevel, the bigger the flames.
    /// </summary>
    Size,
}
