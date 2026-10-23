using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._VG.Status;

/// <summary>
///     The kinds of thing a hallucinating player sees or hears (vg13 handle_hallucinations()).
/// </summary>
[Serializable, NetSerializable]
public enum VgHallucinationKind : byte
{
    /// <summary>A sound only they hear.</summary>
    Sound,

    /// <summary>The health, oxygen or toxin indicator lies (Variant: 1 crit, 2 dead, 3 oxygen, 4 toxins).</summary>
    ScrewyHud,

    /// <summary>Something dangerous in an empty hand, belt or pocket slot (Prototype).</summary>
    StrangeItem,

    /// <summary>A flash of space, fire, C4 or a flashbang on the floor (Rsi/State or Prototype at Where).</summary>
    DangerFlash,

    /// <summary>A body on the floor (Rsi/State at Where).</summary>
    FakeBody,

    /// <summary>Target looks like something else (Prototype) for a while.</summary>
    Disguise,

    /// <summary>The screen turns (Variant: degrees).</summary>
    TurnScreen,

    /// <summary>A fake attacker (Id) appears or moves to Where, looking like Target.</summary>
    AttackerMove,

    /// <summary>A fake attacker's hit: a sound, maybe blood at Where.</summary>
    AttackerHit,

    /// <summary>A fake attacker goes.</summary>
    AttackerGone,
}

/// <summary>
///     Sent to a hallucinating player only: what their client should show or play. The server picks it (and does
///     whatever really happens to them); the client draws it.
/// </summary>
[Serializable, NetSerializable]
public sealed class VgHallucinationEvent : EntityEventArgs
{
    public VgHallucinationKind Kind;
    public float Duration;
    public float Delay;
    public NetCoordinates? Where;
    public NetEntity? Target;
    public string? Prototype;
    public string? Rsi;
    public string? State;
    public string? Sound;
    public int Variant;
    public int Id;
}
