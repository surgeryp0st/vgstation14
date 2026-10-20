using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Clothing;

/// <summary>
///     Finds an item's vg13 clothing properties (<see cref="VgClothingPrototype"/>), from its own entity prototype or
///     the nearest parent that has some.
/// </summary>
public sealed class VgClothingSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<string, VgClothingPrototype?> _cache = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _cache.Clear());
    }

    public VgClothingPrototype? Get(EntityUid item)
    {
        if (MetaData(item).EntityPrototype is not { } entityProto)
            return null;

        if (_cache.TryGetValue(entityProto.ID, out var cached))
            return cached;

        VgClothingPrototype? found = null;
        foreach (var parent in _proto.EnumerateParents<EntityPrototype>(entityProto.ID, includeSelf: true))
        {
            if (_proto.TryIndex(parent.ID, out found))
                break;
        }

        _cache[entityProto.ID] = found;
        return found;
    }
}
