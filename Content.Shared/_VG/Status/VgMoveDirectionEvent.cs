using System.Numerics;

namespace Content.Shared._VG.Status;

/// <summary>
///     Raised on a mob moving under its own power, with the directions its input asks for, so vg13 effects can change
///     them (confusion). Raised from a // VG hook in SharedMoverController.HandleMobMovement.
/// </summary>
[ByRefEvent]
public record struct VgMoveDirectionEvent(Vector2 Walk, Vector2 Sprint);
