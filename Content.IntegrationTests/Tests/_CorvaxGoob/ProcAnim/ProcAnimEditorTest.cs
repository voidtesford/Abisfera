using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.IntegrationTests.Pair;
using Content.Server._CorvaxGoob.ProcAnim;
using Content.Server.Gravity;
using Content.Shared._CorvaxGoob.ProcAnim;
using Content.Shared.Gravity;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._CorvaxGoob.ProcAnim;

/// <summary>
/// The animation editor (Tools/_CorvaxGoob/AnimEditor) side of ProcAnim: scene categories / editor names, a scene in the
/// exact format the editor writes, and the <c>procanim preview</c> / <c>procanim list</c> admin commands.
/// </summary>
[TestFixture]
public sealed class ProcAnimEditorTest
{
    private const string EditorScene = "ProcAnimTestEditorFinisher";

    // Same shape as the editor's "new finisher" template after a save (quoted editorName with ':', flow keys).
    [TestPrototypes]
    private const string Prototypes = @"
- type: procAnimScene
  id: ProcAnimTestEditorFinisher
  category: Finisher
  editorName: 'Тест: удар'
  length: 1
  requiredRoom: 0
  actors:
  - role: attacker
    facing: 1
    move:
    - {t: 0, x: 0}
    - {t: 0.4, x: 0.15, ease: In}
    - {t: 0.9, x: 0}
    parts:
      Body:
        pivot: 0, -14
        keys: [{t: 0, rot: 0}, {t: 0.25, rot: 6, ease: Out}, {t: 0.4, rot: -10, ease: In}, {t: 0.9, rot: 0}]
      ArmNear:
        pivot: 0, 5
        keys: [{t: 0, rot: 10}, {t: 0.25, rot: -40, ease: Out}, {t: 0.4, rot: 80, ease: In}, {t: 0.9, rot: 10}]
      ArmNearLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 30}, {t: 0.4, rot: 5, ease: In}, {t: 0.9, rot: 30}]
  - role: victim
    facing: -1
    move:
    - {t: 0, x: 0.8}
    - {t: 0.4, x: 0.8}
    - {t: 0.8, x: 1.1, ease: Out}
    parts:
      Body:
        pivot: 0, -14
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.8, x: 0, y: -8, rot: -90, ease: Out}]
      Head:
        pivot: 0, 6
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.5, rot: -25, ease: HitStop}, {t: 0.9, rot: 0}]
  events:
  - {t: 0.4, target: victim, beat: hit, hitStop: 0.05}
";

    /// <summary>Scenes shipped with the game and the editor list group they must show up in.</summary>
    private static readonly (string Id, ProcAnimCategory Category)[] Shipped =
    {
        ("GilbertFinisherPhase1", ProcAnimCategory.Finisher),
        ("GilbertFinisherBarrel", ProcAnimCategory.Finisher),
        ("GilbertFinisherGroundPound", ProcAnimCategory.Finisher),
        ("GilbertFinisherRage", ProcAnimCategory.Finisher),
        ("GilbertFinisherRageNeck", ProcAnimCategory.Finisher),
        ("GilbertFinisherRageHeart", ProcAnimCategory.Finisher),
        ("GilbertLeapPounce", ProcAnimCategory.Ability),
        ("ProcAnimExamplePunch", ProcAnimCategory.Example),
    };

    [Test]
    public async Task CategoriesAndEditorNames()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var (id, category) in Shipped)
            {
                var p = protos.Index<ProcAnimScenePrototype>(id);
                Assert.That(p.Category, Is.EqualTo(category), id);
                Assert.That(p.EditorName, Is.Not.Null.And.Not.Empty, id);
            }

            var editor = protos.Index<ProcAnimScenePrototype>(EditorScene);
            Assert.That(editor.Category, Is.EqualTo(ProcAnimCategory.Finisher));
            Assert.That(editor.EditorName, Is.EqualTo("Тест: удар"));
            Assert.That(ProcAnimValidation.Validate(editor), Is.Empty);

            // Unset: the default group.
            Assert.That(new ProcAnimScenePrototype().Category, Is.EqualTo(ProcAnimCategory.Other));
        });

        // The client loads the same fields (it draws the scenes).
        var clientProtos = pair.Client.ResolveDependency<IPrototypeManager>();
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientProtos.Index<ProcAnimScenePrototype>(EditorScene).EditorName, Is.EqualTo("Тест: удар"));
            Assert.That(clientProtos.Index<ProcAnimScenePrototype>("GilbertLeapPounce").Category, Is.EqualTo(ProcAnimCategory.Ability));
        });
        await pair.CleanReturnAsync();
    }

    private static async Task<TestMapData> Floor(TestPair pair)
    {
        var map = await pair.CreateTestMap();
        await pair.Server.WaitPost(() =>
        {
            var mapSys = pair.Server.System<SharedMapSystem>();
            for (var x = -4; x <= 6; x++)
            for (var y = -4; y <= 4; y++)
                mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var gravity = pair.Server.EntMan.EnsureComponent<GravityComponent>(map.Grid);
            pair.Server.System<GravitySystem>().EnableGravity(map.Grid, gravity);
        });
        return map;
    }

    private static async Task RunSeconds(TestPair pair, double seconds)
    {
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync((int) Math.Ceiling(seconds * rate) + 1);
    }

    [Test]
    public async Task PreviewSpawnsPlaysAndCleansUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await Floor(pair);
        var ent = server.EntMan;
        var preview = server.System<ProcAnimPreviewSystem>();
        var xform = server.System<SharedTransformSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var proto = protos.Index<ProcAnimScenePrototype>(EditorScene);

        var dummies = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var at = xform.ToMapCoordinates(map.GridCoords);
            Assert.That(preview.TryPreview("NoSuchScene", at, new List<string>(), out _, out var e1, out _), Is.False);
            Assert.That(e1, Is.EqualTo("procanim-error-no-proto"));
            Assert.That(preview.TryPreview(EditorScene, at, new List<string> { "NoSuchSpecies" }, out var none, out var e2, out var bad), Is.False);
            Assert.That(e2, Is.EqualTo("cmd-procanim-bad-species"));
            Assert.That(bad, Is.EqualTo("NoSuchSpecies"));
            Assert.That(none, Is.Empty);

            Assert.That(preview.TryPreview(EditorScene, at, new List<string> { "Human", "Reptilian" }, out dummies, out var err, out _), Is.True, err);
            Assert.That(dummies, Has.Count.EqualTo(2));
            Assert.That(ent.GetComponent<MetaDataComponent>(dummies[0]).EntityPrototype?.ID, Is.EqualTo("MobHuman"));
            Assert.That(ent.GetComponent<MetaDataComponent>(dummies[1]).EntityPrototype?.ID, Is.EqualTo("MobReptilian"));
            Assert.That(dummies.All(d => ent.HasComponent<ProcAnimComponent>(d)), "the scene runs on the dummies");
            Assert.That(dummies.All(d => ent.HasComponent<ProcAnimPreviewDummyComponent>(d)));
        });

        await RunSeconds(pair, ProcAnimTimeline.TotalLength(proto) + 0.2);
        await server.WaitAssertion(() =>
        {
            Assert.That(dummies.All(d => ent.EntityExists(d)), "kept for a moment after the scene");
            Assert.That(dummies.Any(d => ent.HasComponent<ProcAnimComponent>(d)), Is.False, "scene over");
        });

        await RunSeconds(pair, ProcAnimPreviewSystem.Linger.TotalSeconds + 0.3);
        await server.WaitAssertion(() =>
        {
            Assert.That(dummies.Any(d => ent.EntityExists(d)), Is.False, "dummies removed after the scene");
        });

        // Console: the list prints without errors (the test console fails the test on any WriteError).
        var console = server.ResolveDependency<IConsoleHost>();
        await server.WaitPost(() => console.ExecuteCommand("procanim list"));
        await RunSeconds(pair, 0.1);

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Pose fingerprints of every scene as the game loads them (same sampling as the editor's <c>--fingerprint</c>), for
    /// checking that the editor evaluates scenes exactly like the game. Writes to $PROCANIM_FINGERPRINT_OUT.
    /// </summary>
    [Test]
    [Explicit]
    public async Task DumpFingerprints()
    {
        var output = Environment.GetEnvironmentVariable("PROCANIM_FINGERPRINT_OUT");
        Assert.That(output, Is.Not.Null.And.Not.Empty, "set PROCANIM_FINGERPRINT_OUT");
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitPost(() =>
        {
            var res = new Dictionary<string, List<float>>();
            foreach (var proto in protos.EnumeratePrototypes<ProcAnimScenePrototype>())
            {
                if (proto.ID.Contains("Test", StringComparison.Ordinal))
                    continue;
                var list = new List<float>();
                for (var t = -proto.Intro; t <= proto.Length + 1e-4f; t += 0.05f)
                {
                    for (var a = 0; a < proto.Actors.Count; a++)
                    {
                        var m = ProcAnimTimeline.Evaluate(proto.Actors[a].Move, t);
                        list.Add(m.X);
                        list.Add(m.Y);
                        foreach (var p in ProcAnimTimeline.Solve(proto, a, t))
                        {
                            list.Add(p.X);
                            list.Add(p.Y);
                            list.Add(p.Rot);
                        }
                    }
                }

                list.Add(ProcAnimTimeline.TotalLength(proto));
                list.Add(proto.Events.Count);
                res[proto.ID] = list;
            }

            File.WriteAllText(output!, JsonSerializer.Serialize(res));
        });
        await pair.CleanReturnAsync();
    }
}
