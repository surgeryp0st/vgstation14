using Content.Shared._VG.Body;
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
