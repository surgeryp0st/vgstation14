using System.Numerics;
using Content.Shared._VG.Status;
using Content.Shared.Camera;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._VG.Status;

/// <summary>
///     vg13 handle_dizziness(): the dizzy player's view sways around in a circle, wider and narrower over time.
/// </summary>
public sealed class VgDizzinessSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;

    private bool _swaying;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VgStatusComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } local || !TryComp<VgStatusComponent>(local, out var status))
            return;

        // Once more after it stops, to settle the view back.
        var dizzy = status.Dizziness > 0;
        if (!dizzy && !_swaying)
            return;

        _swaying = dizzy;
        _eye.UpdateEyeOffset(local);
    }

    private void OnGetEyeOffset(Entity<VgStatusComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (ent.Owner != _player.LocalEntity || ent.Comp.Dizziness <= 0)
            return;

        args.Offset += Sway(ent.Comp.Dizziness, _timing.RealTime.TotalSeconds);
    }

    /// <summary>
    ///     vg13's sway in tiles at a time: an amplitude of dizziness * (sin(dizziness * 0.044 * t) + 1) / 70 pixels around
    ///     a circle at 0.008 * dizziness degrees per decisecond, swinging back and forth every Life() tick. BYOND's
    ///     trigonometry is in degrees and world.time in deciseconds.
    /// </summary>
    public static Vector2 Sway(float dizziness, double seconds)
    {
        var t = seconds * 10;
        var amplitude = dizziness * (Math.Sin(Deg(dizziness * 0.044 * t)) + 1) / 70;
        var swing = Math.Sin(Math.PI * seconds);
        var x = amplitude * Math.Sin(Deg(0.008 * dizziness * t)) * swing;
        var y = amplitude * Math.Cos(Deg(0.008 * dizziness * t)) * swing;
        return new Vector2((float) x, (float) y) / EyeManager.PixelsPerMeter;
    }

    private static double Deg(double degrees)
    {
        return degrees * Math.PI / 180;
    }
}
