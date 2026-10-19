using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Body;

/// <summary>
///     A vg13-style body: external limbs that take damage, get wounds, break and fall off.
///     The mob's brute/burn in <see cref="Content.Shared.Damage.DamageableComponent"/> is the sum of its organic limbs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VgBodyComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VgBodyPrototype> Prototype;

    [DataField, AutoNetworkedField]
    public Dictionary<string, VgLimb> Limbs = new();

    /// <summary>
    ///     Total wounds over all limbs; more wounds make existing ones easier to tear open.
    /// </summary>
    [DataField]
    public int NumberWounds;

    /// <summary>
    ///     Damage multipliers from the species (vg13 species brute_mod/burn_mod). Only weaknesses (above 1) spill over.
    /// </summary>
    [DataField]
    public float BruteModifier = 1f;

    [DataField]
    public float BurnModifier = 1f;
}

/// <summary>
///     Limb layout for a species, vg13's species/create_organs().
/// </summary>
[Prototype("vgBody")]
public sealed partial class VgBodyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<VgLimb> Limbs = new();
}
