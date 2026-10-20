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
