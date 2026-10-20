using Content.Shared._VG.Life;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio;

namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    private static readonly SoundSpecifier FractureSound = new SoundCollectionSpecifier("VgFracture");
    private static readonly SoundSpecifier GibSound = new SoundPathSpecifier("/Audio/Effects/gib3.ogg");

    private void OnLifeTick(Entity<VgBodyComponent> ent, ref VgLifeTickEvent args)
    {
        // A mob's own client predicts its Life() tick.
        BeginPrediction(ent, ent, 4);

        foreach (var limb in ent.Comp.Limbs.Values)
        {
            if (limb.Wounds.Count == 0 && limb.Health <= 0 && limb.Status == VgLimbStatus.None)
                continue;

            ProcessLimb(ent, limb);
        }

        HandleBleeding(ent);
        HandleBlood(ent);
        ProcessOrgans(ent, args.LifeTick);

        Dirty(ent);
        WriteBackTotals(ent);
    }

    /// <summary>
    ///     vg13 /datum/organ/external/process().
    /// </summary>
    private void ProcessLimb(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        UpdateWounds(ent, limb);

        if ((limb.Status & VgLimbStatus.Destroyed) != 0)
        {
            if (!limb.DroppedSpawned && _limbsCanBreak)
                DropLimb(ent, limb);
            return;
        }

        if (GetParent(ent.Comp, limb) is { } parent && (parent.Status & VgLimbStatus.Destroyed) != 0)
        {
            limb.Status |= VgLimbStatus.Destroyed;
            return;
        }

        if (_bonesCanBreak && limb.IsOrganic && limb.BruteDamage > limb.MinBrokenDamage * _organHealthMultiplier)
            Fracture(ent, limb);

        if (!limb.IsBroken)
            limb.PermaInjury = 0;

        ProcessGrasp(ent, limb);
    }

    /// <summary>
    ///     vg13 fracture().
    /// </summary>
    public void Fracture(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        if ((limb.Status & VgLimbStatus.Broken) != 0)
            return;

        VisibleMessage(ent,
            Loc.GetString("vg-limb-fracture-others", ("target", ent.Owner)),
            Loc.GetString("vg-limb-fracture-self", ("limb", Loc.GetString(limb.Name))),
            PopupType.LargeCaution);

        Scream(ent);
        _audio.PlayPredicted(FractureSound, ent, PredictingUser);

        limb.Status |= VgLimbStatus.Broken;
        limb.PermaInjury = limb.BruteDamage;
        Dirty(ent);
    }

    /// <summary>
    ///     vg13 process_grasp(): you can't keep holding things with a broken arm or hand.
    /// </summary>
    private void ProcessGrasp(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        if (limb.Grasp is not { } location || !limb.IsOrganic || !limb.IsBroken)
            return;

        if (!TryGetHand(ent, location, out var hand) || hand.IsEmpty)
            return;

        if (!_hands.TryDrop(ent, hand, checkActionBlocker: false))
            return;

        var message = Loc.GetString("vg-limb-drop-broken", ("target", ent.Owner), ("limb", Loc.GetString(limb.Name)));
        VisibleMessage(ent, message, message);
    }

    /// <summary>
    ///     vg13 droplimb(): severs the limb (and everything attached to it).
    /// </summary>
    public void DropLimb(Entity<VgBodyComponent> ent, VgLimb limb, bool force = false, bool spawnLimb = true, bool message = true)
    {
        if (limb.DroppedSpawned || limb.IsTorso)
            return;

        if (force)
            limb.Status |= VgLimbStatus.Destroyed;

        if ((limb.Status & VgLimbStatus.Destroyed) == 0)
            return;

        limb.DroppedSpawned = true;
        SetAmputatedTree(ent.Comp, limb);

        // No predicted spawning in this engine version; the client gets the limb item from the server.
        if (spawnLimb && limb.DroppedPrototype is { } proto && Net.IsServer)
            Spawn(proto, Transform(ent).Coordinates);

        // Everything attached comes off with it; the severed limb item stands in for the whole lot.
        foreach (var childId in limb.Children)
        {
            if (ent.Comp.Limbs.TryGetValue(childId, out var child))
                DropLimb(ent, child, true, spawnLimb: false, message: false);
        }

        limb.Status &= ~(VgLimbStatus.Broken | VgLimbStatus.Bleeding | VgLimbStatus.Splinted |
                         VgLimbStatus.Dead | VgLimbStatus.Malfunctioning);
        limb.BruteDamage = 0;
        limb.BurnDamage = 0;
        limb.PermaInjury = 0;
        limb.Wounds.Clear();
        limb.NumberWounds = 0;

        if (limb.Grasp is { } location && TryGetHand(ent, location, out var hand))
            _hands.TryDrop(ent, hand, checkActionBlocker: false);

        if ((limb.BodyPart & VgBodyParts.Legs) != 0)
        {
            VisibleMessage(ent, null, Loc.GetString("vg-limb-lost-leg"), PopupType.LargeCaution);
            _status.Knockdown(ent, 5);
        }

        RemoveUpstreamPart(ent, limb);

        if (message && spawnLimb)
        {
            var limbName = Loc.GetString(limb.Name);
            VisibleMessage(ent,
                Loc.GetString("vg-limb-severed-others", ("target", ent.Owner), ("limb", limbName)),
                Loc.GetString("vg-limb-severed-self", ("limb", limbName)),
                PopupType.LargeCaution);
        }

        Dirty(ent);

        if (limb.Vital)
            Kill(ent);
    }

    /// <summary>
    ///     vg13 explode(): the limb is blown to bits, leaving nothing to reattach.
    /// </summary>
    public void ExplodeLimb(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        if (limb.IsPeg)
        {
            DropLimb(ent, limb, true);
            return;
        }

        var limbName = Loc.GetString(limb.Name);
        var variant = PredictedRandom.Next(1, 4);
        var key = limb.IsRobotic && variant > 1 ? $"vg-limb-explode-robotic-{variant}" : $"vg-limb-explode-{variant}";
        var message = Loc.GetString(key, ("target", ent.Owner), ("limb", limbName));
        VisibleMessage(ent, message, message, PopupType.LargeCaution);

        if (!limb.IsRobotic)
            _audio.PlayPredicted(GibSound, ent, PredictingUser);

        DropLimb(ent, limb, true, spawnLimb: false, message: false);
    }

    private void SetAmputatedTree(VgBodyComponent body, VgLimb limb)
    {
        foreach (var childId in limb.Children)
        {
            if (!body.Limbs.TryGetValue(childId, out var child))
                continue;

            child.Amputated = true;
            SetAmputatedTree(body, child);
        }
    }

    /// <summary>
    ///     vg13 handle_blood() bleeding: open wounds, stumps and incisions drip blood every Life() tick.
    /// </summary>
    private void HandleBleeding(Entity<VgBodyComponent> ent)
    {
        if (!ent.Comp.HasBlood)
            return;

        var bloodMax = 0f;
        foreach (var limb in ent.Comp.Limbs.Values)
        {
            if ((limb.Status & VgLimbStatus.Bleeding) == 0 || !limb.IsOrganic)
                continue;

            foreach (var wound in limb.Wounds)
            {
                if (IsBleeding(wound))
                    bloodMax += wound.Damage / 4;
            }

            // Missing a limb entirely.
            if ((limb.Status & VgLimbStatus.Destroyed) != 0 && (limb.Status & VgLimbStatus.Gauzed) == 0 && !limb.Amputated)
                bloodMax += 20;

            if (limb.Open > 0)
                bloodMax += 2;
        }

        bloodMax *= BloodlossSpeedMultiplier;
        if (bloodMax > 0)
            RemoveBlood(ent, bloodMax * CalcBloodLoss(ent));
    }

    /// <summary>
    ///     vg13 calcbloodloss(): how fast you're losing blood right now, 1 being normal.
    /// </summary>
    private float CalcBloodLoss(EntityUid uid)
    {
        var factor = 1f;

        // Lying down slows blood loss.
        if (_standing.IsDown(uid))
            factor -= 0.3f;

        return MathF.Max(0, factor);
    }


    private bool TryGetHand(EntityUid uid, HandLocation location, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Hand? hand)
    {
        hand = null;
        if (!TryComp<HandsComponent>(uid, out var hands))
            return false;

        foreach (var candidate in hands.Hands.Values)
        {
            if (candidate.Location != location)
                continue;

            hand = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     While the SS14 body system still provides hands and legs, remove the matching SS14 part when a vg limb
    ///     comes off so the mob actually loses the hand or leg.
    /// </summary>
    private void RemoveUpstreamPart(EntityUid uid, VgLimb limb)
    {
        BodyPartType type;
        if ((limb.BodyPart & VgBodyParts.Arms) != 0)
            type = BodyPartType.Arm;
        else if ((limb.BodyPart & VgBodyParts.Hands) != 0)
            type = BodyPartType.Hand;
        else if ((limb.BodyPart & VgBodyParts.Legs) != 0)
            type = BodyPartType.Leg;
        else if ((limb.BodyPart & VgBodyParts.Feet) != 0)
            type = BodyPartType.Foot;
        else
            return; // The head holds the SS14 brain and mind; vital limb loss already kills.

        var left = (limb.BodyPart & (VgBodyParts.ArmLeft | VgBodyParts.HandLeft | VgBodyParts.LegLeft | VgBodyParts.FootLeft)) != 0;
        var symmetry = left ? BodyPartSymmetry.Left : BodyPartSymmetry.Right;

        foreach (var (partId, part) in _body.GetBodyChildrenOfType(uid, type))
        {
            if (part.Symmetry == symmetry && Net.IsServer)
                QueueDel(partId);
        }
    }
}
