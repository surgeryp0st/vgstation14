using Content.Server.Body.Systems;
using Content.Shared._VG.Species;
using Content.Server.Body.Components;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.Prototypes;

namespace Content.Server._VG.Species;

/// <summary>
///     Equips a species' own outfit for the job it spawned into (<see cref="VgSpeciesGearPrototype"/>), then switches
///     internals on if a tank came with it, as vg13's species <c>final_equip</c> does for plasmamen.
/// </summary>
public sealed class VgSpeciesGearSystem : EntitySystem
{
    [Dependency] private readonly InternalsSystem _internals = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedStationSpawningSystem _spawning = default!;

    public void Equip(EntityUid ent, ProtoId<JobPrototype>? job)
    {
        if (!TryComp<VgSpeciesComponent>(ent, out var comp))
            return;

        VgSpeciesGearPrototype? found = null;
        foreach (var gear in _proto.EnumeratePrototypes<VgSpeciesGearPrototype>())
        {
            if (gear.Species != comp.Species)
                continue;

            if (job != null && gear.Jobs.Contains(job.Value))
            {
                found = gear;
                break;
            }

            if (gear.Default)
                found ??= gear;
        }

        if (found == null)
            return;

        // The species' gear replaces what the job put in those slots, as vg13's per-species outfit lists do.
        if (_proto.TryIndex(found.Gear, out var startingGear))
        {
            foreach (var slot in startingGear.Equipment.Keys)
            {
                if (_inventory.TryUnequip(ent, slot, out var old, silent: true, force: true))
                    QueueDel(old);
            }
        }

        _spawning.EquipStartingGear(ent, found.Gear, raiseEvent: false);

        if (TryComp<InternalsComponent>(ent, out var internals) && !_internals.AreInternalsWorking(internals))
            _internals.ToggleInternals(ent, ent, force: false, internals);
    }
}
