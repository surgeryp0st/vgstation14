using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._VG.Body;

/// <summary>
///     A kind of wound (vg13 /datum/wound subtypes). The wound gets better as it moves down its stages.
/// </summary>
[Prototype("vgWound")]
public sealed partial class VgWoundPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<VgWoundPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    [DataField(required: true)]
    public VgWoundType DamageType;

    /// <summary>
    ///     Stages from worst to best. A wound is at a stage while its damage per instance is at least the stage's damage.
    /// </summary>
    [DataField(required: true)]
    public List<VgWoundStage> Stages = new();

    /// <summary>
    ///     Stage a new wound starts at (1-based), letting several wound sizes share one stage list.
    /// </summary>
    [DataField]
    public int StartStage = 1;

    /// <summary>
    ///     How many of the last stages don't bleed, as in vg13 (1 = all but the last stage bleed).
    /// </summary>
    [DataField]
    public int MaxBleedingStage = 1;

    /// <summary>
    ///     Whether the wound only heals once bandaged (cuts/bruises) or salved (burns).
    /// </summary>
    [DataField]
    public bool NeedsTreatment;

    /// <summary>
    ///     Internal wounds (internal bleeding) don't count towards limb damage and only heal through surgery.
    /// </summary>
    [DataField]
    public bool Internal;

    /// <summary>
    ///     The stage index (1-based) at or below which the wound bleeds.
    /// </summary>
    public int LastBleedingStage => Stages.Count - MaxBleedingStage;
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct VgWoundStage
{
    [DataField(required: true)]
    public LocId Desc;

    [DataField(required: true)]
    public float Damage;
}

/// <summary>
///     A wound on a limb. Several wounds of the same description merge into one with a higher <see cref="Amount"/>.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class VgWound
{
    [DataField(required: true)]
    public ProtoId<VgWoundPrototype> Prototype;

    /// <summary>
    ///     Current stage, 1-based like vg13.
    /// </summary>
    [DataField]
    public int Stage = 1;

    [DataField]
    public float Damage;

    /// <summary>
    ///     Ticks of bleeding left.
    /// </summary>
    [DataField]
    public float BleedTimer;

    [DataField]
    public int Amount = 1;

    [DataField]
    public bool Bandaged;

    [DataField]
    public bool Clamped;

    [DataField]
    public bool Salved;

    [DataField]
    public bool Disinfected;

    [DataField]
    public float GermLevel;

    [DataField]
    public TimeSpan Created;

    public VgWound()
    {
    }

    public VgWound(VgWound other)
    {
        Prototype = other.Prototype;
        Stage = other.Stage;
        Damage = other.Damage;
        BleedTimer = other.BleedTimer;
        Amount = other.Amount;
        Bandaged = other.Bandaged;
        Clamped = other.Clamped;
        Salved = other.Salved;
        Disinfected = other.Disinfected;
        GermLevel = other.GermLevel;
        Created = other.Created;
    }
}
