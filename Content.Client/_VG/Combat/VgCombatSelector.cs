using System.Numerics;
using Content.Shared._VG.Body;
using Content.Shared._VG.Combat;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._VG.Combat;

/// <summary>
///     vg13's intent selector and zone selector (the body doll), side by side, drawn at twice their 32px size like
///     the rest of the hotbar.
/// </summary>
public sealed class VgCombatSelector : BoxContainer
{
    public event Action<VgIntent>? OnIntentPressed;
    public event Action<string>? OnZonePressed;

    private readonly IconControl _intent;
    private readonly IconControl _zone;
    private readonly Func<VgIntent, Texture> _intentTexture;
    private readonly Texture _zoneBase;
    private readonly Func<string, Texture> _zoneTexture;

    private VgIntent? _shownIntent;
    private string? _shownZone;

    public VgCombatSelector(Func<VgIntent, Texture> intentTexture, Texture zoneBase, Func<string, Texture> zoneTexture)
    {
        _intentTexture = intentTexture;
        _zoneBase = zoneBase;
        _zoneTexture = zoneTexture;

        Orientation = LayoutOrientation.Horizontal;
        VerticalAlignment = VAlignment.Bottom;
        Margin = new Thickness(4, 0, 0, 0);

        _intent = new IconControl();
        _intent.OnIconClicked += pos => OnIntentPressed?.Invoke(IntentAt(pos));
        _zone = new IconControl();
        _zone.OnIconClicked += pos =>
        {
            if (ZoneAt(pos) is { } zone)
                OnZonePressed?.Invoke(zone);
        };

        AddChild(_intent);
        AddChild(_zone);
    }

    public void Update(VgIntent intent, string zone)
    {
        if (_shownIntent != intent)
        {
            _shownIntent = intent;
            _intent.Layers = new[] { _intentTexture(intent) };
            _intent.ToolTip = Loc.GetString("vg-intent-selector-tooltip",
                ("intent", Loc.GetString($"vg-intent-{intent.ToString().ToLowerInvariant()}")));
        }

        if (_shownZone != zone)
        {
            _shownZone = zone;
            _zone.Layers = new[] { _zoneBase, _zoneTexture(zone) };
            _zone.ToolTip = Loc.GetString("vg-zone-selector-tooltip",
                ("zone", Loc.GetString($"vg-limb-{zone.Replace('_', '-')}")));
        }
    }

    /// <summary>
    ///     The intent quadrant clicked (vg13 human_hud): help top left, disarm top right, grab bottom right, harm bottom
    ///     left. Positions are DM icon pixels, 1-32 from the bottom left.
    /// </summary>
    private static VgIntent IntentAt(Vector2i pos)
    {
        var top = pos.Y > 16;
        var left = pos.X <= 16;
        return (top, left) switch
        {
            (true, true) => VgIntent.Help,
            (true, false) => VgIntent.Disarm,
            (false, false) => VgIntent.Grab,
            _ => VgIntent.Harm,
        };
    }

    /// <summary>
    ///     vg13 zone_sel/Click(). The doll faces you, so its right side is on the left.
    /// </summary>
    private static string? ZoneAt(Vector2i pos)
    {
        var (x, y) = pos;
        switch (y)
        {
            case >= 1 and <= 3:
                return x switch { >= 10 and <= 15 => VgLimbs.RightFoot, >= 17 and <= 22 => VgLimbs.LeftFoot, _ => null };
            case >= 4 and <= 9:
                return x switch { >= 10 and <= 15 => VgLimbs.RightLeg, >= 17 and <= 22 => VgLimbs.LeftLeg, _ => null };
            case >= 10 and <= 13:
                return x switch
                {
                    >= 8 and <= 11 => VgLimbs.RightHand,
                    >= 12 and <= 20 => VgLimbs.Groin,
                    >= 21 and <= 24 => VgLimbs.LeftHand,
                    _ => null,
                };
            case >= 14 and <= 22:
                return x switch
                {
                    >= 8 and <= 11 => VgLimbs.RightArm,
                    >= 12 and <= 20 => VgLimbs.Chest,
                    >= 21 and <= 24 => VgLimbs.LeftArm,
                    _ => null,
                };
            case >= 23 and <= 30:
                if (x is < 12 or > 20)
                    return null;
                if (y is 23 or 24 && x is >= 15 and <= 17)
                    return VgLimbs.Mouth;
                if (y == 26 && x is >= 14 and <= 18 || y is >= 25 and <= 27 && x is >= 15 and <= 17)
                    return VgLimbs.Eyes;
                return VgLimbs.Head;
            default:
                return null;
        }
    }

    /// <summary>
    ///     A 32x32 vg13 screen icon drawn at 2x, reporting clicks in DM icon pixel coordinates.
    /// </summary>
    private sealed class IconControl : Control
    {
        private const int IconSize = 32;
        private const int Scale = 2;

        public event Action<Vector2i>? OnIconClicked;

        public Texture[] Layers = Array.Empty<Texture>();

        public IconControl()
        {
            MinSize = SetSize = new Vector2(IconSize * Scale, IconSize * Scale);
            MouseFilter = MouseFilterMode.Stop;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            foreach (var layer in Layers)
            {
                handle.DrawTextureRect(layer, PixelSizeBox);
            }
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);

            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            var x = (int) MathF.Floor(args.RelativePosition.X / Size.X * IconSize) + 1;
            var y = IconSize - (int) MathF.Floor(args.RelativePosition.Y / Size.Y * IconSize);
            OnIconClicked?.Invoke(new Vector2i(Math.Clamp(x, 1, IconSize), Math.Clamp(y, 1, IconSize)));
            args.Handle();
        }
    }
}
