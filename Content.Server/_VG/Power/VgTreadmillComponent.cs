namespace Content.Server._VG.Power;

/// <summary>
///     vg13's treadmill generator (code/modules/power/treadmill.dm): a mob standing on it and walking into its facing edge
///     burns nutrition and adds <see cref="BumpEnergy"/> to the power it produces that cycle.
/// </summary>
[RegisterComponent]
public sealed partial class VgTreadmillComponent : Component
{
    /// <summary>vg13 DEFAULT_BUMP_ENERGY.</summary>
    [DataField]
    public float BumpEnergy = 400;

    /// <summary>vg13 power_efficiency: the average rating of its capacitors, 1 to 3.</summary>
    [DataField]
    public float Efficiency = 1;

    /// <summary>Nutrition burned per bump (vg13 HUNGER_FACTOR * 2).</summary>
    [DataField]
    public float BumpNutrition = 0.3f;

    /// <summary>Seconds between power cycles (vg13's machine process).</summary>
    [DataField]
    public float CycleTime = 2;

    /// <summary>Power gathered so far this cycle.</summary>
    [ViewVariables]
    public float CountPower;

    /// <summary>Power the last cycle produced, which is what the treadmill supplies now.</summary>
    [ViewVariables]
    public float TickPower;

    [ViewVariables]
    public TimeSpan NextCycle;

    /// <summary>How far each runner is towards its next bump (a bump per tile of walking).</summary>
    [ViewVariables]
    public Dictionary<EntityUid, float> Progress = new();
}
