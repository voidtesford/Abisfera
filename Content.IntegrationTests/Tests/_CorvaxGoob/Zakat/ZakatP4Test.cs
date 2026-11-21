using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Common.Atmos;
using Content.Goobstation.Common.Body.Components;
using Content.Goobstation.Common.Temperature.Components;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Server.Virology;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Virology;
using Content.Server.Power.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Physics;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// «Закат» p4: the cure (swab, diagnoser slip, individual vaccine), «Рост 4» vines, biomass walls and doors, space-proof flesh.
/// </summary>
public sealed class ZakatP4Test : ZakatTestBase
{
    private ZakatCureSystem Cure => SEntMan.System<ZakatCureSystem>();
    private ZakatFleshSystem Flesh => SEntMan.System<ZakatFleshSystem>();
    private ZakatVineSystem Vines => SEntMan.System<ZakatVineSystem>();
    private ZakatSymptomSystem Symptoms => SEntMan.System<ZakatSymptomSystem>();
    private ItemSlotsSystem Slots => SEntMan.System<ItemSlotsSystem>();

    private Entity<MapGridComponent> _grid;
    private Vector2i _center;

    private async Task Floor(int radius = 3)
    {
        await Server.WaitPost(() =>
        {
            _grid = MapData.Grid;
            var coords = Transform.WithEntityId(SEntMan.GetCoordinates(TargetCoords), _grid);
            _center = MapSystem.TileIndicesFor(_grid, coords);
            var tile = new Tile(TileMan[Plating].TileId);
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                    MapSystem.SetTile(_grid, _center + new Vector2i(x, y), tile);
            }
        });
    }

    private EntityCoordinates At(int dx, int dy) => MapSystem.GridTileToLocal(_grid, _grid.Comp, _center + new Vector2i(dx, dy));

    private EntityUid SpawnAt(string proto, int dx, int dy) => SEntMan.SpawnEntity(proto, At(dx, dy));

    private EntityUid Birth()
    {
        ZakatSys.SetGrowth(Zakat, ZakatGrowth.BirthLevel);
        var host = SpawnAt("MobHuman", 0, 0);
        MakeHost(host);
        var ev = new ZakatBirthActionEvent { Target = host, Performer = SPlayer };
        SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
        Assert.That(Zakat.Comp.Core, Is.Not.Null);
        return Zakat.Comp.Core!.Value;
    }

    private float Total(EntityUid uid) => SEntMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float();

    private void Swab(EntityUid swab, EntityUid user, EntityUid target)
    {
        var ev = new AfterInteractEvent(user, swab, target, SEntMan.GetComponent<TransformComponent>(target).Coordinates, true);
        SEntMan.EventBus.RaiseLocalEvent(swab, ev);
    }

    private static string PaperText(IEntityManager entMan, EntityUid except = default)
    {
        var texts = new List<string>();
        var query = entMan.EntityQueryEnumerator<PaperComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var paper, out var meta))
        {
            if (meta.EntityPrototype?.ID == "DiagnosisReportPaper")
                texts.Add(paper.Content);
        }

        return string.Join("\n----\n", texts);
    }

    #region cure

    [Test]
    public async Task SwabAndDiagnoser()
    {
        EntityUid machine = default, swab = default, host = default, clean = default, doctor = default;
        await Server.WaitAssertion(() =>
        {
            host = Spawn("MobHuman", 1f);
            clean = Spawn("MobHuman", -1f);
            doctor = Spawn("MobHuman", 0f, 1f);
            MakeHost(host);
            machine = Spawn("DiseaseDiagnoser");
            SEntMan.GetComponent<ApcPowerReceiverComponent>(machine).NeedsPower = false;
            swab = Spawn("DiseaseSwab", 0f, 1f);

            Swab(swab, doctor, host);
            var zs = SEntMan.GetComponent<ZakatSwabComponent>(swab);
            Assert.That(zs.Subject, Is.EqualTo(host));
            Assert.That(zs.Zakat, Is.EqualTo(SPlayer));
            Assert.That(SEntMan.GetComponent<DiseaseSwabComponent>(swab).DiseaseUid, Is.Null,
                "the strain itself never sits in the swab, the regular virology can't read it");

            Assert.That(Slots.TryInsert(machine, VirologyMachineComponent.SwabSlotId, swab, null), Is.True);
            Assert.That(SEntMan.HasComponent<ActiveVirologyMachineComponent>(machine), Is.True);
        });

        // the diagnoser's own 5 s analysis
        await RunSeconds(7);

        await Server.WaitAssertion(() =>
        {
            var text = PaperText(SEntMan);
            Assert.That(text, Does.Contain("Обнаружено инфекционное заболевание \"ЗАКАТ\"").Or.Contain("Infectious disease \"ZAKAT\" detected"));
            Assert.That(Slots.TryGetSlot(machine, VirologyMachineComponent.SwabSlotId, out var slot) && slot.Item == null, Is.True, "the swab is ejected");

            // a clean person: nothing found
            var swab2 = Spawn("DiseaseSwab", 0f, 1f);
            Swab(swab2, doctor, clean);
            Assert.That(SEntMan.GetComponent<ZakatSwabComponent>(swab2).Zakat, Is.Null);
            Assert.That(Slots.TryInsert(machine, VirologyMachineComponent.SwabSlotId, swab2, null), Is.True);
        });

        await RunSeconds(7);
        await Server.WaitAssertion(() =>
        {
            var text = PaperText(SEntMan);
            Assert.That(text, Does.Contain("Инфекционных заболеваний не обнаружено").Or.Contain("No infectious diseases detected"));
        });
    }

    [Test]
    public async Task VaccineRollsBackAndCures()
    {
        EntityUid host = default, other = default;
        await Server.WaitAssertion(() =>
        {
            host = Spawn("MobHuman", 1f);
            other = Spawn("MobHuman", -1f);
            var doctor = Spawn("MobHuman", 0f, 1f);
            var h = MakeHost(host);
            MakeHost(other);
            Symptoms.AddSymptom(h, ZakatSymptom.Sneeze);
            Symptoms.AddSymptom(h, ZakatSymptom.Cough);
            Symptoms.AddSymptom(h, ZakatSymptom.Nausea);

            var vaccinator = Spawn("Vaccinator");
            var virology = (vaccinator, SEntMan.GetComponent<ZakatVirologyComponent>(vaccinator));

            EntityUid MakeVaccine()
            {
                var swab = Spawn("DiseaseSwab", 0f, 1f);
                Swab(swab, doctor, host);
                Assert.That(Slots.TryInsert(vaccinator, VirologyMachineComponent.SwabSlotId, swab, null), Is.True);
                // skip the 20 s analysis
                SEntMan.EventBus.RaiseLocalEvent(vaccinator, new VirologyMachineDoneEvent(true));
                var made = SEntMan.EntityQuery<ZakatVaccineComponent>().Where(v => !v.Used).Select(v => v.Owner).ToList();
                Assert.That(made, Has.Count.EqualTo(1), "one personal vaccine");
                return made[0];
            }

            var vaccine = MakeVaccine();
            var vc = SEntMan.GetComponent<ZakatVaccineComponent>(vaccine);
            Assert.That(vc.Subject, Is.EqualTo(host));

            // someone else's vaccine does nothing and is not spent
            Assert.That(Cure.TryInject((vaccine, vc), other, doctor), Is.False);
            Assert.That(vc.Used, Is.False);

            // two symptoms recede, one by one; a branch root never goes before its follower
            Assert.That(Cure.TryInject((vaccine, vc), host, doctor), Is.True);
            var treated = SEntMan.GetComponent<ZakatVaccinatedComponent>(host);
            Assert.That(treated.Rollback, Is.EqualTo(2));
            Assert.That(treated.NextAt, Is.GreaterThan(Now), "gradual");
            Cure.Step(host, treated);
            Assert.That(h.Comp.Symptoms, Has.Count.EqualTo(2));
            Cure.Step(host, treated);
            Assert.That(h.Comp.Symptoms, Has.Count.EqualTo(1));
            Assert.That(h.Comp.Symptoms, Does.Not.Contain(ZakatSymptom.Cough), "a branch rolls back from its newest symptom");
            Assert.That(ZakatSymptoms.Previous(h.Comp.Symptoms.Single()), Is.Null);
            Assert.That(Infected(host), Is.True, "rolling back is not a cure");

            // second dose: the last symptom
            var v2 = MakeVaccine();
            Assert.That(Cure.TryInject((v2, SEntMan.GetComponent<ZakatVaccineComponent>(v2)), host, doctor), Is.True);
            treated = SEntMan.GetComponent<ZakatVaccinatedComponent>(host);
            Cure.Step(host, treated);
            Assert.That(h.Comp.Symptoms, Is.Empty);
            Assert.That(Infected(host), Is.True);

            // third dose with no symptoms: cured, no immunity
            var v3 = MakeVaccine();
            Assert.That(Cure.TryInject((v3, SEntMan.GetComponent<ZakatVaccineComponent>(v3)), host, doctor), Is.True);
            treated = SEntMan.GetComponent<ZakatVaccinatedComponent>(host);
            Assert.That(treated.Cure, Is.True);
            Cure.Step(host, treated);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Infected(host), Is.False);
            Assert.That(ZakatSys.IsImmune(Zakat.Comp, host), Is.True, "like any cured host: immune (the strain sets canGainImmunity)");
            Assert.That(Infected(other), Is.True);
            ZakatSys.UpdateZakat(Zakat);
            Assert.That(SEntMan.HasComponent<ZakatHostComponent>(host), Is.False, "everything grown in the host goes");
        });
    }

    [Test]
    public async Task VaccineUselessOnSlavesAndCreatures()
    {
        await Server.WaitAssertion(() =>
        {
            var slave = Spawn("MobHuman", 1f);
            var h = MakeHost(slave);
            h.Comp.Enslaved = true;
            Assert.That(Cure.Treat(slave), Is.False);
            Assert.That(SEntMan.HasComponent<ZakatVaccinatedComponent>(slave), Is.False);

            var hunter = Spawn("MobZakatHunter", -1f);
            Flesh.SetupCreature(hunter, Zakat);
            Assert.That(Cure.Treat(hunter), Is.False);

            var healthy = Spawn("MobHuman", 0f, 1f);
            Assert.That(Cure.Treat(healthy), Is.False);
        });
    }

    #endregion

    #region vines

    [Test]
    public async Task VinesSpreadCutAndStopAtArmoredDoors()
    {
        await Floor(5);
        EntityUid outsider = default, behindArmor = default, host = default, windowUid = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            // corridor x in -1..1; airlock at (-2,0), armored door at (2,0); walls everywhere else on both columns
            for (var y = -5; y <= 5; y++)
            {
                SpawnAt(y == 0 ? "Airlock" : "WallSolid", -2, y);
                SpawnAt(y == 0 ? "HighSecDoor" : "WallSolid", 2, y);
            }

            windowUid = SpawnAt("Window", 0, 3);
            outsider = SpawnAt("MobHuman", -4, 0);
            behindArmor = SpawnAt("MobHuman", 4, 0);
            host = SpawnAt("MobHuman", 0, -2);
            MakeHost(host);

            Zakat.Comp.VinesEndRound = false;
            Assert.That(Vines.Release(Zakat), Is.True);
            Assert.That(Zakat.Comp.VinesReleased, Is.True);
            Assert.That(Zakat.Comp.Victory, Is.True);
        });

        for (var i = 0; i < 30; i++)
        {
            await Server.WaitPost(() =>
            {
                var vines = SEntMan.GetComponent<ZakatVinesComponent>(SPlayer);
                Vines.Step(Zakat, _grid, vines);
                Vines.Retry(Zakat, _grid, vines);
            });
            await RunTicks(1);
        }

        await Server.WaitAssertion(() =>
        {
            var vines = SEntMan.GetComponent<ZakatVinesComponent>(SPlayer);
            bool Covered(int x, int y) => vines.Covered.Contains(_center + new Vector2i(x, y));
            Assert.That(Covered(0, 0), Is.True);
            Assert.That(Covered(-2, 0), Is.True, "under the airlock");
            Assert.That(Covered(-4, 0), Is.True, "and on behind it");
            Assert.That(Covered(-2, 1), Is.False, "walls hold");
            Assert.That(Covered(2, 0), Is.False, "the armored door holds");
            Assert.That(Covered(4, 0), Is.False);
            Assert.That(SEntMan.Deleted(windowUid) || !SEntMan.EntityExists(windowUid), Is.True, "the window is smashed");
            Assert.That(Covered(0, 3), Is.True, "and the vines took its tile");
            Assert.That(Covered(6, 0), Is.False, "no vines in space");

            var before = (Total(outsider), Total(behindArmor), Total(host));
            Vines.Cut(_grid, _grid.Comp, vines);
            Assert.That(Total(outsider) - before.Item1, Is.EqualTo(30f).Within(0.01f), "30 Slash a second");
            Assert.That(Total(behindArmor), Is.EqualTo(before.Item2));
            Assert.That(Total(host), Is.EqualTo(before.Item3), "the flesh spares its own");
        });
    }

    [Test]
    public async Task VinesGrowthUnlockReleases()
    {
        await Floor(3);
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.VinesEndRound = false;
            Zakat.Comp.CapturedTiles = 200;
            Zakat.Comp.LivingHosts = 20;
            Zakat.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyGrowth(Zakat), Is.True);
            Assert.That(Zakat.Comp.Growth, Is.EqualTo(4));
            Assert.That(Zakat.Comp.VinesReleased, Is.True);
            Assert.That(SEntMan.EntityQuery<ZakatVineComponent>().Count(), Is.GreaterThan(0), "the vines burst out of the core");
            Assert.That(ZakatSys.Summary(SPlayer), Does.Contain("Победа Заката").Or.Contain("Zakat wins"), "the greentext is a victory");
            Assert.That(Zakat.Comp.VinesEndRound, Is.False, "the test keeps the round going");
            Assert.That(new ZakatComponent().VinesEndRound, Is.True, "a real round ends");
            Assert.That(new ZakatComponent().VineRoundRestart, Is.EqualTo(TimeSpan.FromMinutes(5)), "restart in 5 minutes, not 2");
        });
    }

    #endregion

    #region walls and doors

    [Test]
    public async Task BiomassWallsAndDoors()
    {
        await Floor(3);
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.Biomass = 100f;
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Not.Null);
            Assert.That(Flesh.TryCapture(Zakat, At(-1, 0)), Is.Not.Null);
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Wall, At(2, 2)), Is.Null, "only on its own flesh");

            var biomass = Zakat.Comp.Biomass;
            var wall = Flesh.TryBuild(Zakat, ZakatStructure.Wall, At(1, 0));
            Assert.That(wall, Is.Not.Null);
            Assert.That(biomass - Zakat.Comp.Biomass, Is.EqualTo(5f));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(wall!.Value).EntityPrototype!.ID, Is.EqualTo("ZakatBiomassWall"));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(wall.Value).Hard, Is.True);
            Assert.That(SEntMan.HasComponent<Content.Server.Atmos.Components.AirtightComponent>(wall.Value), Is.True);

            biomass = Zakat.Comp.Biomass;
            var door = Flesh.TryBuild(Zakat, ZakatStructure.Door, At(-1, 0));
            Assert.That(door, Is.Not.Null);
            Assert.That(biomass - Zakat.Comp.Biomass, Is.EqualTo(5f));
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Door, At(-1, 0)), Is.Null, "one per tile");

            var doors = SEntMan.System<SharedDoorSystem>();
            var stranger = SpawnAt("MobHuman", -2, 1);
            var host = SpawnAt("MobHuman", -2, -1);
            MakeHost(host);
            var creature = SpawnAt("MobZakatHunter", -2, 2);
            Flesh.SetupCreature(creature, Zakat);

            var dc = SEntMan.GetComponent<DoorComponent>(door!.Value);
            Assert.That(dc.State, Is.EqualTo(DoorState.Closed));
            Assert.That(doors.TryOpen(door.Value, dc, stranger), Is.False, "not for strangers");
            Assert.That(dc.State, Is.EqualTo(DoorState.Closed));
            Assert.That(doors.TryOpen(door.Value, dc, host), Is.True, "the infected pass");
            Assert.That(dc.State, Is.EqualTo(DoorState.Opening));
        });
        await RunSeconds(5);
        await Server.WaitAssertion(() =>
        {
            var door = SEntMan.EntityQuery<ZakatFleshDoorComponent>().Single().Owner;
            var dc = SEntMan.GetComponent<DoorComponent>(door);
            Assert.That(dc.State, Is.EqualTo(DoorState.Closed), "it closes behind them");
            var creature = SEntMan.EntityQuery<ZakatCreatureComponent>().Single().Owner;
            Assert.That(SEntMan.System<SharedDoorSystem>().TryOpen(door, dc, creature), Is.True, "the creatures pass");
        });
    }

    /// <summary>
    /// p4.1: walls, doors and vines are drawn by IconSmooth corners like station walls / flesh floor,
    /// every corner state they pick exists, and a vine tile in the middle of a patch is fully joined.
    /// </summary>
    [Test]
    public async Task SmoothedWallsDoorsAndVines()
    {
        await Floor(4);
        var walls = new List<NetEntity>();
        var vines = new List<NetEntity>();
        NetEntity door = default, middle = default;
        await Server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 0; x++)
                walls.Add(SEntMan.GetNetEntity(SpawnAt("ZakatBiomassWall", x, 3)));
            door = SEntMan.GetNetEntity(SpawnAt("ZakatBiomassDoor", 1, 3));
            walls.Add(SEntMan.GetNetEntity(SpawnAt("ZakatBiomassWall", 2, 3)));
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var vine = SEntMan.GetNetEntity(SpawnAt("ZakatVine", x, y));
                    vines.Add(vine);
                    if (x == 0 && y == 0)
                        middle = vine;
                }
            }
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            foreach (var net in walls.Concat(vines))
            {
                var uid = CEntMan.GetEntity(net);
                var smooth = CEntMan.GetComponent<Content.Client.IconSmoothing.IconSmoothComponent>(uid);
                var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
                var name = CEntMan.ToPrettyString(uid).ToString();
                Assert.That(smooth.Mode, Is.EqualTo(Content.Client.IconSmoothing.IconSmoothingMode.Corners), name);
                var corners = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                    .Where(l => l.State.Name?.StartsWith(smooth.StateBase) == true).ToList();
                Assert.That(corners, Has.Count.EqualTo(4), $"{name}: four corner layers");
                foreach (var layer in corners)
                    Assert.That(layer.ActualRsi!.TryGetState(layer.State, out _), Is.True, $"{name}: {layer.State.Name}");
            }

            Assert.That(CEntMan.GetComponent<Content.Client.IconSmoothing.IconSmoothComponent>(CEntMan.GetEntity(walls[0])).SmoothKey, Is.EqualTo("walls"), "joins the station walls");
            Assert.That(CEntMan.GetComponent<Content.Client.IconSmoothing.IconSmoothComponent>(CEntMan.GetEntity(door)).SmoothKey, Is.EqualTo("walls"), "walls join the door");

            // the middle vine has vine all around: every corner fully joined (state 7)
            var mid = CEntMan.GetEntity(middle);
            var midBase = CEntMan.GetComponent<Content.Client.IconSmoothing.IconSmoothComponent>(mid).StateBase;
            Assert.That(midBase, Does.Match("^v[0-9]+a$"), "a cell of the vine texture");
            var states = CEntMan.GetComponent<SpriteComponent>(mid).AllLayers.OfType<SpriteComponent.Layer>()
                .Select(l => l.State.Name).Where(n => n?.StartsWith(midBase) == true);
            Assert.That(states, Is.All.EqualTo(midBase + "7"));

            // neighbouring vine tiles use different cells of the texture
            var bases = vines.Select(v => CEntMan.GetComponent<Content.Client.IconSmoothing.IconSmoothComponent>(CEntMan.GetEntity(v)).StateBase).Distinct();
            Assert.That(bases.Count(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task SpaceProofFlesh()
    {
        await Floor(3);
        await Server.WaitAssertion(() =>
        {
            var core = Birth();
            var hunter = SpawnAt("MobZakatHunter", 1, 1);
            Flesh.SetupCreature(hunter, Zakat);
            var bioshmat = SpawnAt("MobZakatBioshmat", 1, -1);

            foreach (var mob in new[] { hunter, bioshmat })
            {
                Assert.That(SEntMan.HasComponent<SpecialPressureImmunityComponent>(mob), Is.True, SEntMan.ToPrettyString(mob).ToString());
                Assert.That(SEntMan.HasComponent<SpecialLowTempImmunityComponent>(mob), Is.True);
                Assert.That(SEntMan.HasComponent<SpecialBreathingImmunityComponent>(mob), Is.True);
            }

            Zakat.Comp.Biomass = 100f;
            Flesh.TryCapture(Zakat, At(1, 0));
            Flesh.TryCapture(Zakat, At(-1, 0));
            var wall = Flesh.TryBuild(Zakat, ZakatStructure.Wall, At(1, 0))!.Value;
            var door = Flesh.TryBuild(Zakat, ZakatStructure.Door, At(-1, 0))!.Value;
            var vine = SpawnAt("ZakatVine", 2, 2);
            foreach (var building in new[] { core, wall, door, vine })
            {
                Assert.That(SEntMan.HasComponent<SpecialPressureImmunityComponent>(building), Is.True, SEntMan.ToPrettyString(building).ToString());
                Assert.That(SEntMan.HasComponent<SpecialLowTempImmunityComponent>(building), Is.True);
            }
        });
    }

    #endregion
}
