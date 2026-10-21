using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Species;

public sealed class VgSpeciesSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    ///     The mob's vg13 species, or null if it has none (then vg13's human numbers apply).
    /// </summary>
    public VgSpeciesPrototype? Get(EntityUid uid)
    {
        return TryComp<VgSpeciesComponent>(uid, out var comp) && _proto.TryIndex(comp.Species, out var species)
            ? species
            : null;
    }
}
