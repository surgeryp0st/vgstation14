namespace Content.Shared._VG.Body;

public abstract partial class SharedVgBodySystem
{
    /// <summary>
    ///     vg13 bandage(): covers every external wound on the limb and stops it bleeding. False if they all already were.
    /// </summary>
    public bool Bandage(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        var changed = false;
        limb.Status &= ~VgLimbStatus.Bleeding;
        foreach (var wound in limb.Wounds)
        {
            if (IsInternal(wound))
                continue;

            changed |= !wound.Bandaged;
            wound.Bandaged = true;
        }

        Dirty(ent);
        return changed;
    }

    /// <summary>
    ///     vg13 salve(): puts ointment on every wound on the limb. False if they all already had some.
    /// </summary>
    public bool Salve(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        var changed = false;
        foreach (var wound in limb.Wounds)
        {
            changed |= !wound.Salved;
            wound.Salved = true;
        }

        Dirty(ent);
        return changed;
    }

    /// <summary>
    ///     Splints the limb, so a fracture stops acting broken.
    /// </summary>
    public void Splint(Entity<VgBodyComponent> ent, VgLimb limb)
    {
        limb.Status |= VgLimbStatus.Splinted;
        Dirty(ent);
    }

    /// <summary>
    ///     Heals one limb (vg13 organ heal_damage()) and updates the mob's totals to match.
    /// </summary>
    public void HealLimb(Entity<VgBodyComponent> ent, VgLimb limb, float brute, float burn)
    {
        HealLimbDamage(ent, limb, brute, burn);
        Dirty(ent);
        WriteBackTotals(ent);
    }

    /// <summary>
    ///     Whether the wound is still at one of its bleeding stages (vg13 current_stage &lt;= max_bleeding_stage).
    /// </summary>
    public bool IsAtBleedingStage(VgWound wound)
    {
        return wound.Stage <= Prototypes.Index(wound.Prototype).LastBleedingStage;
    }

    public VgWoundType GetWoundType(VgWound wound)
    {
        return Prototypes.Index(wound.Prototype).DamageType;
    }
}
