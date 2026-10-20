using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._VG.Popups;

/// <summary>
///     vg13's visible_message() and to_chat() for predicted code. The player whose client predicts the event sees its
///     message right away; the server shows everyone else theirs and leaves that player out.
/// </summary>
public sealed class VgPopupSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <summary>
    ///     vg13 visible_message(): <paramref name="self"/> for <paramref name="uid"/>, <paramref name="others"/> for
    ///     everyone else in view. Either can be null.
    /// </summary>
    /// <param name="predictingUser">The player whose client is running this prediction, if any.</param>
    public void Visible(EntityUid uid, string? others, string? self, EntityUid? predictingUser, PopupType type = PopupType.MediumCaution)
    {
        if (_net.IsClient)
        {
            _popup.PopupClient(predictingUser == uid ? self : others, uid, predictingUser, type);
            return;
        }

        if (self != null && predictingUser != uid)
            _popup.PopupEntity(self, uid, uid, type);

        if (others == null)
            return;

        var filter = Filter.PvsExcept(uid);
        if (predictingUser is { } user)
            filter.RemoveWhereAttachedEntity(e => e == user);

        _popup.PopupEntity(others, uid, filter, true, type);
    }

    /// <summary>
    ///     vg13 to_chat(): a message only <paramref name="uid"/> sees.
    /// </summary>
    public void Self(EntityUid uid, string message, EntityUid? predictingUser, PopupType type = PopupType.MediumCaution)
    {
        Visible(uid, null, message, predictingUser, type);
    }
}
