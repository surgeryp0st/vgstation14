using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Power;

/// <summary>
///     vg13's idle_power_usage for a machine, keyed by its entity prototype id; children inherit through their parents.
///     Kept apart from the entity prototypes so upstream SS14 machines aren't edited. Machines vg13 gives no idle use
///     are left alone, since a zero load would make SS14 treat them as powered with no supply.
/// </summary>
[Prototype("vgPowerUse")]
public sealed partial class VgPowerUsePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Watts drawn while powered and idle.</summary>
    [DataField(required: true)]
    public float Idle;
}
