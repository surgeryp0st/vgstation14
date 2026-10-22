using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VG.Status;

/// <summary>
///     vg13 halloss (pain damage that doesn't hurt health) and traumatic shock. Replaces SS14 stamina: stamina damage
///     on this mob becomes halloss. Stuns themselves use SS14's status effects, with vg13 durations.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgStatusSystem))]
public sealed partial class VgStatusComponent : Component
{
    /// <summary>
    ///     vg13 halloss. Over 100 the mob collapses from the pain.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Halloss;

    /// <summary>
    ///     vg13 pain_shock_stage: rises while pain is over <see cref="PainResist"/>, and brings stutters, collapses and
    ///     blackouts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ShockStage;

    /// <summary>
    ///     vg13 pain_numb: Life() ticks the mob feels no pain or shock at all (oxycodone).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int PainNumb;

    /// <summary>
    ///     Sum of the pain_resistance of the reagents in the body, set by the metabolism each Life() tick.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PainResistance;

    /// <summary>
    ///     vg13 losebreath: Life() ticks of not being able to breathe (strangled, ...).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LoseBreath;

    /// <summary>
    ///     vg13 drowsyness: Life() ticks of blurry, sleepy eyes with a 5% chance each tick of dropping off.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Drowsiness;

    /// <summary>
    ///     vg13 eye_blurry: Life() ticks of blurred sight. It wears off by one a tick (three under a blindfold), and not
    ///     while blind.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float EyeBlurry;

    /// <summary>
    ///     vg13 dizziness: the view sways, more the dizzier you are. Wears off one a Life() tick, five lying down.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Dizziness;

    /// <summary>
    ///     vg13 confused: Life() ticks of stepping diagonally to one side or the other of where you meant to go.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Confused;

    /// <summary>
    ///     vg13 sleeping: asleep until then. Shaking takes ten Life() ticks off it.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan SleepingUntil;

    /// <summary>
    ///     vg13 paralysis: out cold until then. Shaking takes three Life() ticks off it. Sleep and paralysis are kept
    ///     apart because both show as SS14's forced sleep, which lasts as long as the longer of the two.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ParalysedUntil;

    /// <summary>
    ///     vg13 BASE_CARBON_PAIN_RESIST.
    /// </summary>
    [DataField]
    public float PainResist = 80;

    /// <summary>
    ///     vg13 last_pain_message: the same pain message isn't repeated every time.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string LastPainMessage = string.Empty;

    /// <summary>
    ///     vg13 next_pain_time.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextPainTime;
}
