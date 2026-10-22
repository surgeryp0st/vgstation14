using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VG.Radiation;

/// <summary>
///     vg13 radiation dose: radiation sources and radioactive reagents add to <see cref="Radiation"/>, which burns off
///     over time while hurting the mob, and builds <see cref="RadTick"/> (how long it has been irradiated), which brings
///     the long-term symptoms. Replaces SS14's instant radiation damage on this mob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgRadiationSystem))]
public sealed partial class VgRadiationComponent : Component
{
    /// <summary>
    ///     vg13 <c>radiation</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radiation;

    /// <summary>
    ///     vg13 <c>rad_tick</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int RadTick;

    /// <summary>
    ///     vg13 NEARSIGHTED from radiation-bruised eyes, which lasts 10 seconds and holds off the next bout.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NearsightedUntil;
}
