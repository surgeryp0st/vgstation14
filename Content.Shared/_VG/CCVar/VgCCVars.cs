using Robust.Shared.Configuration;

namespace Content.Shared._VG.CCVar;

/// <summary>
///     vgstation14 CVars. Most mirror vgstation13's config/game_options.txt, with its shipped values as defaults.
///     Gameplay ones are replicated so clients can predict with the same values.
/// </summary>
[CVarDefs]
public sealed class VgCCVars
{
    /// <summary>
    ///     vg13 BONES_CAN_BREAK: limbs fracture after enough brute damage.
    /// </summary>
    public static readonly CVarDef<bool> BonesCanBreak =
        CVarDef.Create("vg.bones_can_break", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     vg13 LIMBS_CAN_BREAK: limbs can be severed or blown apart, and limb damage is capped at max damage.
    /// </summary>
    public static readonly CVarDef<bool> LimbsCanBreak =
        CVarDef.Create("vg.limbs_can_break", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     vg13 ORGAN_HEALTH_MULTIPLIER (as a fraction): scales how much damage limbs take before breaking or falling off.
    /// </summary>
    public static readonly CVarDef<float> OrganHealthMultiplier =
        CVarDef.Create("vg.organ_health_multiplier", 1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     vg13 ORGAN_REGENERATION_MULTIPLIER (as a fraction): scales natural wound healing.
    /// </summary>
    public static readonly CVarDef<float> OrganRegenerationMultiplier =
        CVarDef.Create("vg.organ_regeneration_multiplier", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     vg13 ZAS CLOTH_CONTAMINATION: plasma in the air gets into the clothes of people not sealed against it.
    /// </summary>
    public static readonly CVarDef<bool> PlasmaClothContamination =
        CVarDef.Create("vg.plasma.cloth_contamination", true, CVar.SERVER);

    /// <summary>
    ///     vg13 ZAS CONTAMINATION_LOSS: toxin damage a Life() tick for each contaminated thing someone has on them.
    /// </summary>
    public static readonly CVarDef<float> PlasmaContaminationLoss =
        CVarDef.Create("vg.plasma.contamination_loss", 0.02f, CVar.SERVER);

    /// <summary>
    ///     vg13 ZAS EYE_BURNS: plasma in the air burns uncovered eyes.
    /// </summary>
    public static readonly CVarDef<bool> PlasmaEyeBurns =
        CVarDef.Create("vg.plasma.eye_burns", true, CVar.SERVER);
}
