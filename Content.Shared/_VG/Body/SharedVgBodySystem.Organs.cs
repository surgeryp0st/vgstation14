using Content.Shared._VG.Damage;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    private static readonly ProtoId<DamageTypePrototype> ToxinDamage = "Poison";

    // vg13 process_accuracy for livers, and the kidney's every-10-ticks filtering.
    private const int LiverAccuracy = 10;
    private const int KidneyInterval = 10;

    public bool TryGetOrgan(Entity<VgBodyComponent?> ent, string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VgOrgan? organ)
    {
        organ = null;
        return Resolve(ent, ref ent.Comp, false) && ent.Comp.Organs.TryGetValue(id, out organ);
    }

    /// <summary>
    ///     vg13 organ take_damage(): fully robotic organs shrug off a fifth of it.
    /// </summary>
    public void TakeOrganDamage(Entity<VgBodyComponent> ent, VgOrgan organ, float amount, bool silent = false)
    {
        organ.Damage += organ.Robotic == 2 ? amount * 0.8f : amount;
        Dirty(ent);

        if (!silent && ent.Comp.Limbs.TryGetValue(organ.Parent, out var parent))
            CustomPain(ent, Loc.GetString("vg-organ-hurts", ("limb", Loc.GetString(parent.Name))));
    }

    /// <summary>
    ///     vg13 take_damage() on a limb: a heavy or sharp hit can reach an organ inside, which takes half of it.
    ///     Returns the brute left for the limb.
    /// </summary>
    private float DamageOrganInside(Entity<VgBodyComponent> ent, VgLimb limb, float brute, float sharp)
    {
        if (!(sharp > 0 && brute >= 5 || brute >= 10) || !Prob(5))
            return brute;

        var inside = new List<VgOrgan>();
        foreach (var organ in ent.Comp.Organs.Values)
        {
            if (organ.Parent == limb.Id)
                inside.Add(organ);
        }

        if (inside.Count == 0)
            return brute;

        TakeOrganDamage(ent, PredictedRandom.Pick(inside), brute / 2);
        return brute - brute / 2;
    }

    /// <summary>
    ///     vg13 organ process() for every internal organ, and brain damage, once per Life() tick.
    /// </summary>
    private void ProcessOrgans(Entity<VgBodyComponent> ent, int lifeTick)
    {
        var toxin = GetDamage(ent, VgDamageTranslation.Toxin);

        foreach (var organ in ent.Comp.Organs.Values)
        {
            switch (organ.Id)
            {
                case VgOrgans.Kidneys:
                    // Healthy kidneys slowly filter out light toxin damage.
                    if (lifeTick % KidneyInterval == 0 && toxin <= 15 && !organ.IsBroken)
                        _damageable.TryChangeDamage(ent, new DamageSpecifier(Prototypes.Index(ToxinDamage), -0.5f), true);
                    break;

                case VgOrgans.Liver:
                    // TODO VG: anti-toxins protect the liver, and a bruised one can't take alcohol or toxins, once
                    // vg13 chemistry is in.
                    if (lifeTick % LiverAccuracy == 0 && toxin >= 60)
                    {
                        if (!organ.IsBroken)
                            organ.Damage += 0.2f * LiverAccuracy;
                        else
                            PredictedRandom.Pick(ent.Comp.Organs.Values).Damage += 0.2f * LiverAccuracy;
                    }

                    break;

                case VgOrgans.Lungs:
                    ProcessLungs(ent, organ);
                    break;

                case VgOrgans.Eyes:
                    // Broken eyes can't see.
                    if (organ.IsBroken)
                        _status.Blind(ent, 12);
                    break;
            }
        }

        ProcessBrainLoss(ent);
    }

    /// <summary>
    ///     vg13 lungs process(): bruised lungs leave you gasping or coughing up blood.
    /// </summary>
    private void ProcessLungs(Entity<VgBodyComponent> ent, VgOrgan lungs)
    {
        if (!lungs.IsBruised)
            return;

        var chance = MathF.Min(50, (lungs.Damage - lungs.MinBruisedDamage) / lungs.MinBrokenDamage * 50);
        var name = IdentityOf(ent);
        if (Prob(chance))
        {
            VisibleMessage(ent, Loc.GetString("vg-organ-lungs-gasp", ("target", name)), Loc.GetString("vg-organ-lungs-gasp", ("target", name)), PopupType.Small);
            if (_status.GetLoseBreath(ent) <= 30)
                _status.AddLoseBreath(ent, 5);
        }
        else if (Prob(chance) && ent.Comp.HasBlood)
        {
            RemoveBlood(ent, 10);
            VisibleMessage(ent, Loc.GetString("vg-organ-lungs-cough", ("target", name)), Loc.GetString("vg-organ-lungs-cough", ("target", name)), PopupType.Small);
        }
    }

    /// <summary>
    ///     vg13 handle_disabilities() brain damage effects.
    /// </summary>
    /// <remarks>TODO VG: gibberish and drooling at 60+, once vg13 speech is in.</remarks>
    private void ProcessBrainLoss(Entity<VgBodyComponent> ent)
    {
        var brain = ent.Comp.BrainLoss;
        if (brain <= 0)
            return;

        if (brain > 50 && Prob(1.5f))
        {
            CustomPain(ent, Loc.GetString("vg-brain-legfail"));
            _status.Knockdown(ent, 3);
        }
        else if (brain > 35 && Prob(1.5f))
        {
            if (_hands.GetActiveItem(ent.Owner) != null)
            {
                CustomPain(ent, Loc.GetString("vg-brain-handfail"));
                _hands.TryDrop(ent.Owner, checkActionBlocker: false);
            }
        }
        else if (brain > 15 && Prob(1.5f))
        {
            // TODO VG: eye_blurry 10, once vg13 eyes are in.
            CustomPain(ent, Loc.GetString("vg-brain-eyeblur"));
        }
        else if (Prob(2))
        {
            CustomPain(ent, Loc.GetString("vg-brain-numb"));
        }
    }

    /// <summary>
    ///     vg13 adjustBrainLoss(), capped at twice max health.
    /// </summary>
    public void AdjustBrainLoss(Entity<VgBodyComponent?> ent, float amount)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.BrainLoss = Math.Clamp(ent.Comp.BrainLoss + amount, 0, 200);
        Dirty(ent);
    }

    private float GetDamage(EntityUid uid, string type)
    {
        return TryComp<DamageableComponent>(uid, out var damageable) &&
               damageable.Damage.DamageDict.TryGetValue(type, out var value)
            ? value.Float()
            : 0;
    }

    private EntityUid IdentityOf(EntityUid uid)
    {
        return Content.Shared.IdentityManagement.Identity.Entity(uid, EntityManager);
    }
}
