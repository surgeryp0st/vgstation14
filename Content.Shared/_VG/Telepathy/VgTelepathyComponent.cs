using Robust.Shared.Prototypes;

namespace Content.Shared._VG.Telepathy;

/// <summary>
///     vg13's telepathy power (<c>/spell/targeted/telepathy</c>, Greys have it from their species): speak straight into the
///     mind of someone whose voice you've heard or whom you've examined, anywhere on the same level.
/// </summary>
[RegisterComponent]
public sealed partial class VgTelepathyComponent : Component
{
    [DataField]
    public EntProtoId ActionId = "VgActionTelepathy";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    ///     vg13 mushroom people (SPECIES_NO_MOUTH): their speech is telepathy to <see cref="Targets"/> instead of sound.
    /// </summary>
    [DataField]
    public bool RedirectSpeech;

    [ViewVariables]
    public VgTelepathyMode Mode = VgTelepathyMode.Local;

    [ViewVariables]
    public List<EntityUid> Targets = new();

    /// <summary>
    ///     vg13 <c>mind.heard_before</c>: the people this mob has heard speak or examined, by the name they had then.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, EntityUid> HeardBefore = new();
}
