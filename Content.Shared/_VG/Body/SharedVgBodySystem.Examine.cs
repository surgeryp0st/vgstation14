using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Localizations;

namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    /// <summary>
    ///     Where a limb's injuries hide, and what shows instead if it bleeds under there.
    /// </summary>
    private enum ExamineCover : byte
    {
        Mask,
        Uniform,
        Gloves,
        Shoes,
    }

    /// <summary>
    ///     vg13's examine order, and which clothing hides each limb.
    /// </summary>
    private static readonly (string Limb, ExamineCover Cover)[] ExamineLimbs =
    {
        (VgLimbs.Head, ExamineCover.Mask),
        (VgLimbs.Chest, ExamineCover.Uniform),
        (VgLimbs.LeftArm, ExamineCover.Uniform),
        (VgLimbs.LeftHand, ExamineCover.Gloves),
        (VgLimbs.RightArm, ExamineCover.Uniform),
        (VgLimbs.RightHand, ExamineCover.Gloves),
        (VgLimbs.Groin, ExamineCover.Uniform),
        (VgLimbs.LeftLeg, ExamineCover.Uniform),
        (VgLimbs.LeftFoot, ExamineCover.Shoes),
        (VgLimbs.RightLeg, ExamineCover.Uniform),
        (VgLimbs.RightFoot, ExamineCover.Shoes),
    };

    /// <summary>
    ///     vg13 surgery stage where the limb is wide open.
    /// </summary>
    private const int OpenWide = 3;

    // vg13 wound germ_level thresholds for looking infected.
    private const float BadlyInfectedGerms = 600;
    private const float LightlyInfectedGerms = 330;

    // Wounds too obvious to be "what might be" one (vg13 no_exclude).
    private static readonly HashSet<string> UnmistakableWounds = new()
    {
        "vg-wound-cut-gaping-1", "vg-wound-cut-gaping-big-1", "vg-wound-cut-massive-1",
        "vg-wound-bruise-1", "vg-wound-bruise-2", "vg-wound-bruise-3",
        "vg-wound-burn-severe-2", "vg-wound-burn-large-2", "vg-wound-burn-deep-2", "vg-wound-burn-carbonised-1",
    };

    private void InitializeExamine()
    {
        SubscribeLocalEvent<VgBodyComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    ///     vg13 human examine(): splints, then each limb's wounds, missing limbs and prosthetics, unless clothing
    ///     covers them, in which case only blood seeping out gives it away. Runs on the client too, so it's predicted.
    /// </summary>
    private void OnExamined(Entity<VgBodyComponent> ent, ref ExaminedEvent args)
    {
        var target = Identity.Entity(ent, EntityManager);

        // Seeded by who looks at whom rather than the tick, so the client's text matches the server's.
        var random = new System.Random(unchecked(GetNetEntity(ent).Id * 397 ^ GetNetEntity(args.Examiner).Id));

        using (args.PushGroup(nameof(SharedVgBodySystem)))
        {
            foreach (var id in new[] { VgLimbs.LeftLeg, VgLimbs.RightLeg, VgLimbs.LeftArm, VgLimbs.RightArm })
            {
                if (ent.Comp.Limbs.TryGetValue(id, out var limb) && (limb.Status & VgLimbStatus.Splinted) != 0)
                    args.PushMarkup(Loc.GetString("vg-examine-splint", ("target", target), ("limb", Loc.GetString(limb.Name))));
            }

            var bloodFace = false;
            var bloodClothing = false;
            var bloodGloves = false;
            var bloodShoes = false;

            foreach (var (id, cover) in ExamineLimbs)
            {
                if (!ent.Comp.Limbs.TryGetValue(id, out var limb))
                    continue;

                var text = GetLimbExamineText(limb, target, random);
                var covered = !limb.IsExisting ? false : cover switch
                {
                    ExamineCover.Mask => IsFaceCovered(ent),
                    ExamineCover.Uniform => _inventory.TryGetSlotEntity(ent, "jumpsuit", out _),
                    ExamineCover.Gloves => _inventory.TryGetSlotEntity(ent, "gloves", out _),
                    _ => _inventory.TryGetSlotEntity(ent, "shoes", out _),
                };

                if (text != null && !covered)
                {
                    args.PushMarkup(text);
                    continue;
                }

                if ((limb.Status & VgLimbStatus.Bleeding) == 0 || !limb.IsExisting || !limb.IsOrganic)
                    continue;

                switch (cover)
                {
                    case ExamineCover.Mask:
                        bloodFace = true;
                        break;
                    case ExamineCover.Uniform:
                        bloodClothing = true;
                        break;
                    case ExamineCover.Gloves:
                        bloodGloves = true;
                        break;
                    default:
                        bloodShoes = true;
                        break;
                }
            }

            if (bloodFace)
                args.PushMarkup(Loc.GetString("vg-examine-blood-face", ("target", target)));
            if (bloodClothing)
                args.PushMarkup(Loc.GetString("vg-examine-blood-clothing", ("target", target)));
            if (bloodShoes)
                args.PushMarkup(Loc.GetString("vg-examine-blood-shoes", ("target", target)));
            if (bloodGloves)
                args.PushMarkup(Loc.GetString("vg-examine-blood-gloves", ("target", target)));
        }
    }

    /// <summary>
    ///     What vg13 says about one limb, or null if there's nothing to see.
    /// </summary>
    private string? GetLimbExamineText(VgLimb limb, EntityUid target, System.Random random)
    {
        var limbName = Loc.GetString(limb.Name);

        if (!limb.IsExisting)
            return Loc.GetString("vg-examine-limb-missing", ("target", target), ("limb", limbName));

        string? text;
        if (limb.IsPeg)
            text = GetProstheticText("peg", limb, target, limbName, random);
        else if (limb.IsRobotic)
            text = GetProstheticText("robot", limb, target, limbName, random);
        else
            text = GetWoundsText(limb, target, limbName, random);

        if (limb.Open >= OpenWide)
        {
            var open = Loc.GetString("vg-examine-limb-open", ("target", target), ("limb", limbName));
            text = text == null ? open : $"{text}\n{open}";
        }

        return text;
    }

    /// <summary>
    ///     "They have a peg left leg, it has some marks and a lot of burns!"
    /// </summary>
    private string GetProstheticText(string kind, VgLimb limb, EntityUid target, string limbName, System.Random random)
    {
        if (limb.BruteDamage <= 0 && limb.BurnDamage <= 0)
            return Loc.GetString($"vg-examine-limb-{kind}", ("target", target), ("limb", limbName));

        var damage = new List<string>();
        if (limb.BruteDamage > 0)
            damage.Add(Loc.GetString(limb.BruteDamage <= 20 ? $"vg-examine-{kind}-brute-light" : $"vg-examine-{kind}-brute-heavy-{random.Next(1, 3)}"));
        if (limb.BurnDamage > 0)
            damage.Add(Loc.GetString(limb.BurnDamage <= 20 ? $"vg-examine-{kind}-burn-light" : $"vg-examine-{kind}-burn-heavy-{random.Next(1, 3)}"));

        return Loc.GetString($"vg-examine-limb-{kind}-damaged",
            ("target", target),
            ("limb", limbName),
            ("damage", string.Join(Loc.GetString("vg-examine-and"), damage)));
    }

    /// <summary>
    ///     "They have a bleeding deep cut, and several small bruises on their left arm."
    /// </summary>
    private string? GetWoundsText(VgLimb limb, EntityUid target, string limbName, System.Random random)
    {
        // Wounds that look alike are counted together, in the order they were found.
        var descriptors = new List<(string Key, string Desc, int Amount)>();
        foreach (var wound in limb.Wounds)
        {
            if (IsInternal(wound) && limb.Open == 0)
                continue; // You can't see a cut vein.

            var key = StageDesc(wound);
            var desc = Loc.GetString(key);
            if (IsBleeding(wound))
                desc = Loc.GetString("vg-examine-wound-bleeding", ("wound", desc));
            else if (wound.Bandaged)
                desc = Loc.GetString("vg-examine-wound-bandaged", ("wound", desc));

            if (wound.GermLevel > BadlyInfectedGerms)
                desc = Loc.GetString("vg-examine-wound-badly-infected", ("wound", desc));
            else if (wound.GermLevel > LightlyInfectedGerms)
                desc = Loc.GetString("vg-examine-wound-lightly-infected", ("wound", desc));

            var index = descriptors.FindIndex(d => d.Desc == desc);
            if (index >= 0)
                descriptors[index] = descriptors[index] with { Amount = descriptors[index].Amount + wound.Amount };
            else
                descriptors.Add((key, desc, wound.Amount));
        }

        if (descriptors.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var (key, desc, amount) in descriptors)
        {
            // Now and then, small wounds are hard to make out.
            var maybe = random.Next(100) < 10 && !UnmistakableWounds.Contains(key);
            var vowel = desc.Length > 0 && "aeiouAEIOU".Contains(desc[0]);
            var fragment = amount switch
            {
                1 => "vg-examine-wound-one",
                2 => "vg-examine-wound-pair",
                <= 5 => "vg-examine-wound-several",
                // vg13 says "a bunch" for the first wound kind, "a ton" for the rest.
                _ => parts.Count == 0 ? "vg-examine-wound-bunch" : "vg-examine-wound-ton",
            };

            parts.Add(Loc.GetString(fragment, ("wound", desc), ("vowel", vowel), ("maybe", maybe)));
        }

        return Loc.GetString("vg-examine-limb-wounds",
            ("target", target),
            ("limb", limbName),
            ("wounds", ContentLocalizationManager.FormatList(parts)));
    }

    /// <summary>
    ///     vg13 only hides head wounds behind a gas mask, not any mask.
    /// </summary>
    private bool IsFaceCovered(EntityUid uid)
    {
        return _inventory.TryGetSlotEntity(uid, "mask", out var mask) &&
               TryComp<IdentityBlockerComponent>(mask, out var blocker) &&
               blocker.Enabled;
    }
}
