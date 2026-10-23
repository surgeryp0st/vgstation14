using System.Collections.Generic;
using System.Linq;
using Content.Shared._VG.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests.Shared._VG;

[TestFixture]
public sealed class VgReagentTranslationTest
{
    [Test]
    public void ReplacesAndMerges()
    {
        var contents = new List<ReagentQuantity>
        {
            new("Bicaridine", FixedPoint2.New(10)),
            new("Water", FixedPoint2.New(5)),
            new("bicaridine", FixedPoint2.New(2)),
            new("Epinephrine", FixedPoint2.New(3)),
        };

        VgReagentTranslation.Translate(contents);

        Assert.That(contents.Select(q => (q.Reagent.Prototype, q.Quantity.Float())), Is.EquivalentTo(new[]
        {
            ("Water", 5f),
            ("bicaridine", 12f),
            ("inaprovaline", 3f),
        }));
    }
}
