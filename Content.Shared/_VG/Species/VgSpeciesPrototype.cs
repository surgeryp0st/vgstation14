using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Species;

/// <summary>
///     The numbers of a vg13 <c>/datum/species</c> (code/modules/mob/living/carbon/species.dm). Attached to a mob by
///     <see cref="VgSpeciesComponent"/>. Fields vg13 doesn't wire up (brute_mod, burn_resist, ...) are left out.
/// </summary>
[Prototype("vgSpecies")]
public sealed partial class VgSpeciesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     vg13 attack_verb: what an empty-hand harm attack is called.
    /// </summary>
    [DataField]
    public LocId AttackVerb = "vg-attack-verb-punches";

    /// <summary>
    ///     vg13 max_hurt_damage: an empty-hand attack does rand(0, this), and 0 is a miss.
    /// </summary>
    [DataField]
    public int MaxHurtDamage = 5;

    /// <summary>
    ///     vg13 punch_damage: added to every empty-hand attack.
    /// </summary>
    [DataField]
    public int PunchDamage;

    /// <summary>
    ///     vg13 punch_sharpness.
    /// </summary>
    [DataField]
    public float PunchSharpness;

    /// <summary>
    ///     vg13 default_mutations has M_CLAWS: +3 damage and at least 1.5 sharpness without gloves.
    /// </summary>
    [DataField]
    public bool Claws;

    /// <summary>
    ///     vg13 move_speed_mod: added to the movement delay tally (higher is slower).
    /// </summary>
    [DataField]
    public float MoveSpeedMod;

    /// <summary>
    ///     vg13 move_speed_multiplier.
    /// </summary>
    [DataField]
    public float MoveSpeedMultiplier = 1;

    /// <summary>
    ///     vg13 has_organ: the internal organs this species has. Anything else is removed from the body.
    /// </summary>
    [DataField]
    public List<string> Organs = new() { "heart", "lungs", "liver", "kidneys", "brain", "appendix", "eyes" };

    /// <summary>
    ///     vg13 brute_mod and burn_mod: multiply the damage the limbs take (organic limbs only).
    /// </summary>
    [DataField]
    public float BruteMod = 1;

    [DataField]
    public float BurnMod = 1;

    /// <summary>
    ///     vg13 tox_mod: multiplies every change in toxin damage, healing included.
    /// </summary>
    [DataField]
    public float ToxMod = 1;

    /// <summary>
    ///     vg13 rad_mod: multiplies the radiation dose taken.
    /// </summary>
    [DataField]
    public float RadMod = 1;

    /// <summary>
    ///     vg13 NO_BREATHE: doesn't breathe, and takes no harm from the air.
    /// </summary>
    [DataField]
    public bool NoBreathe;

    /// <summary>
    ///     vg13 SPECIES_NO_MOUTH: can't eat or drink.
    /// </summary>
    [DataField]
    public bool NoMouth;

    /// <summary>
    ///     vg13 IS_PLANT. With no mouth, a plant person absorbs food it stands on.
    /// </summary>
    [DataField]
    public bool IsPlant;

    /// <summary>
    ///     vg13 plasmaman: bursts into flame in air with at least 1% oxygen unless a containment suit and helmet are worn.
    /// </summary>
    [DataField]
    public bool IgnitesInOxygen;

    /// <summary>
    ///     vg13 NO_BLOOD.
    /// </summary>
    [DataField]
    public bool NoBlood;

    /// <summary>
    ///     vg13 NO_PAIN: no pain, no traumatic shock.
    /// </summary>
    [DataField]
    public bool NoPain;

    /// <summary>
    ///     vg13 RAD_ABSORB: radiation is absorbed as nutrition and healing instead of poisoning.
    /// </summary>
    [DataField]
    public bool RadAbsorb;

    // vg13 body temperature levels, kept here for the temperature port.
    [DataField]
    public float BodyTemperature = 310.15f;

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
}
