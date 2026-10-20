using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VG.Life;

/// <summary>
///     Gives a mob a vgstation13-style Life() tick, which processes organs, wounds, breathing, chemicals and so on.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(VgLifeSystem))]
public sealed partial class VgLifeComponent : Component
{
    /// <summary>
    ///     vg13's mob subsystem runs Life() every 2 seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>
    ///     vg13 life_tick: how many Life() ticks this mob has had.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LifeTick;
}

/// <summary>
///     Raised on a mob every vg13 Life() tick. Predicted for the client's own mob.
/// </summary>
[ByRefEvent]
public readonly record struct VgLifeTickEvent(int LifeTick);
