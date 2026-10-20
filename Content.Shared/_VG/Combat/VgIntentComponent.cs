using Content.Shared._VG.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VG.Combat;

/// <summary>
///     vg13 intents, in vg13's cycling order (intent_numeric()).
/// </summary>
[Serializable, NetSerializable]
public enum VgIntent : byte
{
    Help,
    Disarm,
    Grab,
    Harm,
}

/// <summary>
///     vg13 controls: the intent (a_intent) deciding what an empty-handed click on a mob does, and the zone selector
///     (zone_sel) deciding where attacks land. Replaces SS14's combat mode for this mob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgCombatSystem))]
public sealed partial class VgIntentComponent : Component
{
    [DataField, AutoNetworkedField]
    public VgIntent Intent = VgIntent.Help;

    /// <summary>
    ///     A limb id from <see cref="VgLimbs"/>, or eyes/mouth.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Zone = VgLimbs.Chest;

    /// <summary>
    ///     vg13 attack_delayer: clicking a mob empty-handed blocks the next one for a moment.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUnarmed;

    /// <summary>
    ///     vg13 delayNextAttack(10) for empty-handed clicks on mobs.
    /// </summary>
    [DataField]
    public TimeSpan UnarmedDelay = TimeSpan.FromSeconds(1);
}

/// <summary>
///     Sent by the HUD intent selector. Predicted.
/// </summary>
[Serializable, NetSerializable]
public sealed class VgSetIntentEvent(VgIntent intent) : EntityEventArgs
{
    public readonly VgIntent Intent = intent;
}

/// <summary>
///     Sent by the HUD zone selector. Predicted.
/// </summary>
[Serializable, NetSerializable]
public sealed class VgSetZoneEvent(string zone) : EntityEventArgs
{
    public readonly string Zone = zone;
}
