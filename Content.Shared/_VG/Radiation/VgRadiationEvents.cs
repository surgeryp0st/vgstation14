namespace Content.Shared._VG.Radiation;

/// <summary>
///     Raised on an irradiated mob before the long-term symptoms of its dose. Blocked when something in its blood holds
///     them off (vg13 arithrazine); the server answers it, since the bloodstream is server-side.
/// </summary>
[ByRefEvent]
public record struct VgRadiationSymptomsAttemptEvent(bool Blocked = false);

/// <summary>
///     vg13 radiation nosebleed: <c>drip(1)</c>, with a message if any blood came. Server-side, like the blood.
/// </summary>
[ByRefEvent]
public record struct VgNosebleedEvent;
