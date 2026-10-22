using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 <c>flammable = TRUE</c> on a floor turf: the floor tile with this id burns like a burnable thing.
/// </summary>
[Prototype("vgFlammableTile")]
public sealed partial class VgFlammableTilePrototype : IPrototype
{
    /// <summary>
    ///     The tile definition's id.
    /// </summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<VgThermalMaterialPrototype> Material;

    /// <summary>
    ///     vg13 thermal_mass; every flammable vg13 floor has 5.
    /// </summary>
    [DataField]
    public float ThermalMass = 5;
}

/// <summary>
///     The burning part of a flammable floor tile: a hidden <see cref="VgBurnableComponent"/> standing in for the tile,
///     spawned when a fire first looks at it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgFloorFuelComponent : Component;
