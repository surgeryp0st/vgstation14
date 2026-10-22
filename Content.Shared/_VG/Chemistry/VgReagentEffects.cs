using Content.Shared._VG.Body;
using Content.Shared._VG.Nutrition;
using Content.Shared._VG.Radiation;
using Content.Shared._VG.Status;
using Content.Shared.Damage;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     vg13 REM (REAGENTS_EFFECT_MULTIPLIER), which most reagent effects are written in terms of.
/// </summary>
public static class VgChemistry
{
    public const float Rem = 0.5f;
}

/// <summary>
///     vg13 heal_organ_damage(brute, burn): heals limbs, or the mob's damage totals if it has no vg body.
///     Amounts are multiples of REM.
/// </summary>
public sealed partial class VgHealOrgan : VgReagentEffect
{
    [DataField]
    public float Brute;

    [DataField]
    public float Burn;

    public override void Apply(VgReagentArgs args)
    {
        var brute = Brute * VgChemistry.Rem;
        var burn = Burn * VgChemistry.Rem;

        if (args.EntityManager.TryGetComponent(args.Target, out VgBodyComponent? body))
        {
            args.System<SharedVgBodySystem>().HealOverallDamage((args.Target, body), brute, burn);
            return;
        }

        var spec = new DamageSpecifier();
        spec.DamageDict["Brute"] = -brute;
        spec.DamageDict["Burn"] = -burn;
        args.System<DamageableSystem>().TryChangeDamage(args.Target, spec, true, false);
    }
}

/// <summary>
///     vg13 adjustToxLoss/adjustOxyLoss/adjustCloneLoss. Negative heals; the amount is a multiple of REM.
/// </summary>
public sealed partial class VgAdjustDamage : VgReagentEffect
{
    /// <summary>
    ///     "Toxin", "Oxygen" or "Clone".
    /// </summary>
    [DataField(required: true)]
    public string Type = string.Empty;

    [DataField(required: true)]
    public float Amount;

    /// <summary>
    ///     Not multiplied by REM, for the effects vg13 writes as a flat number.
    /// </summary>
    [DataField]
    public bool Flat;

    public override void Apply(VgReagentArgs args)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict[Type] = Flat ? Amount : Amount * VgChemistry.Rem;
        args.System<DamageableSystem>().TryChangeDamage(args.Target, spec, true, false);
    }
}

/// <summary>
///     vg13 adjustBrainLoss. Negative heals; the amount is a multiple of REM.
/// </summary>
public sealed partial class VgAdjustBrainLoss : VgReagentEffect
{
    [DataField(required: true)]
    public float Amount;

    public override void Apply(VgReagentArgs args)
    {
        args.System<SharedVgBodySystem>().AdjustBrainLoss(args.Target, Amount * VgChemistry.Rem);
    }
}

/// <summary>
///     Inaprovaline's <c>if(M.losebreath >= 10) M.losebreath = max(10, M.losebreath - 5)</c>.
/// </summary>
public sealed partial class VgEaseLoseBreath : VgReagentEffect
{
    [DataField]
    public int Floor = 10;

    [DataField]
    public int Amount = 5;

    public override void Apply(VgReagentArgs args)
    {
        var status = args.System<VgStatusSystem>();
        var current = status.GetLoseBreath(args.Target);
        if (current >= Floor)
            status.AddLoseBreath(args.Target, Math.Max(Floor, current - Amount) - current);
    }
}

/// <summary>
///     vg13 <c>holder.remove_reagents(list, amount)</c>: purges other reagents from the body. Amount is a multiple of REM.
/// </summary>
public sealed partial class VgRemoveReagents : VgReagentEffect
{
    [DataField(required: true)]
    public List<string> Reagents = new();

    [DataField(required: true)]
    public float Amount;

    public override void Apply(VgReagentArgs args)
    {
        foreach (var reagent in Reagents)
        {
            args.RemoveReagent(reagent, Amount * VgChemistry.Rem);
        }
    }
}

/// <summary>
///     Asks for a mob's body temperature, in kelvin. Answered server-side, where temperature lives; reagent effects run
///     there too, but the prototype that names them has to load on the client.
/// </summary>
[ByRefEvent]
public record struct VgGetBodyTemperatureEvent(float? Temperature = null);

/// <summary>
///     Cryoxadone and clonexadone: heal everything, but only when the body is cold enough (below 170 K, twice as well
///     below 95 K). <see cref="Amount"/> is vg13's flat number per damage type.
/// </summary>
public sealed partial class VgCryoHeal : VgReagentEffect
{
    [DataField(required: true)]
    public float Amount;

    public override void Apply(VgReagentArgs args)
    {
        var ev = new VgGetBodyTemperatureEvent();
        args.EntityManager.EventBus.RaiseLocalEvent(args.Target, ref ev);
        if (ev.Temperature is not { } temperature || temperature >= 170)
            return;

        var amount = Amount * (temperature < 95 ? 2 : 1);

        var spec = new DamageSpecifier();
        spec.DamageDict["Clone"] = -amount;
        spec.DamageDict["Oxygen"] = -amount;
        spec.DamageDict["Toxin"] = -amount;
        args.System<DamageableSystem>().TryChangeDamage(args.Target, spec, true, false);

        if (args.EntityManager.TryGetComponent(args.Target, out VgBodyComponent? body))
        {
            args.System<SharedVgBodySystem>().HealOverallDamage((args.Target, body), amount, amount);
            return;
        }

        var wounds = new DamageSpecifier();
        wounds.DamageDict["Brute"] = -amount;
        wounds.DamageDict["Burn"] = -amount;
        args.System<DamageableSystem>().TryChangeDamage(args.Target, wounds, true, false);
    }
}

/// <summary>
///     vg13 <c>pain_numb = max(ticks, pain_numb)</c> (oxycodone).
/// </summary>
public sealed partial class VgNumbPain : VgReagentEffect
{
    [DataField]
    public int Ticks = 5;

    /// <summary>
    ///     vg13 <c>pain_shock_stage -= 3</c>, so shock clears quickly once the oxycodone runs out.
    /// </summary>
    [DataField]
    public int ShockStage = -3;

    public override void Apply(VgReagentArgs args)
    {
        var status = args.System<VgStatusSystem>();
        status.NumbPain(args.Target, Ticks);
        status.AdjustShockStage(args.Target, ShockStage);
    }
}

/// <summary>
///     Tramadol's <c>if(pain_level &lt; BASE_CARBON_PAIN_RESIST) pain_shock_stage--</c>: shock recovers faster once the
///     pain is under control.
/// </summary>
public sealed partial class VgSpeedShockRecovery : VgReagentEffect
{
    public override void Apply(VgReagentArgs args)
    {
        var status = args.System<VgStatusSystem>();
        if (status.GetPainLevel(args.Target) < 80)
            status.AdjustShockStage(args.Target, -1);
    }
}

public enum VgStatusKind
{
    Jitter,
    Druggy,
    Drowsiness,
    Blur,
    Paralyse,
    Stun,
    Knockdown,
    Dizzy,
    Confused,
    Slur,
}

/// <summary>
///     vg13 <c>organ.damage += x</c> on one internal organ, without the messages (sleep toxin's heart).
/// </summary>
public sealed partial class VgDamageOrgan : VgReagentEffect
{
    [DataField(required: true)]
    public string Organ = string.Empty;

    [DataField(required: true)]
    public float Amount;

    public override void Apply(VgReagentArgs args)
    {
        var body = args.System<SharedVgBodySystem>();
        if (args.EntityManager.TryGetComponent<VgBodyComponent>(args.Target, out var comp) && body.TryGetOrgan((args.Target, comp), Organ, out var organ))
            body.TakeOrganDamage((args.Target, comp), organ, Amount, silent: true);
    }
}

/// <summary>
///     vg13 <c>M.Jitter(x)</c>, <c>M.druggy = max(M.druggy, x)</c> and <c>M.drowsyness = max(M.drowsyness, x)</c>:
///     the status lasts at least <see cref="Ticks"/> Life() ticks. With <see cref="Add"/> the ticks are added instead
///     (negative to take them away).
/// </summary>
public sealed partial class VgStatusEffect : VgReagentEffect
{
    [DataField(required: true)]
    public VgStatusKind Kind;

    [DataField(required: true)]
    public float Ticks;

    [DataField]
    public bool Add;

    public override void Apply(VgReagentArgs args)
    {
        var status = args.System<VgStatusSystem>();
        switch (Kind)
        {
            case VgStatusKind.Jitter:
                status.Jitter(args.Target, Ticks);
                break;
            case VgStatusKind.Druggy:
                status.Druggy(args.Target, Ticks);
                break;
            case VgStatusKind.Drowsiness:
                var current = args.EntityManager.GetComponentOrNull<VgStatusComponent>(args.Target)?.Drowsiness ?? 0;
                status.AdjustDrowsiness(args.Target, Add ? (int) Ticks : Math.Max(0, (int) Ticks - current));
                break;
            case VgStatusKind.Blur:
                if (Add)
                    status.AdjustBlur(args.Target, Ticks);
                else
                    status.Blur(args.Target, Ticks);
                break;
            case VgStatusKind.Paralyse:
                status.Paralyse(args.Target, Ticks);
                break;
            case VgStatusKind.Stun:
                status.Stun(args.Target, Ticks);
                break;
            case VgStatusKind.Knockdown:
                status.Knockdown(args.Target, Ticks);
                break;
            case VgStatusKind.Dizzy:
                status.Dizzy(args.Target, Ticks);
                break;
            case VgStatusKind.Confused:
                status.Confuse(args.Target, Ticks);
                break;
            case VgStatusKind.Slur:
                status.Slur(args.Target, Ticks);
                break;
        }
    }
}

/// <summary>
///     vg13 <c>M.radiation = max(M.radiation - x * REM, 0)</c> (hyronalin, arithrazine) and radium's
///     <c>apply_radiation(x * REM, RAD_INTERNAL)</c>. Negative treats radiation; the amount is a multiple of REM.
/// </summary>
public sealed partial class VgAdjustRadiation : VgReagentEffect
{
    [DataField(required: true)]
    public float Amount;

    public override void Apply(VgReagentArgs args)
    {
        args.System<VgRadiationSystem>().Add(args.Target, Amount * VgChemistry.Rem);
    }
}

/// <summary>
///     vg13 <c>M.vomit()</c>.
/// </summary>
public sealed partial class VgVomit : VgReagentEffect
{
    public override void Apply(VgReagentArgs args)
    {
        var ev = new VgVomitEvent();
        args.EntityManager.EventBus.RaiseLocalEvent(args.Target, ref ev);
    }
}

/// <summary>
///     Peridaxon's and imidazoline's <c>organ.damage = max(0, organ.damage - x)</c> over internal organs. Amounts are
///     flat, as vg13 writes them.
/// </summary>
public sealed partial class VgHealInternalOrgans : VgReagentEffect
{
    [DataField(required: true)]
    public float Amount;

    /// <summary>
    ///     Only organs inside this limb (peridaxon: the chest).
    /// </summary>
    [DataField]
    public string? Parent;

    /// <summary>
    ///     Only this organ (imidazoline: the eyes).
    /// </summary>
    [DataField]
    public string? Organ;

    /// <summary>
    ///     vg13 <c>!E.robotic</c>.
    /// </summary>
    [DataField]
    public bool SkipRobotic;

    public override void Apply(VgReagentArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.Target, out VgBodyComponent? body))
            return;

        var system = args.System<SharedVgBodySystem>();
        foreach (var organ in body.Organs.Values)
        {
            if (Parent != null && organ.Parent != Parent || Organ != null && organ.Id != Organ)
                continue;

            if (SkipRobotic && organ.Robotic != 0)
                continue;

            system.HealOrganDamage((args.Target, body), organ, Amount);
        }
    }
}

/// <summary>
///     Activated charcoal's <c>on_mob_life</c>: a 5% chance to vomit instead of anything else, otherwise every other
///     reagent in the body (bar charcoal and blood) loses 15 * REM, or the charcoal itself is used up if there was
///     nothing to remove. Heals 2 * REM toxin damage.
/// </summary>
public sealed partial class VgActivatedCharcoal : VgReagentEffect
{
    [DataField]
    public float VomitChance = 5;

    [DataField]
    public List<string> Keep = new() { "charcoal", "blood" };

    public override void Apply(VgReagentArgs args)
    {
        if (System.Random.Shared.NextDouble() * 100 < VomitChance)
        {
            var ev = new VgVomitEvent();
            args.EntityManager.EventBus.RaiseLocalEvent(args.Target, ref ev);
            return;
        }

        var found = false;
        foreach (var reagent in args.Contents)
        {
            if (Keep.Contains(reagent))
                continue;

            args.RemoveReagent(reagent, 15 * VgChemistry.Rem);
            found = true;
        }

        if (!found)
            args.RemoveReagent(args.Reagent, args.Volume.Float());

        var spec = new DamageSpecifier();
        spec.DamageDict["Toxin"] = -2 * VgChemistry.Rem;
        args.System<DamageableSystem>().TryChangeDamage(args.Target, spec, true, false);
    }
}

/// <summary>
///     Asks the body to move its temperature towards <see cref="Target"/> by up to <see cref="Rate"/> kelvin.
///     Answered server-side, where temperature lives.
/// </summary>
[ByRefEvent]
public record struct VgMoveBodyTemperatureEvent(float Target, float Rate);

/// <summary>
///     Leporazine's pull towards 310 K, in vg13 as <c>40 * TEMPERATURE_DAMAGE_COEFFICIENT</c> per Life() tick.
/// </summary>
public sealed partial class VgMoveBodyTemperature : VgReagentEffect
{
    [DataField]
    public float Target = 310;

    [DataField]
    public float Rate = 60;

    public override void Apply(VgReagentArgs args)
    {
        var ev = new VgMoveBodyTemperatureEvent(Target, Rate);
        args.EntityManager.EventBus.RaiseLocalEvent(args.Target, ref ev);
    }
}

/// <summary>
///     Lipozine's <c>M.overeatduration = 0</c>.
/// </summary>
public sealed partial class VgResetOvereat : VgReagentEffect
{
    public override void Apply(VgReagentArgs args)
    {
        args.System<VgNutritionSystem>().ResetOvereat(args.Target);
    }
}
