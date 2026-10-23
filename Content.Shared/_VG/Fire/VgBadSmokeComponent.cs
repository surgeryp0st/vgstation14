namespace Content.Shared._VG.Fire;

/// <summary>
///     vg13 /obj/effect/smoke/bad: the smoke burning things give off. It blocks sight like any smoke, and a beam
///     fired through it loses half its damage for each puff. (vg13's choking effects on mobs never run there: its
///     base smoke affect() returns early for every carbon mob, so they aren't ported.)
/// </summary>
[RegisterComponent]
public sealed partial class VgBadSmokeComponent : Component;
