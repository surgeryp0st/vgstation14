using System.Linq;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    /// <summary>
    ///     vg13 BLOODLOSS_SPEED_MULTIPLIER.
    /// </summary>
    private const float BloodlossSpeedMultiplier = 0.75f;

    /// <summary>
    ///     Healed wounds stick around as scars and bruises on examine for this long.
    /// </summary>
    private static readonly TimeSpan HealedWoundLifetime = TimeSpan.FromMinutes(10);

    // Wound sizes, smallest first, picked by damage / 10 (vg13 typesof() order).
    private static readonly ProtoId<VgWoundPrototype>[] CutWounds =
        { "CutSmall", "CutDeep", "CutFlesh", "CutGaping", "CutGapingBig", "CutMassive" };

    private static readonly ProtoId<VgWoundPrototype>[] BruiseWounds =
        { "BruiseTiny", "BruiseSmall", "BruiseModerate", "BruiseLarge", "BruiseHuge", "BruiseMonumental" };

    private static readonly ProtoId<VgWoundPrototype>[] BurnWounds =
        { "BurnModerate", "BurnLarge", "BurnSevere", "BurnDeep", "BurnCarbonised" };

    private static readonly ProtoId<VgWoundPrototype> InternalBleedingWound = "InternalBleeding";

    /// <summary>
    ///     Opens a vein in the limb unless one already is.
    /// </summary>
    public void AddInternalBleeding(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        if (limb.Wounds.Any(IsInternal))
            return;

        OpenVein(ent, limb, 15);
        CustomPain(ent, Loc.GetString("vg-limb-internal-bleeding-rip", ("limb", Loc.GetString(limb.Name))));
    }

    /// <summary>
    ///     vg13 <c>wounds += new /datum/wound/internal_bleeding(damage)</c>: a bleeding vein, even beside another.
    /// </summary>
    public void OpenVein(Entity<VgBodyComponent> ent, VgLimb limb, float damage)
    {
        limb.Wounds.Add(NewWound(InternalBleedingWound, damage));
        limb.InternallyBleeding = true;
        Dirty(ent);
    }

    /// <summary>
    ///     vg13 createwound().
    /// </summary>
    private void CreateWound(Entity<VgBodyComponent> ent, VgLimb limb, VgWoundType type, float damage)
    {
        if (damage <= 0)
            return;

        // First see if an existing wound just tears open further.
        if (WidenWound(ent, limb, type, damage))
        {
            UpdateDamages(ent, limb);
            return;
        }

        var sizes = type switch
        {
            VgWoundType.Cut => CutWounds,
            VgWoundType.Bruise => BruiseWounds,
            _ => BurnWounds,
        };

        var size = (int) Math.Min(Math.Max(1, damage / 10), sizes.Length);
        var wound = NewWound(sizes[size - 1], damage);

        // Bad enough hits can also open up a vein.
        var localDamage = limb.Health + damage;
        if (damage > 10 && type != VgWoundType.Burn && localDamage > 20 && Prob(damage) && limb.IsOrganic && ent.Comp.HasBlood)
        {
            AddInternalBleeding(ent, limb);
        }

        // Wounds that look the same are counted as one.
        var desc = StageDesc(wound);
        var merged = false;
        foreach (var other in limb.Wounds)
        {
            if (StageDesc(other) != desc)
                continue;

            other.Damage += wound.Damage;
            other.Amount++;
            merged = true;
            break;
        }

        if (!merged)
            limb.Wounds.Add(wound);

        UpdateDamages(ent, limb);
    }

    private VgWound NewWound(ProtoId<VgWoundPrototype> id, float damage)
    {
        var proto = Prototypes.Index(id);
        var wound = new VgWound
        {
            Prototype = id,
            Stage = proto.StartStage,
            Damage = damage,
            Created = Timing.CurTime,
        };

        // Move the wound to the stage that matches its damage.
        HealWound(wound, 0);
        wound.BleedTimer += damage / BloodlossSpeedMultiplier;
        return wound;
    }

    /// <summary>
    ///     vg13 widenwound(): reopens a healing wound instead of making a new one. Likelier the more wounds you have.
    /// </summary>
    private bool WidenWound(Entity<VgBodyComponent> ent, VgLimb limb, VgWoundType type, float damage)
    {
        if (limb.Wounds.Count == 0 || damage < 5 || type == VgWoundType.Burn)
            return false;

        if (!Prob(50 + ent.Comp.NumberWounds * 10))
            return false;

        var valid = limb.Wounds
            .Where(w => w.Amount == 1 && w.Stage > 1 && Prototypes.Index(w.Prototype).DamageType != VgWoundType.Burn)
            .ToList();

        if (valid.Count == 0)
            return false;

        OpenWound(PredictedRandom.Pick(valid), damage);

        if (Prob(25))
        {
            var limbName = Loc.GetString(limb.Name);
            VisibleMessage(ent,
                Loc.GetString("vg-limb-wound-widens-others", ("target", ent.Owner), ("limb", limbName)),
                Loc.GetString("vg-limb-wound-widens-self", ("limb", limbName)));
        }

        return true;
    }

    /// <summary>
    ///     vg13 /datum/wound/heal_damage(). Returns the healing left over for other wounds.
    /// </summary>
    private float HealWound(VgWound wound, float amount, bool healsInternal = false)
    {
        var proto = Prototypes.Index(wound.Prototype);
        if (proto.Internal && !healsInternal)
            return amount;

        var healed = MathF.Min(wound.Damage, amount);
        amount -= healed;
        wound.Damage -= healed;

        while (wound.Stage < proto.Stages.Count && wound.Damage / wound.Amount < proto.Stages[wound.Stage - 1].Damage)
        {
            wound.Stage++;
        }

        return amount;
    }

    /// <summary>
    ///     vg13 /datum/wound/open_wound(): adds damage and moves the wound back to worse stages.
    /// </summary>
    private void OpenWound(VgWound wound, float damage)
    {
        var proto = Prototypes.Index(wound.Prototype);
        wound.Damage += damage;
        wound.BleedTimer += damage / BloodlossSpeedMultiplier;

        while (wound.Stage > 1 && proto.Stages[wound.Stage - 2].Damage <= wound.Damage)
        {
            wound.Stage--;
        }
    }

    public bool IsBleeding(VgWound wound)
    {
        var proto = Prototypes.Index(wound.Prototype);
        if (proto.Internal || wound.Bandaged || wound.Clamped)
            return false;

        if (wound.Damage <= 30 && wound.BleedTimer <= 0)
            return false;

        var bleedsAtThisSize = proto.DamageType == VgWoundType.Bruise && wound.Damage >= 20 ||
                               proto.DamageType == VgWoundType.Cut && wound.Damage >= 5;

        return bleedsAtThisSize && wound.Stage <= proto.LastBleedingStage;
    }

    /// <summary>
    ///     Whether the wound has had the treatment it needs to heal (bandage for cuts/bruises, salve for burns).
    /// </summary>
    public bool IsTreated(VgWound wound)
    {
        var proto = Prototypes.Index(wound.Prototype);
        if (!proto.NeedsTreatment)
            return true;

        return proto.DamageType == VgWoundType.Burn ? wound.Salved : wound.Bandaged;
    }

    public bool IsInternal(VgWound wound)
    {
        return Prototypes.Index(wound.Prototype).Internal;
    }

    public string StageDesc(VgWound wound)
    {
        var proto = Prototypes.Index(wound.Prototype);
        return proto.Stages[wound.Stage - 1].Desc;
    }

    /// <summary>
    ///     vg13 update_damages(): recomputes limb damage from its wounds and whether it's bleeding.
    /// </summary>
    private void UpdateDamages(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        var hasBlood = ent.Comp.HasBlood;
        var clamped = false;

        limb.NumberWounds = 0;
        limb.BruteDamage = 0;
        limb.BurnDamage = 0;
        limb.Status &= ~VgLimbStatus.Bleeding;

        foreach (var wound in limb.Wounds)
        {
            var proto = Prototypes.Index(wound.Prototype);
            if (!proto.Internal)
            {
                if (proto.DamageType == VgWoundType.Burn)
                    limb.BurnDamage += wound.Damage;
                else
                    limb.BruteDamage += wound.Damage;
            }

            if (limb.IsOrganic && hasBlood && IsBleeding(wound))
            {
                wound.BleedTimer--;
                limb.Status |= VgLimbStatus.Bleeding;
            }

            clamped |= wound.Clamped;
            limb.NumberWounds += wound.Amount;
        }

        // Things tend to bleed if they're cut open.
        if (limb.Open > 0 && !clamped && limb.IsOrganic && hasBlood)
            limb.Status |= VgLimbStatus.Bleeding;

        ent.Comp.NumberWounds = 0;
        foreach (var other in ent.Comp.Limbs.Values)
        {
            ent.Comp.NumberWounds += other.NumberWounds;
        }
    }

    /// <summary>
    ///     vg13 update_wounds(): natural healing and internal bleeding, once per Life() tick.
    /// </summary>
    private void UpdateWounds(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        if (!limb.IsOrganic)
            return;

        var hasBlood = ent.Comp.HasBlood;
        limb.InternallyBleeding = false;

        for (var i = limb.Wounds.Count - 1; i >= 0; i--)
        {
            var wound = limb.Wounds[i];
            var internalWound = IsInternal(wound);

            // Internal wounds bleed into you until they're treated.
            if (internalWound && !IsTreated(wound) && hasBlood)
            {
                limb.InternallyBleeding = true;
                RemoveBlood(ent, wound.Damage * 0.07f * CalcBloodLoss(ent));

                if (Prob(1))
                    CustomPain(ent, Loc.GetString("vg-limb-internal-bleeding-pain", ("limb", Loc.GetString(limb.Name))));
            }

            // Slow natural healing. Wounds this big don't close by themselves.
            var heal = 0f;
            if (wound.Damage < 15)
                heal += 0.2f;
            if (IsTreated(wound) && wound.Damage < 50)
                heal += 0.3f;

            heal *= _organRegenerationMultiplier;
            heal /= limb.Wounds.Count + 1;
            heal = MathF.Round(heal, 1);
            HealWound(wound, heal);

            // Salving also helps against infection.
            if (wound.GermLevel > 0 && wound.Salved && Prob(2))
            {
                wound.GermLevel = 0;
                wound.Disinfected = true;
            }

            // Healed wounds stay as scars for a while, so they show up on examine.
            if (wound.Damage <= 0 && (internalWound || wound.Created + HealedWoundLifetime <= Timing.CurTime))
                limb.Wounds.RemoveAt(i);
        }

        UpdateDamages(ent, limb);
    }
}
