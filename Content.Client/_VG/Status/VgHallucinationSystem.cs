using System.Linq;
using Content.Client.Alerts;
using Content.Client.UserInterface.Controls;
using Content.Shared._VG.Status;
using Content.Shared.Alert;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._VG.Status;

/// <summary>
///     Draws a hallucinating player's hallucinations (vg13 handle_hallucinations()), as the server describes them in
///     <see cref="VgHallucinationEvent"/>s. Everything here is seen and heard by this player only.
/// </summary>
public sealed class VgHallucinationSystem : EntitySystem
{
    [Dependency] private readonly ClientAlertsSystem _alerts = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const string Image = "VgHallucinationImage";

    private static readonly ProtoId<AlertPrototype>[] ScrewyAlerts = { "HumanCrit", "HumanDead", "LowOxygen", "Toxins" };

    private static readonly string[] StrangeSlots = { "belt", "pocket1", "pocket2" };

    /// <summary>
    ///     Something to undo later.
    /// </summary>
    private readonly List<(TimeSpan When, Action Undo)> _pending = new();

    private readonly Dictionary<int, EntityUid> _attackers = new();

    private sealed class Screwy
    {
        public ProtoId<AlertPrototype> Fake;
        public AlertState? Real;
        public TimeSpan Until;
    }

    private readonly List<Screwy> _screwy = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VgHallucinationEvent>(OnHallucination);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var (_, undo) in _pending)
        {
            undo();
        }
        _pending.Clear();
    }

    private void Later(float seconds, Action action)
    {
        _pending.Add((_timing.CurTime + TimeSpan.FromSeconds(seconds), action));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (now < _pending[i].When)
                continue;

            var undo = _pending[i].Undo;
            _pending.RemoveAt(i);
            undo();
        }

        UpdateScrewyHud(now);
    }

    private void OnHallucination(VgHallucinationEvent ev)
    {
        switch (ev.Kind)
        {
            case VgHallucinationKind.Sound:
                if (ev.Delay > 0)
                    Later(ev.Delay, () => PlaySound(ev.Sound));
                else
                    PlaySound(ev.Sound);
                break;
            case VgHallucinationKind.ScrewyHud:
                ScrewyHud(ev);
                break;
            case VgHallucinationKind.StrangeItem:
                StrangeItem(ev);
                break;
            case VgHallucinationKind.DangerFlash:
            case VgHallucinationKind.FakeBody:
                if (ev.Where is { } where)
                    Flash(GetCoordinates(where), ev.Rsi, ev.State, ev.Prototype, ev.Duration);
                break;
            case VgHallucinationKind.Disguise:
                Disguise(ev);
                break;
            case VgHallucinationKind.TurnScreen:
                TurnScreen(ev);
                break;
            case VgHallucinationKind.AttackerMove:
                AttackerMove(ev);
                break;
            case VgHallucinationKind.AttackerHit:
                PlaySound(ev.Sound);
                if (ev.Where is { } blood && ev.State is { } splatter)
                    Flash(GetCoordinates(blood), "/Textures/_VG/effects/blood.rsi", splatter, null, 30);
                break;
            case VgHallucinationKind.AttackerGone:
                if (_attackers.Remove(ev.Id, out var attacker))
                    QueueDel(attacker);
                break;
        }
    }

    private void PlaySound(string? sound)
    {
        if (sound != null)
            _audio.PlayGlobal(new SoundPathSpecifier(sound), Filter.Local(), false);
    }

    /// <summary>
    ///     vg13 hal_screwyhud: the health indicator says crit or dead, or the oxygen or toxin warning lights up.
    /// </summary>
    private void ScrewyHud(VgHallucinationEvent ev)
    {
        if (_player.LocalEntity is not { } local || ev.Variant < 1 || ev.Variant > ScrewyAlerts.Length)
            return;

        var fake = ScrewyAlerts[ev.Variant - 1];
        var category = _proto.Index(fake).Category;
        _screwy.Add(new Screwy
        {
            Fake = fake,
            Real = RealAlert(category, fake),
            Until = _timing.CurTime + TimeSpan.FromSeconds(ev.Duration),
        });
        _alerts.ShowAlert(local, fake);
    }

    private AlertState? RealAlert(ProtoId<AlertCategoryPrototype>? category, ProtoId<AlertPrototype> fake)
    {
        if (_alerts.ActiveAlerts is not { } active)
            return null;

        foreach (var (key, state) in active)
        {
            if (category != null ? key.AlertCategory == category : key.AlertType == fake)
            {
                if (ScrewyAlerts.Contains(state.Type) && _screwy.Any(s => s.Fake == state.Type))
                    continue;
                return state;
            }
        }

        return null;
    }

    private void UpdateScrewyHud(TimeSpan now)
    {
        if (_screwy.Count == 0 || _player.LocalEntity is not { } local)
            return;

        for (var i = _screwy.Count - 1; i >= 0; i--)
        {
            var screwy = _screwy[i];
            var active = _alerts.ActiveAlerts;
            var showing = active != null && active.Values.Any(a => a.Type == screwy.Fake);

            if (now >= screwy.Until)
            {
                _screwy.RemoveAt(i);
                if (showing)
                    _alerts.ClearAlert(local, screwy.Fake);
                if (screwy.Real is { } real)
                    _alerts.ShowAlert(local, real.Type, real.Severity);
                continue;
            }

            // A fresh state from the server put the real alert back: note it, and lie again.
            if (!showing)
            {
                screwy.Real = RealAlert(_proto.Index(screwy.Fake).Category, screwy.Fake) ?? screwy.Real;
                _alerts.ShowAlert(local, screwy.Fake);
            }
        }
    }

    /// <summary>
    ///     vg13 halitem: a revolver, C4, a sword, a baton, an emag or a flashbang in an empty hand, belt or pocket.
    /// </summary>
    private void StrangeItem(VgHallucinationEvent ev)
    {
        if (ev.Prototype == null || _ui.RootControl is not { } root)
            return;

        var slots = new List<SlotControl>();
        Collect(root, slots);
        var empty = slots.Where(s => s.Entity == null && (s is Content.Client.UserInterface.Systems.Hands.Controls.HandButton || StrangeSlots.Contains(s.SlotName))).ToList();
        if (empty.Count == 0)
            return;

        var slot = _random.Pick(empty);
        var fake = Spawn(ev.Prototype, MapCoordinates.Nullspace);
        slot.SetEntity(fake);
        Later(ev.Duration, () =>
        {
            if (slot.Entity == fake)
                slot.SetEntity(null);
            QueueDel(fake);
        });
    }

    private static void Collect(Control control, List<SlotControl> into)
    {
        foreach (var child in control.Children)
        {
            if (child is SlotControl slot)
                into.Add(slot);
            Collect(child, into);
        }
    }

    /// <summary>
    ///     Something seen on the floor for a moment: an image from an RSI, or what a prototype looks like.
    /// </summary>
    private void Flash(EntityCoordinates where, string? rsi, string? state, string? prototype, float duration)
    {
        var image = Spawn(Image, where);
        if (prototype != null)
            LookLike(image, prototype);
        else if (rsi != null && state != null && TryComp<SpriteComponent>(image, out var sprite))
            sprite.AddLayer(new SpriteSpecifier.Rsi(new ResPath(rsi), state));

        Later(duration, () => QueueDel(image));
    }

    private void LookLike(EntityUid image, string prototype)
    {
        if (!_proto.TryIndex<EntityPrototype>(prototype, out var proto) || !proto.Components.TryGetValue("Sprite", out var sprite))
            return;

        EntityManager.AddComponents(image, new ComponentRegistry { ["Sprite"] = sprite }, removeExisting: true);
    }

    /// <summary>
    ///     vg13 food and monster images: someone looks like something else for a minute or two.
    /// </summary>
    private void Disguise(VgHallucinationEvent ev)
    {
        if (ev.Prototype == null || ev.Target is not { } net || !TryGetEntity(net, out var target)
            || !TryComp<SpriteComponent>(target, out var targetSprite))
        {
            return;
        }

        var image = Spawn(Image, new EntityCoordinates(target.Value, 0, 0));
        LookLike(image, ev.Prototype);
        var wasVisible = targetSprite.Visible;
        targetSprite.Visible = false;

        Later(ev.Duration, () =>
        {
            QueueDel(image);
            if (TryComp<SpriteComponent>(target, out var sprite))
                sprite.Visible = wasVisible;
        });
    }

    /// <summary>
    ///     vg13 turns the client's view a quarter, half or three quarters round for a while.
    /// </summary>
    private void TurnScreen(VgHallucinationEvent ev)
    {
        if (_player.LocalEntity is not { } local || !TryComp<EyeComponent>(local, out var eye))
            return;

        var angle = Angle.FromDegrees(ev.Variant);
        _eye.SetRotation(local, eye.Rotation + angle, eye);
        Later(ev.Duration, () =>
        {
            if (TryComp<EyeComponent>(local, out var after))
                _eye.SetRotation(local, after.Rotation - angle, after);
        });
    }

    /// <summary>
    ///     vg13 fake_attacker: someone's double, closing in.
    /// </summary>
    private void AttackerMove(VgHallucinationEvent ev)
    {
        if (ev.Where is not { } where)
            return;

        var coords = GetCoordinates(where);
        if (!_attackers.TryGetValue(ev.Id, out var attacker) || TerminatingOrDeleted(attacker))
        {
            attacker = Spawn(Image, coords);
            _attackers[ev.Id] = attacker;

            var look = ev.Target is { } net && TryGetEntity(net, out var source) ? source : _player.LocalEntity;
            if (TryComp<SpriteComponent>(look, out var lookSprite) && TryComp<SpriteComponent>(attacker, out var sprite))
                sprite.CopyFrom(lookSprite);
            return;
        }

        _transform.SetCoordinates(attacker, coords);
    }
}
