using System.Linq;
using Robust.Shared.Random;

namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    /// <summary>
    ///     vg13 apply_damage() for brute/burn: damages the limb at a zone, spilling onto neighbouring limbs.
    /// </summary>
    public void ApplyDamage(Entity<VgBodyComponent> ent, string zone, float brute, float burn, float sharp = 0, bool edge = false)
    {
        if (!TryGetLimbForZone((ent, ent.Comp), zone, out var limb))
            return;

        TakeLimbDamage(ent, limb, brute, burn, sharp, edge);
    }

    /// <summary>
    ///     vg13 take_overall_damage(): damages random limbs one after another until the damage is used up.
    /// </summary>
    public void TakeOverallDamage(Entity<VgBodyComponent> ent, float brute, float burn, float sharp = 0, bool edge = false)
    {
        var parts = ent.Comp.Limbs.Values
            .Where(l => l.IsExisting && l.Health < l.MaxDamage)
            .ToList();

        while (parts.Count > 0 && (brute > 0 || burn > 0))
        {
            var picked = PredictedRandom.PickAndTake(parts);
            var bruteWas = picked.BruteDamage;
            var burnWas = picked.BurnDamage;
            var damageCap = picked.MaxDamage - picked.Health;

            TakeLimbDamage(ent, picked, brute, burn, sharp, edge, spread: false);

            if (!picked.IsExisting)
            {
                // The limb came off; vg13 rules in the victim's favour and assumes it soaked up as much as it could.
                brute -= damageCap;
                burn -= damageCap;
            }
            else if (picked.MaxDamage - picked.Health > 0)
            {
                // Damage doesn't spread here, so if the limb isn't maxed out it took everything.
                break;
            }
            else
            {
                brute -= picked.BruteDamage - bruteWas;
                burn -= picked.BurnDamage - burnWas;
            }

            brute = MathF.Max(0, brute);
            burn = MathF.Max(0, burn);
        }
    }

    /// <summary>
    ///     vg13 heal_overall_damage(): heals random damaged organic limbs until the healing is used up.
    /// </summary>
    public void HealOverallDamage(Entity<VgBodyComponent> ent, float brute, float burn)
    {
        var parts = ent.Comp.Limbs.Values
            .Where(l => l.IsOrganic && (brute > 0 && l.BruteDamage > 0 || burn > 0 && l.BurnDamage > 0))
            .ToList();

        while (parts.Count > 0 && (brute > 0 || burn > 0))
        {
            var picked = PredictedRandom.PickAndTake(parts);
            var bruteWas = picked.BruteDamage;
            var burnWas = picked.BurnDamage;

            HealLimbDamage(ent, picked, brute, burn);

            brute -= bruteWas - picked.BruteDamage;
            burn -= burnWas - picked.BurnDamage;
        }
    }

    /// <summary>
    ///     vg13 /datum/organ/external/take_damage().
    /// </summary>
    /// <param name="forbidden">Limbs the damage already spilled through, so it doesn't bounce back.</param>
    /// <param name="spread">Whether damage the limb can't take spills onto its parent or children.</param>
    private void TakeLimbDamage(
        Entity<VgBodyComponent> ent,
        VgLimb limb,
        float brute,
        float burn,
        float sharp,
        bool edge,
        HashSet<string>? forbidden = null,
        bool spread = true,
        bool noDamageModifier = false)
    {
        brute = MathF.Max(0, brute);
        burn = MathF.Max(0, burn);
        if (brute <= 0 && burn <= 0 || !limb.IsExisting)
            return;

        // Only weaknesses carry over when damage spills to another limb, so they don't cascade.
        var originalBrute = brute;
        var originalBurn = burn;
        if (!noDamageModifier)
        {
            if (!limb.IsOrganic)
            {
                brute *= 0.66f;
                burn *= limb.IsPeg ? 2f : 0.66f;
            }
            else
            {
                brute *= ent.Comp.BruteModifier;
                burn *= ent.Comp.BurnModifier;
            }
        }

        var bonusBrute = MathF.Max(0, brute - originalBrute);
        var bonusBurn = MathF.Max(0, burn - originalBurn);

        // Enough damage on a maxed out limb cuts or tears it off. The torso never comes off.
        if (!limb.IsTorso)
        {
            var maxDamage = limb.MaxDamage * _organHealthMultiplier;
            if (_limbsCanBreak && limb.Health >= maxDamage)
            {
                if (sharp > 0 || limb.IsPeg)
                {
                    // Sharper things sever more easily.
                    if (Prob(5 * brute * (sharp > 0 ? sharp : 1)))
                    {
                        DropLimb(ent, limb, true);
                        return;
                    }
                }
                // 15 damage: 8% chance, 22: 27%, 30: 64%, anything above ~35 always blows the limb apart.
                else if (brute > 15 && Prob(MathF.Pow(brute / 7.5f, 3)))
                {
                    ExplodeLimb(ent, limb);
                    return;
                }
                else if (brute > 20 && Prob(2 * brute))
                {
                    DropLimb(ent, limb, true);
                    return;
                }
            }
            // Exceptionally sharp things can sever a limb before it's maxed out, the sharper the earlier.
            else if (_limbsCanBreak && (sharp >= 100 || sharp >= 2 && limb.Health >= maxDamage / sharp))
            {
                if (Prob(5 * brute * sharp * (sharp - 1)))
                {
                    DropLimb(ent, limb, true);
                    return;
                }
            }
        }

        // Getting hit on a broken, unsplinted limb hurts.
        if (limb.IsBroken && brute > 0 && Prob(40))
            Scream(ent);

        var canCut = (Prob(brute * 2) || sharp > 0) && limb.IsOrganic;
        var cutOrBruise = canCut ? VgWoundType.Cut : VgWoundType.Bruise;

        if (limb.Health + brute + burn < limb.MaxDamage * _organHealthMultiplier || !_limbsCanBreak)
        {
            if (brute > 0)
                CreateWound(ent, limb, cutOrBruise, brute);
            if (burn > 0)
                CreateWound(ent, limb, VgWoundType.Burn, burn);
        }
        else
        {
            // The limb can't take all of it: inflict what it can, then pass the rest on.
            var canInflict = limb.MaxDamage * _organHealthMultiplier - limb.Health;
            if (canInflict > 0)
            {
                if (brute > 0)
                {
                    CreateWound(ent, limb, cutOrBruise, MathF.Min(brute, canInflict));
                    var inflictable = canInflict;
                    canInflict = MathF.Max(0, canInflict - brute);
                    brute = MathF.Max(0, brute - inflictable);
                }

                if (burn > 0 && canInflict > 0)
                {
                    CreateWound(ent, limb, VgWoundType.Burn, MathF.Min(burn, canInflict));
                    burn = MathF.Max(0, burn - canInflict);
                }
            }

            brute -= bonusBrute;
            burn -= bonusBurn;

            if (burn > 0 || brute > 0)
            {
                if (!limb.IsOrganic)
                {
                    // Prosthetics just fall off.
                    DropLimb(ent, limb, true);
                }
                else if (spread)
                {
                    forbidden ??= new HashSet<string>();
                    var candidates = new List<VgLimb>();
                    if (GetParent(ent.Comp, limb) is { } parent && !forbidden.Contains(parent.Id))
                        candidates.Add(parent);

                    foreach (var childId in limb.Children)
                    {
                        if (!forbidden.Contains(childId) && ent.Comp.Limbs.TryGetValue(childId, out var child))
                            candidates.Add(child);
                    }

                    if (candidates.Count > 0)
                    {
                        forbidden.Add(limb.Id);
                        TakeLimbDamage(ent, PredictedRandom.Pick(candidates), brute, burn, sharp, edge, forbidden, noDamageModifier: noDamageModifier);
                    }
                }
            }
        }

        UpdateDamages(ent, limb);
    }

    /// <summary>
    ///     vg13 /datum/organ/external/heal_damage(). Robotic limbs need <paramref name="roboRepair"/>; pegs can't be fixed.
    /// </summary>
    public void HealLimbDamage(Entity<VgBodyComponent> ent, VgLimb limb, float brute, float burn, bool internalDamage = false, bool roboRepair = false)
    {
        if (limb.IsRobotic && !roboRepair || limb.IsPeg)
            return;

        foreach (var wound in limb.Wounds)
        {
            if (brute <= 0 && burn <= 0)
                break;

            var proto = Prototypes.Index(wound.Prototype);
            if (proto.DamageType == VgWoundType.Burn)
                burn = HealWound(wound, burn);
            else
                brute = HealWound(wound, brute);
        }

        if (internalDamage)
        {
            limb.Status &= ~VgLimbStatus.Broken;
            limb.PermaInjury = 0;
        }

        UpdateDamages(ent, limb);
    }
}
