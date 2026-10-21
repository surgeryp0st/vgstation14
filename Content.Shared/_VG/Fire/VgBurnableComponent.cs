using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 <c>flammable = TRUE</c>: this burns. Catches fire when the air around it is hot enough and has oxygen, or
///     when a flame touches it, and burns up its <see cref="ThermalMass"/> while giving heat to the air.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VgBurnableComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VgThermalMaterialPrototype> Material;

    /// <summary>
    ///     vg13 thermal_mass: a very loose estimate of its mass in kg, which is what burns away.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ThermalMass = 1;

    /// <summary>
    ///     Overrides the material's autoignition temperature (vg13 autoignition_temperature).
    /// </summary>
    [DataField]
    public float? AutoignitionTemperature;

    /// <summary>
    ///     vg13 on_fire.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool OnFire;

    /// <summary>
    ///     vg13 burntime: how long it has been burning. It can't burn away before <see cref="MinBurnTime"/>.
    /// </summary>
    [DataField]
    public TimeSpan BurnTime;

    /// <summary>
    ///     vg13 fire_protection: can't catch fire until then (it was just put out).
    /// </summary>
    [DataField]
    public TimeSpan FireProtectionUntil;
}
