using Robust.Shared.Configuration;

namespace Content.Shared._VG.CCVar;

/// <summary>
///     vgstation14 CVars. Most mirror vgstation13's config/game_options.txt, with its shipped values as defaults.
/// </summary>
[CVarDefs]
public sealed class VgCCVars
{
    /// <summary>
    ///     vg13 BONES_CAN_BREAK: limbs fracture after enough brute damage.
    /// </summary>
    public static readonly CVarDef<bool> BonesCanBreak =
        CVarDef.Create("vg.bones_can_break", true, CVar.SERVERONLY);

    /// <summary>
    ///     vg13 LIMBS_CAN_BREAK: limbs can be severed or blown apart, and limb damage is capped at max damage.
    /// </summary>
    public static readonly CVarDef<bool> LimbsCanBreak =
        CVarDef.Create("vg.limbs_can_break", true, CVar.SERVERONLY);

    /// <summary>
    ///     vg13 ORGAN_HEALTH_MULTIPLIER (as a fraction): scales how much damage limbs take before breaking or falling off.
    /// </summary>
    public static readonly CVarDef<float> OrganHealthMultiplier =
        CVarDef.Create("vg.organ_health_multiplier", 1f, CVar.SERVERONLY);

    /// <summary>
    ///     vg13 ORGAN_REGENERATION_MULTIPLIER (as a fraction): scales natural wound healing.
    /// </summary>
    public static readonly CVarDef<float> OrganRegenerationMultiplier =
        CVarDef.Create("vg.organ_regeneration_multiplier", 2f, CVar.SERVERONLY);
}
