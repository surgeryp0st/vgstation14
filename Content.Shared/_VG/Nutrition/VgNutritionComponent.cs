using Robust.Shared.GameStates;

namespace Content.Shared._VG.Nutrition;

/// <summary>
///     vg13 nutrition: a number that burns down every Life() tick and that food and drink reagents add to. Replaces
///     SS14's hunger and thirst, which are removed from the mob. Starvation only exists in vg13's hardcore mode, which
///     is off by default and not ported.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VgNutritionComponent : Component
{
    /// <summary>
    ///     vg13 OVEREAT_THRESHOLD.
    /// </summary>
    public const float OverEatThreshold = 450;

    /// <summary>
    ///     vg13 mob <c>nutrition</c>, 400 at round start.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Nutrition = 400;

    /// <summary>
    ///     vg13 <c>calorie_burn_rate</c> (HUNGER_FACTOR): nutrition burned each Life() tick.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CalorieBurnRate = 0.15f;

    /// <summary>
    ///     vg13 <c>overeatduration</c>: Life() ticks spent above the overeat threshold, capped at 600.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int OverEatDuration;
}
