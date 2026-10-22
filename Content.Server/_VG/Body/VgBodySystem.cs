using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Shared._VG.Body;
using Content.Shared._VG.Popups;
using Content.Shared._VG.Status;
using Robust.Shared.Random;
using Content.Shared.FixedPoint;

namespace Content.Server._VG.Body;

/// <summary>
///     The server half of the vg13 limb model: the parts that touch server-only systems (bloodstream, chat emotes).
/// </summary>
public sealed partial class VgBodySystem : SharedVgBodySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom Random = default!;
    [Dependency] private readonly VgPopupSystem _vgPopup = default!;
    [Dependency] private readonly VgStatusSystem _status = default!;

    /// <summary>
    ///     vg13 BLOOD_VOLUME_MAX, used to scale vg13 blood amounts onto the SS14 bloodstream until blood is ported.
    /// </summary>
    private const float VgBloodVolumeMax = 560f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgBodyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VgBodyComponent, Content.Shared._VG.Radiation.VgNosebleedEvent>(OnNosebleed);
    }

    /// <summary>
    ///     vg13 drip(1): a drop of blood, if there's any to lose.
    /// </summary>
    private void OnNosebleed(Entity<VgBodyComponent> ent, ref Content.Shared._VG.Radiation.VgNosebleedEvent args)
    {
        if (!ent.Comp.HasBlood || !TryComp<BloodstreamComponent>(ent, out var bloodstream) || _bloodstream.GetBloodLevelPercentage(ent, bloodstream) <= 0)
            return;

        RemoveBlood(ent, 1);
        _vgPopup.Self(ent, Loc.GetString("vg-radiation-nosebleed"), null, Content.Shared.Popups.PopupType.MediumCaution);
    }

    private void OnStartup(Entity<VgBodyComponent> ent, ref ComponentStartup args)
    {
        // The client can't see the bloodstream, so tell it whether this mob bleeds.
        ent.Comp.HasBlood = HasComp<BloodstreamComponent>(ent);
        Dirty(ent);
    }

    protected override void Scream(EntityUid uid)
    {
        _chat.TryEmoteWithChat(uid, "Scream", ignoreActionBlocker: true);
    }

    protected override void RemoveBlood(EntityUid uid, float amount)
    {
        if (amount <= 0 || !TryComp<BloodstreamComponent>(uid, out var bloodstream))
            return;

        FixedPoint2 maxVolume = bloodstream.BloodMaxVolume;
        var scale = maxVolume.Float() / VgBloodVolumeMax;
        _bloodstream.TryModifyBloodLevel(uid, FixedPoint2.New(-amount * scale), bloodstream);
    }
}
