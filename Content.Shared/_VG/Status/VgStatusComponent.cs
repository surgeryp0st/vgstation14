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
