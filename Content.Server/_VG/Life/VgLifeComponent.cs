using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._VG.Life;

/// <summary>
///     Gives a mob a vgstation13-style Life() tick, which processes organs, wounds, breathing, chemicals and so on.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(VgLifeSystem))]
public sealed partial class VgLifeComponent : Component
{
    /// <summary>
    ///     vg13's mob subsystem runs Life() every 2 seconds.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>
    ///     vg13 life_tick: how many Life() ticks this mob has had.
    /// </summary>
    [DataField]
    public int LifeTick;
}

/// <summary>
///     Raised on a mob every vg13 Life() tick.
/// </summary>
[ByRefEvent]
public readonly record struct VgLifeTickEvent(int LifeTick);
