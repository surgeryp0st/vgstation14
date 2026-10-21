using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     vg13's per-reagent mob behaviour (<c>on_mob_life</c>, <c>on_overdose</c>, <c>custom_metabolism</c>), keyed by the
///     id of the <c>reagent</c> prototype it belongs to. The reagent itself stays an ordinary SS14 reagent so SS14's
///     solution and reaction engine keeps working.
/// </summary>
[Prototype("vgReagent")]
public sealed partial class VgReagentPrototype : IPrototype
{
    /// <summary>
    ///     vg13 REAGENTS_METABOLISM.
    /// </summary>
    public const float DefaultMetabolism = 0.2f;

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     vg13 custom_metabolism: units removed from the body per Life() tick.
    /// </summary>
    [DataField]
    public float Metabolism = DefaultMetabolism;

    /// <summary>
    ///     vg13 overdose_am: overdosing starts at this many units in the body. 0 for never.
    /// </summary>
    [DataField]
    public float OverdoseAmount;

    /// <summary>
    ///     vg13 overdose_tick: overdosing starts after this many Life() ticks in the body. 0 for never.
    /// </summary>
    [DataField]
    public int OverdoseTick;

    /// <summary>
    ///     vg13 pain_resistance: how much of the pain the mob feels this takes away while in the body.
    /// </summary>
    [DataField]
    public float PainResistance;

    /// <summary>
    ///     vg13 nutriment_factor: nutrition added every Life() tick the reagent is in the body.
    /// </summary>
    [DataField]
    public float NutrimentFactor;

    /// <summary>
    ///     vg13 on_mob_life(), every Life() tick the reagent is in the body.
    /// </summary>
    [DataField]
    public List<VgReagentEffect> Effects = new();

    /// <summary>
    ///     vg13 on_overdose(), every Life() tick while overdosing.
    /// </summary>
    [DataField]
    public List<VgReagentEffect> OverdoseEffects = new();
}
