using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Medical;

[Serializable, NetSerializable]
public enum VgMedicalKind : byte
{
    /// <summary>
    ///     vg13 bruise_pack: bandages the limb's wounds.
    /// </summary>
    Bandage,

    /// <summary>
    ///     vg13 ointment: salves the limb's wounds.
    /// </summary>
    Salve,

    /// <summary>
    ///     vg13 splint: secures a fracture.
    /// </summary>
    Splint,
}

/// <summary>
///     A vg13 medical stack (/obj/item/stack/medical): used on the limb the user is aiming at.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgMedicalComponent : Component
{
    [DataField(required: true)]
    public VgMedicalKind Kind;

    /// <summary>
    ///     Advanced kits also heal rand(HealBrute, HealBrute + 5) / rand(HealBurn, HealBurn + 5) on the limb.
    ///     Mobs without vg limbs get half of this.
    /// </summary>
    [DataField]
    public float HealBrute;

    [DataField]
    public float HealBurn;

    /// <summary>
    ///     Advanced kits use different messages (bioglue, regenerative membrane).
    /// </summary>
    [DataField]
    public bool Advanced;

    /// <summary>
    ///     vg13 do_mob(user, M, 50) for splints.
    /// </summary>
    [DataField]
    public TimeSpan SplintDelay = TimeSpan.FromSeconds(5);
}

/// <summary>
///     Marks clothing that seals out space (vg13 space helmets and suits), which medical stacks can't get through.
///     The server adds it to anything with pressure protection, so the client can predict the check.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VgSpaceproofComponent : Component;

/// <summary>
///     Finishing a splint on the limb at <see cref="Zone"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class VgSplintDoAfterEvent : DoAfterEvent
{
    [DataField]
    public string Zone { get; private set; } = string.Empty;

    private VgSplintDoAfterEvent()
    {
    }

    public VgSplintDoAfterEvent(string zone)
    {
        Zone = zone;
    }

    public override DoAfterEvent Clone() => this;
}
