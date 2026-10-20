using Content.Client.Gameplay;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Shared._VG.Combat;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._VG.Combat;

/// <summary>
///     Puts the vg13 intent and zone selectors at the right end of the hotbar, and keeps them showing the local
///     mob's intent and zone. Clicks are sent as predictive events.
/// </summary>
public sealed class VgCombatUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [UISystemDependency] private readonly SpriteSystem? _sprite = default;

    private static readonly ResPath ScreenRsi = new("/Textures/_VG/mob/screen1_Midnight.rsi");
    private static readonly ResPath ZoneRsi = new("/Textures/_VG/mob/zone_sel.rsi");

    private VgCombatSelector? _selector;

    public void OnStateEntered(GameplayState state)
    {
        UIManager.OnScreenChanged += OnScreenChanged;
        Attach();
    }

    public void OnStateExited(GameplayState state)
    {
        UIManager.OnScreenChanged -= OnScreenChanged;
        Detach();
    }

    private void OnScreenChanged((UIScreen? Old, UIScreen? New) screens)
    {
        Detach();
        Attach();
    }

    private void Attach()
    {
        if (_selector != null || _sprite == null)
            return;

        if (UIManager.ActiveScreen?.GetWidget<HotbarGui>() is not { } hotbar)
            return;

        var sprite = _sprite;
        _selector = new VgCombatSelector(
            intent => sprite.Frame0(new SpriteSpecifier.Rsi(ScreenRsi, $"intent_{IntentState(intent)}")),
            sprite.Frame0(new SpriteSpecifier.Rsi(ScreenRsi, "zone_sel")),
            zone => sprite.Frame0(new SpriteSpecifier.Rsi(ZoneRsi, zone)));

        _selector.OnIntentPressed += intent => EntityManager.RaisePredictiveEvent(new VgSetIntentEvent(intent));
        _selector.OnZonePressed += zone => EntityManager.RaisePredictiveEvent(new VgSetZoneEvent(zone));

        hotbar.FindControl<BoxContainer>("Hotbar").AddChild(_selector);
    }

    private void Detach()
    {
        _selector?.Orphan();
        _selector = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_selector == null)
        {
            Attach();
            if (_selector == null)
                return;
        }

        if (EntityManager.TryGetComponent<VgIntentComponent>(_player.LocalEntity, out var comp))
        {
            _selector.Visible = true;
            _selector.Update(comp.Intent, comp.Zone);
        }
        else
        {
            _selector.Visible = false;
        }
    }

    /// <summary>
    ///     vg13 calls harm "hurt" in its icon states.
    /// </summary>
    private static string IntentState(VgIntent intent)
    {
        return intent == VgIntent.Harm ? "hurt" : intent.ToString().ToLowerInvariant();
    }
}
