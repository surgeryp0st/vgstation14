using Robust.Shared.GameStates;

namespace Content.Shared._VG.Movement;

/// <summary>
///     Moves at vg13 speeds, with vg13's slowdowns (see <see cref="VgMovementSystem"/>). Needs a vg13 body.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgMovementComponent : Component
{
    /// <summary>
    ///     Where this mob has been lately while pulling something, for <see cref="VgPullController"/>. Not networked;
    ///     each side records it as the mob moves.
    /// </summary>
    [ViewVariables]
    public List<System.Numerics.Vector2> PullTrail = new();

    [ViewVariables]
    public bool PullDriving;
}
