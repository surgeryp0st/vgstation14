using Robust.Shared.GameStates;

namespace Content.Shared._VG.Status;

/// <summary>
///     vg13 stun baton: its stamina hits (only made when it's on and charged) become vg13's apply_baton_effect() on
///     vg13 mobs instead.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgStunOnHitComponent : Component
{
    /// <summary>
    ///     vg13 stunforce: Knockdown() and Stun() in Life() ticks.
    /// </summary>
    [DataField]
    public float StunForce = 10;

    /// <summary>
    ///     vg13 apply_effect(10, STUTTER).
    /// </summary>
    [DataField]
    public float Stutter = 10;
}

/// <summary>
///     vg13 projectile stun, weaken, stutter and agony (/obj/item/projectile vars), in Life() ticks and halloss.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgStunOnCollideComponent : Component
{
    [DataField]
    public float Stun;

    [DataField]
    public float Weaken;

    [DataField]
    public float Stutter;

    [DataField]
    public float Agony;
}
