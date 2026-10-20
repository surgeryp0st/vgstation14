using Content.Shared.Atmos;

namespace Content.Shared._VG.Breathing;

/// <summary>
///     Breathes the vg13 way, once per Life() tick (vg13 breathe() and the lungs' lung_gas handlers). Defaults are
///     human lungs; species that don't breathe (vg13 NO_BREATHE) don't have this.
/// </summary>
[RegisterComponent]
public sealed partial class VgBreathingComponent : Component
{
    /// <summary>
    ///     vg13 species breath_type: the gas the lungs live on.
    /// </summary>
    [DataField]
    public Gas BreathGas = Gas.Oxygen;

    /// <summary>
    ///     vg13 metabolizable min_pp/max_pp, kPa.
    /// </summary>
    [DataField]
    public float MinPressure = 16;

    [DataField]
    public float MaxPressure = 140;

    /// <summary>
    ///     vg13 waste gas (exhaled) and the partial pressure where it starts poisoning you.
    /// </summary>
    [DataField]
    public Gas WasteGas = Gas.CarbonDioxide;

    [DataField]
    public float WasteMaxPressure = 10;

    /// <summary>
    ///     vg13 toxic gases: partial pressure where they start dealing toxin damage, the same with a gas mask on
    ///     (0 = masks don't help), and the damage multiplier.
    /// </summary>
    [DataField]
    public List<VgToxicGas> ToxicGases = new() { new VgToxicGas { Gas = Gas.Plasma, MaxPressure = 0.5f, MaxPressureMask = 5, Multiplier = 0.1f } };

    /// <summary>
    ///     vg13 sleep_agent (N2O): giggles, then paralysis, then sleep.
    /// </summary>
    [DataField]
    public Gas SleepGas = Gas.NitrousOxide;

    [DataField]
    public float GigglePressure = 0.15f;

    [DataField]
    public float ParalysePressure = 1;

    [DataField]
    public float SleepPressure = 5;

    // vg13 species cold_level_1-3 and heat_level_1-3, K.
    [DataField]
    public float ColdLevel1 = 220;

    [DataField]
    public float ColdLevel2 = 200;

    [DataField]
    public float ColdLevel3 = 120;

    [DataField]
    public float HeatLevel1 = 360;

    [DataField]
    public float HeatLevel2 = 400;

    [DataField]
    public float HeatLevel3 = 1000;

    /// <summary>
    ///     vg13 co2overloadtime: when the waste gas started building up.
    /// </summary>
    [ViewVariables]
    public TimeSpan? WasteOverloadStart;
}

[DataDefinition]
public sealed partial class VgToxicGas
{
    [DataField(required: true)]
    public Gas Gas;

    [DataField]
    public float MaxPressure = 0.5f;

    [DataField]
    public float MaxPressureMask;

    [DataField]
    public float Multiplier = 0.1f;
}
