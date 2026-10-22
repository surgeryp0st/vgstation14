using Robust.Shared.GameStates;

namespace Content.Shared._VG.Species;

/// <summary>
///     vg13 CONTAINPLASMAMAN: a suit and a helmet with this keep a plasmaman from igniting in oxygen. Worn together the
///     suit also puts out the fire (at most every 10 s) and holds the wearer at body temperature.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgPlasmaContainmentComponent : Component
{
    /// <summary>
    ///     Only the suit extinguishes and regulates temperature.
    /// </summary>
    [DataField]
    public bool Regulates;

    [DataField]
    public TimeSpan ExtinguishCooldown = TimeSpan.FromSeconds(10);

    [ViewVariables]
    public TimeSpan NextExtinguish;
}
