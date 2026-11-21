using System.Linq;
using Content.Server._Lavaland.Procedural.Systems;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Lavaland;

/// <summary>
/// The ruin pool caps how many grid ruins of one limit group spawn per round, across all lavaland planets.
/// </summary>
[TestFixture]
public sealed class LavalandRuinGroupLimitTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: lavalandGridRuin
  id: TestLimitRuinA
  name: lavaland-ruin-syndicate
  path: /Maps/_Lavaland/Lavaland/ruin_shelter.yml
  priority: 1
  patchToPlanet: false
  limitGroup: TestLimitGroup

- type: lavalandGridRuin
  id: TestLimitRuinB
  name: lavaland-ruin-syndicate
  path: /Maps/_Lavaland/Lavaland/ruin_shelter.yml
  priority: 2
  patchToPlanet: false
  limitGroup: TestLimitGroup

- type: lavalandGridRuin
  id: TestLimitRuinFree
  name: lavaland-ruin-syndicate
  path: /Maps/_Lavaland/Lavaland/ruin_shelter.yml
  priority: 3
  patchToPlanet: false

- type: lavalandRuinPool
  id: TestLimitPool
  ruinDistance: 16
  minDistance: 0
  maxDistance: 80
  groupLimits:
    TestLimitGroup: 2
  gridRuins:
    TestLimitRuinA: 2
    TestLimitRuinB: 2
    TestLimitRuinFree: 1

- type: lavalandPlanet
  id: TestLimitPlanet
  name: lavaland-planet-name-lavaland
  biome: LavaLand
  restrictedRange: 96
  markers: []
  atmosphere:
    volume: 2500
    immutable: True
    temperature: 308.4523
    moles:
    - 10.524779
    - 25.10312
    - 8.21301

- type: lavalandMap
  id: TestLimitLavaland
  planet: TestLimitPlanet
  ruins: TestLimitPool
";

    [Test]
    public async Task GroupLimitPerRound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var lavaland = entMan.System<LavalandSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        int RuinGrids(EntityUid mapUid)
        {
            var count = 0;
            var query = entMan.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (uid != mapUid && xform.MapUid == mapUid)
                    count++;
            }

            return count;
        }

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CCVars.LavalandEnabled, true);
            lavaland.EnsurePreloaderMap();

            Assert.That(lavaland.SetupLavalandPlanet("TestLimitLavaland", out var first, seed: 1234), Is.True);
            // 4 group ruins requested, capped at 2, plus 1 ruin without a group.
            Assert.That(lavaland.GetRuinGroupCount("TestLimitGroup"), Is.EqualTo(2));
            Assert.That(RuinGrids(first!.Value.Owner), Is.EqualTo(3));

            // Second planet in the same round: the group is already full.
            Assert.That(lavaland.SetupLavalandPlanet("TestLimitLavaland", out var second, seed: 4321), Is.True);
            Assert.That(lavaland.GetRuinGroupCount("TestLimitGroup"), Is.EqualTo(2));
            Assert.That(RuinGrids(second!.Value.Owner), Is.EqualTo(1));

            entMan.DeleteEntity(first.Value.Owner);
            entMan.DeleteEntity(second.Value.Owner);
            cfg.SetCVar(CCVars.LavalandEnabled, false);
        });

        await pair.CleanReturnAsync();
    }
}
