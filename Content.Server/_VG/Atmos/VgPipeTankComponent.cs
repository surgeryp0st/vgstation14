using Content.Shared.Atmos;

namespace Content.Server._VG.Atmos;

/// <summary>
///     vg13's pressure tank (<c>/obj/machinery/atmospherics/unary/tank</c>): a big pipe-connected vessel that starts
///     full of gas at <see cref="Pressure"/>. Its volume is its pipe node's.
/// </summary>
[RegisterComponent]
public sealed partial class VgPipeTankComponent : Component
{
    /// <summary>What the tank starts with, as fractions of the mixture. Empty means an empty tank.</summary>
    [DataField]
    public Dictionary<Gas, float> Gases = new();

    /// <summary>vg13 STARTING_PRESSURE, 45 atmospheres.</summary>
    [DataField]
    public float Pressure = 45 * Atmospherics.OneAtmosphere;

    [DataField]
    public float Temperature = Atmospherics.T20C;

    [ViewVariables]
    public bool Filled;
}
