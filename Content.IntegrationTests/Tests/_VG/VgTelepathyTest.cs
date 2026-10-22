using Content.Server._VG.Telepathy;
using Content.Server.Speech;
using Content.Shared._VG.Telepathy;
using Content.Shared.Examine;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VG;

[TestFixture]
public sealed class VgTelepathyTest
{
    [Test]
    public async Task GreysRememberWhoTheyHeardOrExamined()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var minds = entMan.System<SharedMindSystem>();
            var grey = entMan.SpawnEntity("MobGrey", map.GridCoords);
            var speaker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var examined = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var mindless = entMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { speaker, examined })
            {
                var mind = minds.CreateMind(null);
                minds.TransferTo(mind, mob);
            }

            // heard_before is keyed by name, as in vg13
            entMan.System<MetaDataSystem>().SetEntityName(speaker, "Speaker");
            entMan.System<MetaDataSystem>().SetEntityName(examined, "Examined");

            var telepathy = entMan.GetComponent<VgTelepathyComponent>(grey);
            Assert.That(telepathy.ActionEntity, Is.Not.Null, "gets the action");
            Assert.That(entMan.HasComponent<Content.Server.Speech.Components.ActiveListenerComponent>(grey));

            entMan.EventBus.RaiseLocalEvent(grey, new ListenEvent("hello", speaker));
            entMan.EventBus.RaiseLocalEvent(grey, new ListenEvent("hello", mindless));
            entMan.EventBus.RaiseLocalEvent(grey, new ListenEvent("hello", grey));
            Assert.That(telepathy.HeardBefore.Values, Is.EquivalentTo(new[] { speaker }), "only a mind that isn't its own");

            var ev = new ExaminedEvent(new FormattedMessage(), examined, grey, true, false);
            entMan.EventBus.RaiseLocalEvent(examined, ev);
            Assert.That(telepathy.HeardBefore.Values, Is.EquivalentTo(new[] { speaker, examined }));

            // projecting to a name nobody has, or with no text, does nothing and doesn't throw
            var ui = new VgTelepathySendMessage("Nobody", "hi");
            entMan.EventBus.RaiseLocalEvent(grey, ui);
            entMan.EventBus.RaiseLocalEvent(grey, new VgTelepathySendMessage(null, "hi"));

            // a human without the power hears nothing and remembers nobody
            Assert.That(entMan.HasComponent<VgTelepathyComponent>(speaker), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
