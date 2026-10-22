using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Species;

/// <summary>
///     vg13's per-species job outfit (an outfit datum's list under <c>/datum/species/...</c>): what a species wears and
///     carries instead of, or on top of, the job's own gear. The first <see cref="Default"/> gear of the species is used by
///     jobs that no other gear of that species names.
/// </summary>
[Prototype("vgSpeciesGear")]
public sealed partial class VgSpeciesGearPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<VgSpeciesPrototype> Species;

    [DataField]
    public List<ProtoId<Content.Shared.Roles.JobPrototype>> Jobs = new();

    [DataField]
    public bool Default;

    /// <summary>
    ///     Equipped after the job's gear, replacing what's in the slots.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<StartingGearPrototype> Gear;
}
