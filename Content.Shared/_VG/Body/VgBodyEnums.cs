using Robust.Shared.Serialization;

namespace Content.Shared._VG.Body;

/// <summary>
///     Body part bitflags from vgstation13 (setup.dm). Used for limbs and for clothing coverage.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum VgBodyParts : uint
{
    None = 0,
    Head = 1 << 0,
    UpperTorso = 1 << 1,
    LowerTorso = 1 << 2,
    LegLeft = 1 << 3,
    LegRight = 1 << 4,
    FootLeft = 1 << 5,
    FootRight = 1 << 6,
    ArmLeft = 1 << 7,
    ArmRight = 1 << 8,
    HandLeft = 1 << 9,
    HandRight = 1 << 10,
    Eyes = 1 << 11,
    Mouth = 1 << 12,
    Ears = 1 << 13,
    Beard = 1 << 15,
    Tail = 1 << 19,

    FullTorso = UpperTorso | LowerTorso,
    FullHead = Head | Eyes | Mouth | Ears,
    Legs = LegLeft | LegRight,
    Feet = FootLeft | FootRight,
    Arms = ArmLeft | ArmRight,
    Hands = HandLeft | HandRight,
    FullBody = FullHead | Hands | FullTorso | Arms | Feet | Legs,
}

/// <summary>
///     Limb status flags, vgstation13's ORGAN_* defines.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum VgLimbStatus : ushort
{
    None = 0,
    CutAway = 1 << 0,
    Gauzed = 1 << 1,
    Attachable = 1 << 2,
    Bleeding = 1 << 3,
    Broken = 1 << 5,
    Destroyed = 1 << 6,
    Robot = 1 << 7,
    Splinted = 1 << 8,
    Salved = 1 << 9,
    Dead = 1 << 10,
    Mutated = 1 << 11,
    Peg = 1 << 12,
    Malfunctioning = 1 << 13,
}

[Serializable, NetSerializable]
public enum VgWoundType : byte
{
    Cut,
    Bruise,
    Burn,
}

/// <summary>
///     vgstation13 limb names (limb_defines.dm). Also used as target zones.
/// </summary>
public static class VgLimbs
{
    public const string Head = "head";
    public const string Chest = "chest";
    public const string Groin = "groin";
    public const string LeftArm = "l_arm";
    public const string RightArm = "r_arm";
    public const string LeftHand = "l_hand";
    public const string RightHand = "r_hand";
    public const string LeftLeg = "l_leg";
    public const string RightLeg = "r_leg";
    public const string LeftFoot = "l_foot";
    public const string RightFoot = "r_foot";

    // Zones that aren't limbs; check_zone() maps them onto one.
    public const string Eyes = "eyes";
    public const string Mouth = "mouth";

    /// <summary>
    ///     vg13 check_zone(): eyes and mouth are part of the head, no zone means the chest.
    /// </summary>
    public static string CheckZone(string? zone)
    {
        return zone switch
        {
            null => Chest,
            Eyes or Mouth => Head,
            _ => zone,
        };
    }
}
