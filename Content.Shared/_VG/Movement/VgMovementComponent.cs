using Robust.Shared.GameStates;

namespace Content.Shared._VG.Movement;

/// <summary>
///     Moves at vg13 speeds, with vg13's slowdowns (see <see cref="VgMovementSystem"/>). Needs a vg13 body.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgMovementComponent : Component;
