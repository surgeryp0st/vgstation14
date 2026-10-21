namespace Content.Server._VG.Fire;

/// <summary>
///     A vg13 fire (obj/effect/fire) burning on a tile. <see cref="VgFireSystem"/> runs it.
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
