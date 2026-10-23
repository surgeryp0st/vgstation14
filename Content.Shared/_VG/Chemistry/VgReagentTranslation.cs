using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Shared._VG.Chemistry;

/// <summary>
///     SS14 reagents that vg13's replace. Any solution loaded from a prototype or a map holds the vg13 reagent instead,
///     so SS14's pills, syringes, food and medkits work on vg13 bodies without editing upstream YAML.
/// </summary>
public static class VgReagentTranslation
{
    /// <summary>
    ///     SS14 id to vg13 id: the same medicine in both games, or for SS14-only ones the closest vg13 medicine.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Replacements = new Dictionary<string, string>
    {
        // Both games.
        ["Bicaridine"] = "bicaridine",
        ["Kelotane"] = "kelotane",
        ["Dylovene"] = "anti_toxin",
        ["Dexalin"] = "dexalin",
        ["DexalinPlus"] = "dexalinp",
        ["Inaprovaline"] = "inaprovaline",
        ["Tricordrazine"] = "tricordrazine",
        ["Cryoxadone"] = "cryoxadone",
        ["Dermaline"] = "dermaline",
        ["Leporazine"] = "leporazine",
        ["Phalanximine"] = "phalanximine",
        ["Charcoal"] = "charcoal",
        ["Lexorin"] = "lexorin",
        ["Synaptizine"] = "synaptizine",
        ["Lipozine"] = "lipozine",

        // SS14-only: the closest vg13 medicine.
        ["Oculine"] = "imidazoline",
        ["Epinephrine"] = "inaprovaline",
        ["Omnizine"] = "tricordrazine",
        ["Doxarubixadone"] = "clonexadone",
        ["Bruizine"] = "bicaridine",
        ["Lacerinol"] = "bicaridine",
        ["Puncturase"] = "bicaridine",
        ["Pyrazine"] = "kelotane",
        ["Insuzine"] = "kelotane",
    };

    public static string Translate(string reagent)
    {
        return Replacements.GetValueOrDefault(reagent, reagent);
    }

    /// <summary>
    ///     Swaps replaced reagents in a solution's contents, merging them into the vg13 reagent if both are there.
    /// </summary>
    public static void Translate(List<ReagentQuantity> contents)
    {
        for (var i = 0; i < contents.Count; i++)
        {
            var (reagent, quantity) = contents[i];
            if (!Replacements.TryGetValue(reagent.Prototype, out var replacement))
                continue;

            var id = new ReagentId(replacement, reagent.Data);
            var existing = contents.FindIndex(q => q.Reagent.Equals(id));
            if (existing >= 0)
            {
                contents[existing] = new ReagentQuantity(id, contents[existing].Quantity + quantity);
                contents.RemoveAt(i);
                i--;
            }
            else
            {
                contents[i] = new ReagentQuantity(id, quantity);
            }
        }
    }
}
