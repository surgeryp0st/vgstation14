namespace Content.Shared._VG.Slip;

/// <summary>
///     vg13's kinds of slip (TURF_WET_WATER, TURF_WET_LUBE, TURF_WET_ICE, and things you step on).
/// </summary>
public enum VgSlipKind : byte
{
    Water,
    Lube,
    Ice,
    Item,
}

/// <summary>
///     How a slippery thing slips a vg mob. Puddles work it out from what they hold; things without one slip like a
///     banana peel.
/// </summary>
[RegisterComponent]
public sealed partial class VgSlipComponent : Component
{
    [DataField]
    public VgSlipKind Kind = VgSlipKind.Item;

    /// <summary>
    ///     vg13 stun_amount in Life() ticks, for things you step on (the wet floors have vg13's fixed numbers).
    /// </summary>
    [DataField]
    public float Stun = 2;

    /// <summary>
    ///     vg13 weaken_amount in Life() ticks.
    /// </summary>
    [DataField]
    public float Knockdown = 2;
}

/// <summary>
///     vg13 /obj/effect/overlay/puddle/ice: frozen over by cryotheum in freezing air. It thickens below 0 C and melts
///     above it, faster the warmer it is.
/// </summary>
[RegisterComponent]
public sealed partial class VgIceComponent : Component
{
    /// <summary>
    ///     vg13 ice_thickness: it goes when this drops below 0. Capped at 100.
    /// </summary>
    [DataField]
    public float Thickness = 5;
}
