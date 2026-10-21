using System.Linq;
using Content.Shared._VG.Body;
using Content.Shared._VG.Breathing;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Species;

public sealed class VgSpeciesSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgSpeciesComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<VgSpeciesComponent> ent, ref MapInitEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.Species, out var species))
            return;

        // vg13 species flags: what the mob doesn't do at all is taken off it.
        if (species.NoBreathe)
            RemComp<VgBreathingComponent>(ent);

        if (TryComp<VgBodyComponent>(ent, out var body))
        {
            if (species.NoBlood)
                body.HasBlood = false;

            // The species decides which internal organs the body has.
            foreach (var id in body.Organs.Keys.ToArray())
            {
                if (!species.Organs.Contains(id))
                    body.Organs.Remove(id);
            }

            Dirty(ent.Owner, body);
        }
    }

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
