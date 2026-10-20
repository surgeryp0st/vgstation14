using Content.Shared._VG.Armor;
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
    ///     The vg13 armor category this attack checks (melee, bullet, ...). Null means armor doesn't apply.
    /// </summary>
    [DataField("vgArmor")]
    public VgArmorType? VgArmor;

    /// <summary>
    ///     vg13 armor_penetration: armor points the attack ignores.
    /// </summary>
    [DataField("vgArmorPenetration")]
    public float VgArmorPenetration;

    /// <summary>
    ///     Whether vg13 armor_absorb also applies (vg13 only runs it for projectiles and unarmed attacks).
    /// </summary>
    [DataField("vgArmorAbsorb")]
    public bool VgArmorAbsorb;

    /// <summary>
    ///     Copies the vg13 properties from another specifier, keeping the sharper and more penetrating of the two.
    /// </summary>
    public void CombineVgProperties(DamageSpecifier other)
    {
        VgSharpness = MathF.Max(VgSharpness, other.VgSharpness);
        VgEdge |= other.VgEdge;
        VgZone ??= other.VgZone;
        VgArmor ??= other.VgArmor;
        VgArmorPenetration = MathF.Max(VgArmorPenetration, other.VgArmorPenetration);
        VgArmorAbsorb |= other.VgArmorAbsorb;
    }

    /// <summary>
    ///     Returns a copy of this damage as a vg13 attack of the given armor category, at a zone if given.
    /// </summary>
    public DamageSpecifier WithVgAttack(VgArmorType armor, string? zone, bool absorb = false)
    {
        return new DamageSpecifier(this) { VgArmor = armor, VgZone = zone ?? VgZone, VgArmorAbsorb = absorb || VgArmorAbsorb };
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
