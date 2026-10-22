using Content.Shared._VG.Fire;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._VG;

[TestFixture]
public sealed class VgHeatColorTest
{
    // Worked out from vg13's heat2color() in code/__HELPERS/type2type.dm.
    [TestCase(293.15f, 255, 0, 0)]
    [TestCase(1000f, 255, 68, 0)]
    [TestCase(1500f, 255, 108, 0)]
    [TestCase(3000f, 255, 177, 110)]
    [TestCase(10000f, 202, 218, 255)]
    public void MatchesVg13(float kelvin, int r, int g, int b)
    {
        var color = VgHeatColor.FromTemperature(kelvin);
        Assert.That((color.RByte, color.GByte, color.BByte), Is.EqualTo(((byte) r, (byte) g, (byte) b)));
    }
}
