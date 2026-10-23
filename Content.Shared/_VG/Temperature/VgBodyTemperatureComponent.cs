using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Temperature;

/// <summary>
///     vg13 body temperature (thermoregulation.dm, handle_environment(), handle_hypothermia.dm): the body loses heat to
///     colder air and gains it from hotter air through its clothing, burns calories and sweats to keep warm or cool,
///     takes burns past its species' limits, and goes through hypothermia's stages. The temperature itself is SS14's
///     <c>TemperatureComponent</c> (server-side, as atmos is); the hypothermia stage is networked for movement.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VgBodyTemperatureComponent : Component
{
    [DataField, AutoNetworkedField]
    public VgHypothermia Hypothermia;

    /// <summary>
    ///     vg13 movement tally from the cold: 2 per hypothermia stage, or a slime person's sluggishness below 183 K.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Tally;

    /// <summary>
    ///     vg13 thermal_loss_multiplier: heat lost to the air is multiplied by this.
    /// </summary>
    [DataField]
    public float ThermalLossMultiplier = 1;

    /// <summary>
    ///     Tiles walked since the last Life() tick; vg13 burns calories, and so makes heat, on every step.
    /// </summary>
    [ViewVariables]
    public int Steps;

    [ViewVariables]
    public int RunSteps;
}

/// <summary>
///     vg13 hypothermia stages (the Swiss staging system).
/// </summary>
[Serializable, NetSerializable]
public enum VgHypothermia : byte
{
    /// <summary>
    ///     Above 34 C.
    /// </summary>
    None,

    /// <summary>
    ///     32-34 C: awake and shivering.
    /// </summary>
    Mild,

    /// <summary>
    ///     28-32 C: drowsy, dizzy, slurring, fumbling.
    /// </summary>
    Moderate,

    /// <summary>
    ///     24-28 C: unconscious.
    /// </summary>
    Severe,

    /// <summary>
    ///     Below 24 C: no vital signs, no breathing.
    /// </summary>
    Profound,
}
