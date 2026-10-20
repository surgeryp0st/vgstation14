using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Body;

/// <summary>
///     An internal organ, vg13's /datum/organ/internal. Defined per species in a <see cref="VgBodyPrototype"/>.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class VgOrgan
{
    /// <summary>
    ///     vg13 organ_type: "heart", "lungs", "liver", "kidneys", "brain", "appendix", "eyes".
    /// </summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    ///     The limb it sits in (vg13 parent_organ).
    /// </summary>
    [DataField(required: true)]
    public string Parent = string.Empty;

    [DataField]
    public float MinBruisedDamage = 10;

    [DataField]
    public float MinBrokenDamage = 30;

    /// <summary>
    ///     Losing it kills you (the brain).
    /// </summary>
    [DataField]
    public bool Vital;

    /// <summary>
    ///     vg13 robotic: 0 organic, 1 assisted (pacemaker etc.), 2 fully robotic.
    /// </summary>
    [DataField]
    public int Robotic;

    /// <summary>
    ///     Item the organ becomes when taken out.
    /// </summary>
    [DataField]
    public EntProtoId? RemovedPrototype;

    [DataField]
    public float Damage;

    [DataField]
    public float GermLevel;

    public bool IsBruised => Damage >= MinBruisedDamage;
    public bool IsBroken => Damage >= MinBrokenDamage;

    public VgOrgan()
    {
    }

    public VgOrgan(VgOrgan other)
    {
        Id = other.Id;
        Name = other.Name;
        Parent = other.Parent;
        MinBruisedDamage = other.MinBruisedDamage;
        MinBrokenDamage = other.MinBrokenDamage;
        Vital = other.Vital;
        Robotic = other.Robotic;
        RemovedPrototype = other.RemovedPrototype;
        Damage = other.Damage;
        GermLevel = other.GermLevel;
    }
}

/// <summary>
///     vg13 organ_type names.
/// </summary>
public static class VgOrgans
{
    public const string Heart = "heart";
    public const string Lungs = "lungs";
    public const string Liver = "liver";
    public const string Kidneys = "kidneys";
    public const string Brain = "brain";
    public const string Appendix = "appendix";
    public const string Eyes = "eyes";
}
