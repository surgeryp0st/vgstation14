using System.Runtime.CompilerServices;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared._VG.Damage;

/// <summary>
///     Maps upstream SS14 damage types onto vgstation13's damage types (Brute, Burn, Toxin, Oxygen, Clone).
///     Upstream content keeps using its own type names in YAML and code; they are translated here when
///     prototypes load and, as a fallback, whenever damage is applied.
/// </summary>
public static class VgDamageTranslation
{
    public const string Brute = "Brute";
    public const string Burn = "Burn";
    public const string Toxin = "Toxin";
    public const string Oxygen = "Oxygen";
    public const string Clone = "Clone";

    /// <summary>
    ///     Upstream type -> (vg type, sharpness, edge). Sharpness/edge mirror vg13's apply_damage(sharp, edge):
    ///     sharpness is the weapon's sharpness (1 for an ordinary knife), edge means it has a blade.
    /// </summary>
    private static readonly Dictionary<string, (string Type, float Sharpness, bool Edge)> Map = new()
    {
        { "Blunt", (Brute, 0f, false) },
        { "Slash", (Brute, 1f, true) },
        { "Piercing", (Brute, 1f, false) },
        { "Heat", (Burn, 0f, false) },
        { "Cold", (Burn, 0f, false) },
        { "Shock", (Burn, 0f, false) },
        { "Caustic", (Burn, 0f, false) },
        { "Poison", (Toxin, 0f, false) },
        { "Radiation", (Toxin, 0f, false) },
        { "Asphyxiation", (Oxygen, 0f, false) },
        { "Bloodloss", (Oxygen, 0f, false) },
        { "Cellular", (Clone, 0f, false) },
    };

    /// <summary>
    ///     When several upstream modifiers collapse into one vg type, the modifier of this upstream type wins.
    /// </summary>
    private static readonly Dictionary<string, string> PrimarySource = new()
    {
        { Brute, "Blunt" },
        { Burn, "Heat" },
        { Toxin, "Poison" },
        { Oxygen, "Asphyxiation" },
        { Clone, "Cellular" },
    };

    /// <summary>
    ///     Returns the vg damage type for a damage type ID. Types without a translation are returned unchanged.
    /// </summary>
    public static string TranslateId(string type)
    {
        return Map.TryGetValue(type, out var vg) ? vg.Type : type;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NeedsTranslation(Dictionary<string, FixedPoint2> dict)
    {
        foreach (var type in dict.Keys)
        {
            if (Map.ContainsKey(type))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Translates a damage specifier's dictionary in place, raising its sharpness/edge to what the upstream
    ///     types it contained imply.
    /// </summary>
    public static void TranslateInPlace(DamageSpecifier damage)
    {
        var dict = damage.DamageDict;
        if (!NeedsTranslation(dict))
            return;

        var translated = new Dictionary<string, FixedPoint2>(dict.Count);
        foreach (var (type, value) in dict)
        {
            var target = type;
            if (Map.TryGetValue(type, out var vg))
            {
                target = vg.Type;
                if (value > 0)
                {
                    damage.VgSharpness = MathF.Max(damage.VgSharpness, vg.Sharpness);
                    damage.VgEdge |= vg.Edge;
                }
            }

            translated[target] = translated.GetValueOrDefault(target) + value;
        }

        dict.Clear();
        foreach (var (type, value) in translated)
        {
            dict[type] = value;
        }
    }

    /// <summary>
    ///     Returns a vg-typed version of the given damage, or the same instance if it is already vg-typed.
    /// </summary>
    public static DamageSpecifier Translate(DamageSpecifier damage)
    {
        if (!NeedsTranslation(damage.DamageDict))
            return damage;

        var copy = new DamageSpecifier(damage);
        TranslateInPlace(copy);
        return copy;
    }

    /// <summary>
    ///     Translates a modifier dictionary (coefficients or flat reductions) in place.
    ///     Where several upstream types map to one vg type, an explicit vg entry wins, then the primary upstream type,
    ///     then whichever upstream type comes first.
    /// </summary>
    public static void TranslateModifiersInPlace(Dictionary<string, float> modifiers)
    {
        var any = false;
        foreach (var type in modifiers.Keys)
        {
            if (Map.ContainsKey(type))
            {
                any = true;
                break;
            }
        }

        if (!any)
            return;

        var translated = new Dictionary<string, float>(modifiers.Count);
        foreach (var (type, value) in modifiers)
        {
            if (!Map.ContainsKey(type))
                translated[type] = value;
        }

        foreach (var (type, value) in modifiers)
        {
            if (!Map.TryGetValue(type, out var vg) || translated.ContainsKey(vg.Type))
                continue;

            var primary = PrimarySource[vg.Type];
            translated[vg.Type] = modifiers.TryGetValue(primary, out var primaryValue) ? primaryValue : value;
        }

        modifiers.Clear();
        foreach (var (type, value) in translated)
        {
            modifiers[type] = value;
        }
    }
}
