using Content.Shared._VG.Fire;
using Robust.Shared.Prototypes;

namespace Content.Server._VG.Fire;

/// <summary>
///     Makes SS14's entities burn as vg13's would (<see cref="VgFlammablePrototype"/>): gives them a
///     <see cref="VgBurnableComponent"/> as they are created.
/// </summary>
public sealed class VgFlammableSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<string, VgFlammablePrototype?> _cache = new();

    public override void Initialize()
    {
        base.Initialize();

        EntityManager.EntityInitialized += OnEntityInitialized;
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _cache.Clear());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        EntityManager.EntityInitialized -= OnEntityInitialized;
    }

    private void OnEntityInitialized(Entity<MetaDataComponent> ent)
    {
        if (ent.Comp.EntityPrototype is not { } entityProto || Get(entityProto.ID) is not { Burns: true } flammable)
            return;

        if (HasComp<VgBurnableComponent>(ent))
            return;

        var burnable = AddComp<VgBurnableComponent>(ent);
        burnable.Material = flammable.Material;
        burnable.ThermalMass = flammable.ThermalMass;
        Dirty(ent, burnable);
    }

    /// <summary>
    ///     The entity prototype's own entry, or its nearest listed parent's.
    /// </summary>
    public VgFlammablePrototype? Get(string entityPrototype)
    {
        if (_cache.TryGetValue(entityPrototype, out var cached))
            return cached;

        VgFlammablePrototype? found = null;
        foreach (var parent in _proto.EnumerateParents<EntityPrototype>(entityPrototype, includeSelf: true))
        {
            if (_proto.TryIndex(parent.ID, out found))
                break;
        }

        _cache[entityPrototype] = found;
        return found;
    }
}
