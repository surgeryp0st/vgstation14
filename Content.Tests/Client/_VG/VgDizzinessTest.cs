using Content.Client._VG.Status;
using NUnit.Framework;

namespace Content.Tests.Client._VG;

[TestFixture]
public sealed class VgDizzinessTest
{
    [Test]
    public void SwayStaysWithinVg13Amplitude()
    {
        // vg13: at most dizziness * 2 / 70 pixels; 32 pixels to a tile.
        for (var t = 0.0; t < 20; t += 0.37)
        {
            Assert.That(VgDizzinessSystem.Sway(120, t).Length(), Is.LessThanOrEqualTo(120f * 2 / 70 / 32 + 0.0001f));
            Assert.That(VgDizzinessSystem.Sway(0, t).Length(), Is.Zero);
        }

        var moved = false;
        for (var t = 0.0; t < 20; t += 0.37)
        {
            moved |= VgDizzinessSystem.Sway(120, t).Length() > 0.01f;
        }
        Assert.That(moved, "a very dizzy view sways");
    }
}
