using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 plasma walls (PlasmaBurn()): something hot enough sets it alight, and it goes up in a burst of hot plasma,
///     leaving its girder and setting off the plasma walls around it.
/// </summary>
[RegisterComponent]
public sealed partial class VgPlasmaBurnComponent : Component
{
    /// <summary>
    ///     What's left where it stood (vg13 girder_type).
    /// </summary>
    [DataField]
    public EntProtoId? LeavesBehind = "Girder";

    /// <summary>
    ///     It only burns from something hotter than this, K.
    /// </summary>
    [DataField]
    public float MinTemperature = 300;

    /// <summary>
    ///     Plasma it gives off, mol (vg13 toxinsToDeduce).
    /// </summary>
    [DataField]
    public float Plasma = 20;

    /// <summary>
    ///     How hot that plasma is, K (vg13 400 C).
    /// </summary>
    [DataField]
    public float PlasmaTemperature = 673.15f;

    /// <summary>
    ///     Plasma walls this many tiles away (a square, BYOND range()) catch at a quarter of the temperature.
    /// </summary>
    [DataField]
    public int ChainRange = 3;

    [ViewVariables]
    public bool Burning;
}
