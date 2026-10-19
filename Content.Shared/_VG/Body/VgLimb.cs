using Content.Shared.Hands.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Body;

/// <summary>
///     An external organ (limb), vg13's /datum/organ/external. Defined per species in a <see cref="VgBodyPrototype"/>.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class VgLimb
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public string? Parent;

    /// <summary>
    ///     Filled in from the other limbs' parents when the body is created.
    /// </summary>
    [DataField]
    public List<string> Children = new();

    [DataField(required: true)]
    public VgBodyParts BodyPart;

    [DataField(required: true)]
    public float MaxDamage;

    [DataField(required: true)]
    public float MinBrokenDamage;

    /// <summary>
    ///     Losing a vital limb kills you.
    /// </summary>
    [DataField]
    public bool Vital;

    /// <summary>
    ///     What protects the organs inside, e.g. "skull". Has to be sawn open in surgery.
    /// </summary>
    [DataField]
    public LocId? Encased;

    /// <summary>
    ///     The hand this limb holds items with, if it grasps.
    /// </summary>
    [DataField]
    public HandLocation? Grasp;

    /// <summary>
    ///     Whether this limb holds things itself (hands) rather than just carrying a grasping limb (arms).
    /// </summary>
    [DataField]
    public bool CanGrasp;

    [DataField]
    public bool CanStand;

    /// <summary>
    ///     Item spawned when the limb is severed.
    /// </summary>
    [DataField]
    public EntProtoId? DroppedPrototype;

    [DataField]
    public VgLimbStatus Status;

    [DataField]
    public float BruteDamage;

    [DataField]
    public float BurnDamage;

    [DataField]
    public List<VgWound> Wounds = new();

    [DataField]
    public int NumberWounds;

    /// <summary>
    ///     Damage the limb keeps while a fracture is untreated.
    /// </summary>
    [DataField]
    public float PermaInjury;

    /// <summary>
    ///     Cleanly amputated (doesn't bleed or hurt).
    /// </summary>
    [DataField]
    public bool Amputated;

    /// <summary>
    ///     vg13 destspawn: the severed limb item has already been made.
    /// </summary>
    [DataField]
    public bool DroppedSpawned;

    [DataField]
    public bool InternallyBleeding;

    [DataField]
    public float GermLevel;

    /// <summary>
    ///     Surgical incision stage. Open limbs bleed.
    /// </summary>
    [DataField]
    public int Open;

    public float Health => BruteDamage + BurnDamage;

    public bool IsRobotic => (Status & VgLimbStatus.Robot) != 0;
    public bool IsPeg => (Status & VgLimbStatus.Peg) != 0;
    public bool IsOrganic => (Status & (VgLimbStatus.Robot | VgLimbStatus.Peg)) == 0;
    public bool IsExisting => (Status & (VgLimbStatus.Destroyed | VgLimbStatus.CutAway)) == 0;

    public bool IsUsable => (Status & (VgLimbStatus.Destroyed | VgLimbStatus.Mutated | VgLimbStatus.Dead |
                                       VgLimbStatus.CutAway | VgLimbStatus.Malfunctioning)) == 0;

    /// <summary>
    ///     Broken and not splinted.
    /// </summary>
    public bool IsBroken => (Status & VgLimbStatus.Broken) != 0 && (Status & VgLimbStatus.Splinted) == 0;

    public bool IsTorso => (BodyPart & VgBodyParts.FullTorso) != 0;

    public VgLimb()
    {
    }

    public VgLimb(VgLimb other)
    {
        Id = other.Id;
        Name = other.Name;
        Parent = other.Parent;
        Children = new List<string>(other.Children);
        BodyPart = other.BodyPart;
        MaxDamage = other.MaxDamage;
        MinBrokenDamage = other.MinBrokenDamage;
        Vital = other.Vital;
        Encased = other.Encased;
        Grasp = other.Grasp;
        CanGrasp = other.CanGrasp;
        CanStand = other.CanStand;
        DroppedPrototype = other.DroppedPrototype;
        Status = other.Status;
        BruteDamage = other.BruteDamage;
        BurnDamage = other.BurnDamage;
        Wounds = other.Wounds.ConvertAll(w => new VgWound(w));
        NumberWounds = other.NumberWounds;
        PermaInjury = other.PermaInjury;
        Amputated = other.Amputated;
        DroppedSpawned = other.DroppedSpawned;
        InternallyBleeding = other.InternallyBleeding;
        GermLevel = other.GermLevel;
        Open = other.Open;
    }
}
