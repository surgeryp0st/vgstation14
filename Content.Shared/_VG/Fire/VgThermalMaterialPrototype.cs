using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 <c>/datum/thermal_material</c> (code/modules/ZAS/Fire.dm and __DEFINES/setup.dm): what a burnable thing is
///     made of, which says when it catches fire and what it gives off while burning.
/// </summary>
[Prototype("vgThermalMaterial")]
public sealed partial class VgThermalMaterialPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     vg13 autoignition_temperature, K.
    /// </summary>
    [DataField(required: true)]
    public float AutoignitionTemperature;

    /// <summary>
    ///     vg13 heating_value (higher heating value), MJ/kg.
    /// </summary>
    [DataField(required: true)]
    public float HeatingValue;

    /// <summary>
    ///     vg13 molecular_weight, kg/mol.
    /// </summary>
    [DataField(required: true)]
    public float MolecularWeight;

    /// <summary>
    ///     vg13 fuel_ox_ratio: moles of fuel burnt per mole of oxygen.
    /// </summary>
    [DataField(required: true)]
    public float FuelOxRatio;

    /// <summary>
    ///     vg13 flame_temp, K.
    /// </summary>
    [DataField(required: true)]
    public float FlameTemperature;
}
