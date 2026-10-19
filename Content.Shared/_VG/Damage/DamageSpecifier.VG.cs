using Content.Shared._VG.Damage;
using Robust.Shared.Serialization;

namespace Content.Shared.Damage;

public sealed partial class DamageSpecifier : ISerializationHooks
{
    /// <summary>
    ///     vg13 sharpness of the brute damage (0 = blunt, 1 = ordinary knife). Sharper hits cut instead of bruise
    ///     and sever limbs more easily. Implied by upstream Slash/Piercing damage, or set directly in YAML.
    /// </summary>
    [DataField("vgSharpness")]
    public float VgSharpness;

    /// <summary>
    ///     Whether the damage comes from a blade (vg13 SHARP_BLADE). Implied by upstream Slash damage.
    /// </summary>
    [DataField("vgEdge")]
    public bool VgEdge;

    /// <summary>
    ///     The vg13 zone (limb) this damage is aimed at, e.g. "l_arm". Null spreads it over random limbs.
    ///     Specifiers loaded from prototypes are shared, so use <see cref="WithVgZone"/> rather than setting this.
    /// </summary>
    [DataField("vgZone")]
    public string? VgZone;

    /// <summary>
    ///     Copies sharpness, edge and zone from another specifier, keeping the sharper of the two.
    /// </summary>
    public void CombineVgProperties(DamageSpecifier other)
    {
        VgSharpness = MathF.Max(VgSharpness, other.VgSharpness);
        VgEdge |= other.VgEdge;
        VgZone ??= other.VgZone;
    }

    /// <summary>
    ///     Returns a copy of this damage aimed at a vg13 zone.
    /// </summary>
    public DamageSpecifier WithVgZone(string? zone)
    {
        return new DamageSpecifier(this) { VgZone = zone };
    }

    void ISerializationHooks.AfterDeserialization()
    {
        VgDamageTranslation.TranslateInPlace(this);
    }
}

public partial class DamageModifierSet : ISerializationHooks
{
    void ISerializationHooks.AfterDeserialization()
    {
        VgDamageTranslation.TranslateModifiersInPlace(Coefficients);
        VgDamageTranslation.TranslateModifiersInPlace(FlatReduction);
    }
}
