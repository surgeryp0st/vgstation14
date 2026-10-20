using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Body;

/// <summary>
///     A vg13-style body: external limbs that take damage, get wounds, break and fall off.
///     The mob's brute/burn in <see cref="Content.Shared.Damage.DamageableComponent"/> is the sum of its organic limbs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class VgBodyComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VgBodyPrototype> Prototype;

    /// <summary>
    ///     The limbs. Predicted, so the client deep-copies them out of each server state.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, VgLimb> Limbs = new();

    /// <summary>
    ///     Total wounds over all limbs; more wounds make existing ones easier to tear open.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int NumberWounds;

    /// <summary>
    ///     Damage multipliers from the species (vg13 species brute_mod/burn_mod). Only weaknesses (above 1) spill over.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BruteModifier = 1f;

    [DataField, AutoNetworkedField]
    public float BurnModifier = 1f;

    /// <summary>
    ///     Whether the mob has blood to bleed. Set by the server, which owns the bloodstream until vg13 blood is ported.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HasBlood = true;
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
