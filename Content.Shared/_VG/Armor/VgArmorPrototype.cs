using Content.Shared._VG.Body;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Armor;

/// <summary>
///     vg13 armor categories (the keys of /obj/item armor lists). An attack says which one it is.
/// </summary>
[Serializable, NetSerializable]
public enum VgArmorType : byte
{
    Melee,
    Bullet,
    Laser,
    Energy,
    Bomb,
    Bio,
    Rad,
}

/// <summary>
///     vg13 armor for an item, keyed by its entity prototype id; children inherit through their parents.
///     Kept apart from the entity prototypes so upstream SS14 clothing doesn't have to be edited.
/// </summary>
[Prototype("vgArmor")]
public sealed partial class VgArmorPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     vg13 armor: percent of the damage blocked, per category (melee, bullet, laser, energy, bomb, bio, rad).
    /// </summary>
    [DataField]
    public Dictionary<string, float> Armor = new();

    /// <summary>
    ///     vg13 armor_absorb: damage under this much is scaled down further (damage * damage / absorb).
    /// </summary>
    [DataField]
    public Dictionary<string, float> Absorb = new();

    /// <summary>
    ///     vg13 body_parts_covered: limbs this item protects.
    /// </summary>
    [DataField]
    public List<VgBodyParts> Coverage = new();

    public VgBodyParts CoverageFlags
    {
        get
        {
            var flags = VgBodyParts.None;
            foreach (var part in Coverage)
            {
                flags |= part;
            }

            return flags;
        }
    }

    public float Get(VgArmorType type)
    {
        return Armor.GetValueOrDefault(Key(type));
    }

    public float GetAbsorb(VgArmorType type)
    {
        return Absorb.GetValueOrDefault(Key(type));
    }

    public static string Key(VgArmorType type)
    {
        return type.ToString().ToLowerInvariant();
    }
}
