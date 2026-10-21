namespace Content.Shared._VG.Status;

/// <summary>
///     Raised on a mob that should vomit the vg13 way (<c>vomit()</c>). Handled on the server, since the vomit puddle
///     is.
/// </summary>
[ByRefEvent]
public readonly record struct VgVomitEvent(bool Instant = false);
