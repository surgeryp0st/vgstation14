using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 <c>possible_fuels</c> (code/__HELPERS/global_lists.dm): a reagent that burns when it's on a tile with a fire,
///     keyed by the id of the reagent.
/// </summary>
[Prototype("vgFuel")]
public sealed partial class VgFuelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     vg13 max_temperature, K.
    /// </summary>
    [DataField(required: true)]
    public float MaxTemperature;

    /// <summary>
    ///     vg13 thermal_energy_transfer: heat given to the fire each tick.
    /// </summary>
    [DataField(required: true)]
    public float ThermalEnergyTransfer;

    /// <summary>
    ///     vg13 consumption_rate: units burnt each tick.
    /// </summary>
    [DataField(required: true)]
    public float ConsumptionRate;

    /// <summary>
    ///     vg13 o2_cons: oxygen used each tick.
    /// </summary>
    [DataField(required: true)]
    public float OxygenConsumption;

    /// <summary>
    ///     vg13 co2_cons: carbon dioxide used each tick (negative produces it).
    /// </summary>
    [DataField]
    public float CarbonDioxideConsumption;
}
