using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VG.Combat;

/// <summary>
///     vg13 grab states (GRAB_PASSIVE..GRAB_KILL).
/// </summary>
[Serializable, NetSerializable]
public enum VgGrabState : byte
{
    Passive,
    Aggressive,
    Neck,

    /// <summary>
    ///     Tightening a neck grab into a strangle (a 10 second do-after).
    /// </summary>
    Upgrading,
    Kill,
}

/// <summary>
///     On a mob holding someone in a vg13 grab. The grab rides on an SS14 pull, whose virtual item in hand is the grab.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgGrabSystem))]
public sealed partial class VgGrabberComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Victim;

    [DataField, AutoNetworkedField]
    public VgGrabState State = VgGrabState.Passive;

    /// <summary>
    ///     The grab item in the grabber's hand.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Item;

    /// <summary>
    ///     vg13 delayNextAttack(10) after an upgrade.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpgrade;
}

/// <summary>
///     On a mob in someone's vg13 grab.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgGrabSystem))]
public sealed partial class VgGrabbedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Grabber;

    /// <summary>
    ///     vg13 delayNextMove(10) after trying to struggle free.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextEscape;
}

/// <summary>
///     vg13's /obj/item/weapon/grab in the grabber's hand: use it (or click the victim with it) to tighten, drop it to
///     let go, throw it to throw the victim.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VgGrabSystem))]
public sealed partial class VgGrabItemComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Grabber;

    [DataField, AutoNetworkedField]
    public EntityUid Victim;
}

[Serializable, NetSerializable]
public enum VgGrabVisuals : byte
{
    State,
    Layer,
}

[Serializable, NetSerializable]
public sealed partial class VgGrabStrangleDoAfterEvent : SimpleDoAfterEvent;
