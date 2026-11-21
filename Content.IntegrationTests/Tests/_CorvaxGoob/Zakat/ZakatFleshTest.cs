using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Shared._Starlight.CollectiveMind;
using Content.Shared.Armor;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Server.Ghost.Roles.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// «Закат» p3: «рождение» and the core, captured tiles, buildings, creatures, «освободить от оков», «преторианец».
/// </summary>
public sealed class ZakatFleshTest : ZakatTestBase
{
    private ZakatFleshSystem Flesh => SEntMan.System<ZakatFleshSystem>();
    private ZakatPraetorianSystem Praetorians => SEntMan.System<ZakatPraetorianSystem>();
    private SharedMindSystem MindSys => SEntMan.System<SharedMindSystem>();
    private MobStateSystem MobStates => SEntMan.System<MobStateSystem>();
    private MobThresholdSystem Thresholds => SEntMan.System<MobThresholdSystem>();
    private DamageableSystem Damageable => SEntMan.System<DamageableSystem>();
    private InventorySystem Inventory => SEntMan.System<InventorySystem>();
    private NpcFactionSystem Factions => SEntMan.System<NpcFactionSystem>();

    private Entity<MapGridComponent> _grid;
    private Vector2i _center;

    private float Total(EntityUid uid) => SEntMan.GetComponent<DamageableComponent>(uid).TotalDamage.Float();

    private void Hit(EntityUid uid, string type, float amount)
    {
        Damageable.TryChangeDamage(uid, new DamageSpecifier { DamageDict = { [type] = amount } }, ignoreResistances: true, canMiss: false);
    }

    /// <summary>
    /// Plating around the target tile so the flesh has room.
    /// </summary>
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

    private Entity<ZakatFleshTileComponent>? FleshAt(int dx, int dy) => Flesh.FleshAt(_grid, _center + new Vector2i(dx, dy));

    private EntityUid Human(int dx, int dy, bool mind = false)
    {
        var uid = SEntMan.SpawnEntity("MobHuman", At(dx, dy));
        if (mind)
            MindSys.TransferTo(MindSys.CreateMind(null, "host"), uid);
        return uid;
    }

    /// <summary>
    /// Рост 3 and «рождение» on a fresh host in the middle of the plating. Returns the core.
    /// </summary>
    private EntityUid Birth()
    {
        ZakatSys.SetGrowth(Zakat, ZakatGrowth.BirthLevel);
        Assert.That(Zakat.Comp.BirthActionEntity, Is.Not.Null);
        var host = Human(0, 0);
        MakeHost(host);
        var ev = new ZakatBirthActionEvent { Target = host, Performer = SPlayer };
        SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
        Assert.That(ev.Handled, Is.True);
        Assert.That(Zakat.Comp.Core, Is.Not.Null);
        return Zakat.Comp.Core!.Value;
    }

    [Test]
    public async Task BirthAndCoreLoss()
    {
        await Floor();
        EntityUid core = default;
        await Server.WaitAssertion(() =>
        {
            // no «рождение» before Рост 3
            Assert.That(Zakat.Comp.BirthActionEntity, Is.Null);
            var early = Human(1, 1);
            MakeHost(early);
            Assert.That(Flesh.TryBirth(Zakat, early), Is.Null);

            core = Birth();
            var z = Zakat.Comp;
            Assert.That(z.Born, Is.True);
            Assert.That(z.BirthActionEntity, Is.Null, "«рождение» is spent");
            Assert.That(z.FleshActionEntities, Has.Count.EqualTo(10));
            Assert.That(SEntMan.HasComponent<ZakatCoreComponent>(core), Is.True);
            Assert.That(FleshAt(0, 0), Is.Not.Null, "the core lies on its own flesh");
            Assert.That(z.ControllerEntity, Is.Not.Null);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, z.ControllerEntity!.Value), Is.True);
            Assert.That(Flesh.TryBirth(Zakat, early), Is.Null, "only one birth");
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityQuery<ZakatHostComponent>().Count(h => h.Zakat == SPlayer), Is.EqualTo(1),
                "the host burst; only the early one is left");

            // capture: side by side with active flesh, 1 per floor tile
            Zakat.Comp.Biomass = 10f;
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Not.Null);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(10f - ZakatGrowth.CaptureCost));
            Assert.That(Flesh.TryCapture(Zakat, At(3, 0)), Is.Null, "not touching the flesh");
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Null, "already flesh");
            Assert.That(Flesh.TryCapture(Zakat, At(2, 0)), Is.Not.Null, "it spreads from captured flesh");

            // the hand of flesh: a click captures the tile
            var controller = Zakat.Comp.ControllerEntity!.Value;
            var click = new AfterInteractEvent(SPlayer, controller, null, At(-1, 0), true);
            SEntMan.EventBus.RaiseLocalEvent(controller, click);
            Assert.That(click.Handled, Is.True);
            Assert.That(FleshAt(-1, 0), Is.Not.Null);

            // no biomass, no flesh
            Zakat.Comp.Biomass = 0f;
            Assert.That(Flesh.TryCapture(Zakat, At(-2, 0)), Is.Null);

            // the core dies
            Hit(core, "Blunt", 300);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var z = Zakat.Comp;
            Assert.That(SEntMan.Deleted(core), Is.True);
            Assert.That(z.Defeated, Is.True);
            Assert.That(z.Core, Is.Null);
            Assert.That(z.FleshActionEntities, Is.Empty);
            Assert.That(z.ControllerEntity, Is.Null);
            Assert.That(SEntMan.System<SharedHandsSystem>().GetHandCount(SPlayer), Is.Zero, "the hand of flesh is gone");
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (2, 0), (-1, 0) })
            {
                var flesh = FleshAt(dx, dy);
                Assert.That(flesh, Is.Not.Null);
                Assert.That(flesh!.Value.Comp.Active, Is.False, "connected flesh goes inactive");
                Assert.That(SEntMan.HasComponent<SpeedModifierContactsComponent>(flesh.Value), Is.False, "dead flesh no longer slows");
            }

            z.Biomass = 100f;
            Assert.That(Flesh.TryCapture(Zakat, At(-2, 0)), Is.Null, "a lost disease spreads no more");
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Spewer, At(1, 0)), Is.Null);
        });
    }

    [Test]
    public async Task CaptureSlowsTheHealthy()
    {
        await Floor();
        await AddGravity();
        await AddGravity(MapData.MapUid);
        EntityUid healthy = default, infected = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.Biomass = 10f;
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Not.Null);
            Assert.That(Flesh.TryCapture(Zakat, At(-1, 0)), Is.Not.Null);
            healthy = Human(1, 0);
            infected = Human(-1, 0);
            MakeHost(infected);
            Assert.That(Factions.IsMember(infected, ZakatFleshSystem.Faction), Is.True, "flesh creatures don't attack the infected");
            Assert.That(Factions.IsMember(healthy, ZakatFleshSystem.Faction), Is.False);
            Assert.That(SEntMan.HasComponent<ZakatFleshFriendComponent>(infected), Is.True);
            Assert.That(SEntMan.HasComponent<ZakatFleshFriendComponent>(healthy), Is.False);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            var h = SEntMan.GetComponent<MovementSpeedModifierComponent>(healthy);
            var i = SEntMan.GetComponent<MovementSpeedModifierComponent>(infected);
            Assert.That(h.CurrentWalkSpeed / h.BaseWalkSpeed, Is.EqualTo(0.7f).Within(0.01f));
            Assert.That(h.CurrentSprintSpeed / h.BaseSprintSpeed, Is.EqualTo(0.7f).Within(0.01f));
            Assert.That(i.CurrentWalkSpeed / i.BaseWalkSpeed, Is.EqualTo(1f).Within(0.01f), "the infected walk freely");
        });
    }

    [Test]
    public async Task SpewerIncome()
    {
        await Floor();
        EntityUid spewer = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.Biomass = 100f;
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Spewer, At(0, 0)), Is.Null, "the core takes its tile");
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Spewer, At(2, 0)), Is.Null, "only on captured tiles");
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Not.Null);
            spewer = Flesh.TryBuild(Zakat, ZakatStructure.Spewer, At(1, 0))!.Value;
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(100f - ZakatGrowth.CaptureCost - 25f));
            Assert.That(Flesh.TryBuild(Zakat, ZakatStructure.Spewer, At(1, 0)), Is.Null, "one building per tile");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(74f));

            var s = SEntMan.GetComponent<ZakatStructureComponent>(spewer);
            Assert.That(s.Income, Is.EqualTo(5f));
            Assert.That(s.Interval, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(s.NextAt, Is.GreaterThan(Now + TimeSpan.FromSeconds(29)));
            s.NextAt = Now;
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(79f));
            // a lost disease gets nothing
            Flesh.Defeat(Zakat);
            SEntMan.GetComponent<ZakatStructureComponent>(spewer).NextAt = Now;
        });
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(Zakat.Comp.Biomass, Is.EqualTo(79f)));
    }

    [Test]
    public async Task Cocoons()
    {
        await Floor();
        EntityUid small = default, hunterCocoon = default, large = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.Biomass = 1000f;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1) })
                Assert.That(Flesh.TryCapture(Zakat, At(dx, dy)), Is.Not.Null);

            small = Flesh.TryBuild(Zakat, ZakatStructure.SmallCocoon, At(1, 0))!.Value;
            hunterCocoon = Flesh.TryBuild(Zakat, ZakatStructure.HunterCocoon, At(-1, 0))!.Value;
            large = Flesh.TryBuild(Zakat, ZakatStructure.LargeCocoon, At(0, 1))!.Value;
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(1000f - 3 * ZakatGrowth.CaptureCost - 30f - 50f - 40f));
        });

        // the nest fills up to 4 and stops
        for (var i = 0; i < 6; i++)
        {
            await Server.WaitPost(() => SEntMan.GetComponent<ZakatStructureComponent>(small).NextAt = Now);
            await RunTicks(2);
        }

        await Server.WaitAssertion(() =>
        {
            var shmats = SEntMan.GetComponent<ZakatStructureComponent>(small).Spawned;
            Assert.That(shmats, Has.Count.EqualTo(4));
            foreach (var shmat in shmats)
            {
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(shmat).EntityPrototype!.ID, Is.EqualTo("MobZakatBioshmat"));
                Assert.That(SEntMan.GetComponent<ZakatCreatureComponent>(shmat).Zakat, Is.EqualTo(SPlayer));
                Assert.That(SEntMan.GetComponent<ZakatBioshmatComponent>(shmat).Zakat, Is.EqualTo(SPlayer));
                Assert.That(SEntMan.HasComponent<GhostRoleComponent>(shmat), Is.True);
                Assert.That(Factions.IsMember(shmat, ZakatFleshSystem.Faction), Is.True);
                Assert.That(SEntMan.HasComponent<CollectiveMindComponent>(shmat), Is.True, "hive mind");
            }

            SEntMan.DeleteEntity(shmats[0]);
            SEntMan.GetComponent<ZakatStructureComponent>(small).NextAt = Now;
            SEntMan.GetComponent<ZakatStructureComponent>(hunterCocoon).NextAt = Now;
            SEntMan.GetComponent<ZakatStructureComponent>(large).NextAt = Now;
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ZakatStructureComponent>(small).Spawned, Has.Count.EqualTo(4), "a dead one is replaced");

            Assert.That(SEntMan.Deleted(hunterCocoon), Is.True, "the cocoon bursts");
            Assert.That(SEntMan.Deleted(large), Is.True);
            Assert.That(FleshAt(-1, 0), Is.Not.Null, "plain flesh stays");
            Assert.That(FleshAt(0, 1), Is.Not.Null);

            var hunters = SEntMan.EntityQuery<ZakatHunterComponent>().Select(h => h.Owner).ToList();
            Assert.That(hunters, Has.Count.EqualTo(1));
            var hunter = hunters[0];
            Assert.That(Thresholds.TryGetThresholdForState(hunter, MobState.Dead, out var dead), Is.True);
            Assert.That(dead!.Value.Float(), Is.EqualTo(150f));
            var melee = SEntMan.GetComponent<MeleeWeaponComponent>(hunter);
            Assert.That(melee.Damage.DamageDict["Slash"].Float(), Is.EqualTo(20f));
            Assert.That(melee.AttackRate, Is.EqualTo(1f));
            Assert.That(SEntMan.GetComponent<ZakatHunterComponent>(hunter).DevourAction, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<GhostRoleComponent>(hunter), Is.True);
            Assert.That(SEntMan.GetComponent<ZakatCreatureComponent>(hunter).Zakat, Is.EqualTo(SPlayer));

            var protos = new[] { "MobZakatFleshWalker", "MobZakatFleshBrute", "MobZakatFleshStalker" };
            var bigs = SEntMan.EntityQuery<ZakatCreatureComponent>()
                .Select(c => c.Owner)
                .Where(u => protos.Contains(SEntMan.GetComponent<MetaDataComponent>(u).EntityPrototype?.ID))
                .ToList();
            Assert.That(bigs, Has.Count.EqualTo(1));
            Assert.That(Thresholds.TryGetThresholdForState(bigs[0], MobState.Dead, out var bigDead), Is.True);
            Assert.That(bigDead!.Value.Float(), Is.EqualTo(100f));
            Assert.That(SEntMan.HasComponent<GhostRoleComponent>(bigs[0]), Is.True);
            Assert.That(Factions.IsMember(bigs[0], ZakatFleshSystem.Faction), Is.True);
        });
    }

    /// <summary>upd2: <c>zakatdebugmass</c> adds and takes biomass, never below zero.</summary>
    [Test]
    public async Task DebugMassCommand()
    {
        await Floor();
        var console = Server.ResolveDependency<Robust.Server.Console.IServerConsoleHost>();
        await Server.WaitAssertion(() =>
        {
            var cmd = console.AvailableCommands["zakatdebugmass"];
            var id = SEntMan.GetNetEntity(SPlayer).ToString();
            Zakat.Comp.Biomass = 10f;

            var shell = new ZakatTestShell(console);
            cmd.Execute(shell, "", new[] { id, "15.5" });
            Assert.That(shell.Errors, Is.Empty);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(25.5f));

            cmd.Execute(shell, "", new[] { id, "-5" });
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(20.5f));
            cmd.Execute(shell, "", new[] { id, "-100" });
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(0f), "clamped at zero");

            shell = new ZakatTestShell(console);
            var stranger = Human(3, 3);
            cmd.Execute(shell, "", new[] { SEntMan.GetNetEntity(stranger).ToString(), "5" });
            cmd.Execute(shell, "", new[] { id, "lots" });
            cmd.Execute(shell, "", new[] { id });
            Assert.That(shell.Errors, Has.Count.EqualTo(3));
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(0f));

            var hint = cmd.GetCompletion(shell, new[] { "" });
            Assert.That(hint.Options.Select(o => o.Value), Does.Contain(id));
        });
    }

    [Test]
    public async Task Liberate()
    {
        await Floor();
        EntityUid host = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            var stranger = Human(-3, 3);
            Assert.That(Flesh.TryLiberate(Zakat, stranger), Is.False, "only own hosts");

            host = Human(0, 3);
            MakeHost(host);
            // upd2: a host that isn't a hive slave has no shackles to break
            Zakat.Comp.Biomass = 5f;
            Assert.That(Flesh.TryLiberate(Zakat, host), Is.False, "only hive slaves");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(5f));
            Assert.That(FleshAt(0, 3), Is.Null, "a non-slave was liberated");
            Assert.That(SEntMan.EntityExists(host) && !SEntMan.IsQueuedForDeletion(host), Is.True);
            Zakat.Comp.Biomass = ZakatGrowth.EnslaveCost;
            Assert.That(ZakatSys.TryEnslave(Zakat, host), Is.True);
            Zakat.Comp.Biomass = 5f;
            var ev = new ZakatLiberateActionEvent { Target = host, Performer = SPlayer };
            SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
            Assert.That(ev.Handled, Is.True);
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(5f + ZakatGrowth.LiberateBiomass));
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = 2; dy <= 3; dy++)
                {
                    var flesh = FleshAt(dx, dy);
                    Assert.That(flesh, Is.Not.Null, $"{dx},{dy}");
                    Assert.That(flesh!.Value.Comp.Active, Is.True);
                    Assert.That(flesh.Value.Comp.Zakat, Is.EqualTo(SPlayer));
                }
            }

            Assert.That(FleshAt(0, 1), Is.Null, "exactly 3x2");
            Assert.That(FleshAt(2, 3), Is.Null);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(host), Is.True, "the host burst"));
    }

    [Test]
    public async Task Praetorian()
    {
        await Floor();
        await AddAtmosphere();
        EntityUid praet = default;
        float before = 0f;
        await Server.WaitAssertion(() =>
        {
            Birth();
            praet = Human(1, 0, mind: true);
            var host = MakeHost(praet);
            Zakat.Comp.Biomass = ZakatGrowth.PraetorianCost - 1f;
            Assert.That(Flesh.TryMakePraetorian(Zakat, praet), Is.False);
            Zakat.Comp.Biomass = ZakatGrowth.PraetorianCost;
            Assert.That(Flesh.TryMakePraetorian(Zakat, praet), Is.True);
            Assert.That(Zakat.Comp.Biomass, Is.Zero);
            Assert.That(host.Comp.Enslaved, Is.True);

            var p = SEntMan.GetComponent<ZakatPraetorianComponent>(praet);
            Assert.That(p.AbsorbAction, Is.Not.Null);
            Assert.That(p.ShiftAction, Is.Not.Null);
            Assert.That(p.ClawsAction, Is.Not.Null);
            Assert.That(Thresholds.TryGetThresholdForState(praet, MobState.Dead, out var dead), Is.True);
            Assert.That(dead!.Value.Float(), Is.EqualTo(400f));

            // no more upgrades
            Zakat.Comp.Biomass = 1000f;
            Assert.That(ZakatSys.TryBuyMutation(Zakat, praet, ZakatMutation.NoPain), Is.False);
            Assert.That(Flesh.TryMakePraetorian(Zakat, praet), Is.False);

            var move = SEntMan.GetComponent<MovementSpeedModifierComponent>(praet);
            Assert.That(move.CurrentSprintSpeed, Is.EqualTo(move.BaseSprintSpeed + 2f).Within(0.05f));

            // claws
            var melee = SEntMan.System<SharedMeleeWeaponSystem>();
            Assert.That(melee.GetDamage(praet, praet).DamageDict.TryGetValue("Slash", out var bare) ? bare.Float() : 0f, Is.LessThan(25f));
            Praetorians.ToggleClaws(praet);
            Assert.That(SEntMan.HasComponent<ZakatPraetorianClawsComponent>(praet), Is.True);
            Assert.That(melee.GetDamage(praet, praet).DamageDict["Slash"].Float(), Is.EqualTo(25f));
            Praetorians.ToggleClaws(praet);
            Assert.That(SEntMan.HasComponent<ZakatPraetorianClawsComponent>(praet), Is.False);

            // no crit: well past a human's critical threshold it still stands
            Hit(praet, "Poison", 250);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MobStates.IsAlive(praet), Is.True);
            before = Total(praet);
            Assert.That(before, Is.GreaterThan(200f));
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(praet), Is.LessThan(before - 3f), "regeneration 2/s");
            Hit(praet, "Poison", -1000);
        });

        // swallowing a corpse and wearing its look
        EntityUid victim = default, second = default;
        await Server.WaitAssertion(() =>
        {
            victim = Human(2, 0);
            SEntMan.System<MetaDataSystem>().SetEntityName(victim, "Иван Петров");
            var suit = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", At(2, 0));
            var armor = SEntMan.SpawnEntity("ClothingOuterArmorBasic", At(2, 0));
            Assert.That(Inventory.TryEquip(victim, suit, "jumpsuit", true, true), Is.True);
            Assert.That(Inventory.TryEquip(victim, armor, "outerClothing", true, true), Is.True);

            var p = (praet, SEntMan.GetComponent<ZakatPraetorianComponent>(praet));
            Assert.That(Praetorians.TryStartAbsorb(p, victim), Is.False, "only corpses");
            Hit(victim, "Poison", 400);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(MobStates.IsDead(victim), Is.True);
            var p = (praet, SEntMan.GetComponent<ZakatPraetorianComponent>(praet));
            Assert.That(Praetorians.CanAbsorb(praet, victim), Is.True);
            var ownName = SEntMan.GetComponent<MetaDataComponent>(praet).EntityName;
            Praetorians.Absorb(p, victim);
            Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(victim), Is.True, "swallowed");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo(ownName), "the look changed by itself");
            Shapeshift(praet);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo("Иван Петров"));

            Assert.That(Inventory.TryGetSlotEntity(praet, "jumpsuit", out var suit), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(suit!.Value).EntityPrototype!.ID, Is.EqualTo("ClothingUniformJumpsuitColorGrey"));
            Assert.That(SEntMan.HasComponent<ZakatLookClothingComponent>(suit.Value), Is.True);
            Assert.That(Inventory.TryGetSlotEntity(praet, "outerClothing", out var armor), Is.True);
            Assert.That(SEntMan.HasComponent<ArmorComponent>(armor!.Value), Is.False, "copied clothes have no stats");
            Assert.That(Inventory.TryUnequip(praet, "outerClothing", true), Is.False, "can't be taken off");
            Assert.That(Inventory.TryUnequip(praet, "jumpsuit", true), Is.False);

            // a second look; the action cycles between them
            second = Human(2, 0);
            SEntMan.System<MetaDataSystem>().SetEntityName(second, "Анна Смирнова");
            Hit(second, "Poison", 400);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            var p = (praet, SEntMan.GetComponent<ZakatPraetorianComponent>(praet));
            Praetorians.Absorb(p, second);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo("Иван Петров"), "the look changed by itself");
            Shapeshift(praet);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo("Анна Смирнова"));
            Assert.That(Inventory.TryGetSlotEntity(praet, "jumpsuit", out _), Is.False, "she wore nothing");
            // the shift action only picks the next look
            var shift = new ZakatShiftActionEvent { Performer = praet };
            SEntMan.EventBus.RaiseLocalEvent(praet, shift);
            Assert.That(shift.Handled, Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo("Анна Смирнова"));
            Shapeshift(praet);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo("Иван Петров"));
            Assert.That(Inventory.TryGetSlotEntity(praet, "jumpsuit", out _), Is.True);
        });
    }

    private void Shapeshift(EntityUid praet)
    {
        var ev = new ZakatTransformActionEvent { Performer = praet };
        SEntMan.EventBus.RaiseLocalEvent(praet, ev);
        Assert.That(ev.Handled, Is.True);
    }

    /// <summary>upd1: the absorption runs in stages (tentacles out, seize, draw in) and only then swallows the body.</summary>
    [Test]
    public async Task PraetorianAbsorbsInStages()
    {
        await Floor();
        EntityUid praet = default, victim = default;
        string ownName = "";
        await Server.WaitAssertion(() =>
        {
            Birth();
            praet = Human(0, 0);
            Praetorians.Make(praet, Zakat);
            ownName = SEntMan.GetComponent<MetaDataComponent>(praet).EntityName;
            victim = Human(1, 0);
            Hit(victim, "Poison", 400);
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            var p = (praet, SEntMan.GetComponent<ZakatPraetorianComponent>(praet));
            Assert.That(Praetorians.TryStartAbsorb(p, victim), Is.True);
            Assert.That(p.Item2.AbsorbEffects, Is.Not.Empty, "no tentacles burst out");
        });
        await RunSeconds(2.5f);
        await Server.WaitAssertion(() =>
        {
            var p = SEntMan.GetComponent<ZakatPraetorianComponent>(praet);
            Assert.That(p.Absorbed, Is.Empty, "swallowed too early");
            Assert.That(p.AbsorbEffects.Count, Is.GreaterThanOrEqualTo(2), "the tentacles don't reach the body");
        });
        await RunSeconds(4f);
        await Server.WaitAssertion(() =>
        {
            var p = SEntMan.GetComponent<ZakatPraetorianComponent>(praet);
            Assert.That(p.Absorbed, Is.EquivalentTo(new[] { victim }), "not swallowed");
            Assert.That(p.AbsorbEffects, Is.Empty);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(praet).EntityName, Is.EqualTo(ownName), "the look changed by itself");
        });
    }

    /// <summary>
    /// upd1: walls, windows and airlocks are struck (blob-style), never covered; armored doors hold;
    /// the passive spread takes only free floor.
    /// </summary>
    [Test]
    public async Task FleshBreaksWallsAndSpreadsPassively()
    {
        await Floor();
        EntityUid wall = default, airlock = default, armored = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            wall = SEntMan.SpawnEntity("WallSolid", At(1, 0));
            airlock = SEntMan.SpawnEntity("Airlock", At(-1, 0));
            armored = SEntMan.SpawnEntity("HighSecDoor", At(0, 1));
            Zakat.Comp.Biomass = 100f;

            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Null);
            Assert.That(FleshAt(1, 0), Is.Null, "flesh grew over a wall");
            Assert.That(Total(wall), Is.GreaterThan(0f), "the wall wasn't struck");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(100f - ZakatGrowth.AttackCost));

            // one blow per cooldown
            Assert.That(Flesh.TryCapture(Zakat, At(-1, 0)), Is.Null);
            Assert.That(Total(airlock), Is.Zero, "struck again before the cooldown");

            Assert.That(Flesh.TryCapture(Zakat, At(0, 1)), Is.Null);
            Assert.That(Total(armored), Is.Zero, "an armored door was struck");
            Assert.That(FleshAt(0, 1), Is.Null);
        });
        await RunSeconds(1.2f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Flesh.TryCapture(Zakat, At(-1, 0)), Is.Null);
            Assert.That(Total(airlock), Is.GreaterThan(0f), "the airlock wasn't struck");
            Assert.That(FleshAt(-1, 0), Is.Null);
        });

        // the passive spread: free floor only, nothing struck, nothing paid
        float wallDamage = 0f, biomass = 0f;
        await Server.WaitPost(() =>
        {
            wallDamage = Total(wall);
            biomass = Zakat.Comp.Biomass;
            Zakat.Comp.PassiveSpreadInterval = TimeSpan.FromSeconds(0.1);
            Zakat.Comp.NextPassiveSpread = TimeSpan.Zero;
        });
        await RunSeconds(4f);
        await Server.WaitAssertion(() =>
        {
            var flesh = SEntMan.EntityQuery<ZakatFleshTileComponent>().Count(f => f.Zakat == SPlayer);
            Assert.That(flesh, Is.GreaterThan(3), "the flesh didn't spread by itself");
            Assert.That(FleshAt(1, 0), Is.Null);
            Assert.That(FleshAt(-1, 0), Is.Null);
            Assert.That(FleshAt(0, 1), Is.Null);
            Assert.That(Total(wall), Is.EqualTo(wallDamage), "the passive spread struck a wall");
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(biomass), "the passive spread costs biomass");
        });
    }

    /// <summary>upd1: «изрыгатель спор» lets out 10 mol of «вирусный газ заката» every 10 s; breathing it infects.</summary>
    [Test]
    public async Task SporeSpewer()
    {
        await Server.WaitPost(() =>
        {
            if (!SEntMan.HasComponent<Content.Server.Atmos.Components.GridAtmosphereComponent>(MapData.Grid))
                SEntMan.AddComponent<Content.Server.Atmos.Components.GridAtmosphereComponent>(MapData.Grid);
        });
        await Floor();
        await AddAtmosphere();
        await RunTicks(10);
        EntityUid spewer = default, victim = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            Zakat.Comp.Biomass = 100f;
            Assert.That(Flesh.TryCapture(Zakat, At(1, 0)), Is.Not.Null);
            spewer = Flesh.TryBuild(Zakat, ZakatStructure.SporeSpewer, At(1, 0))!.Value;
            Assert.That(Zakat.Comp.Biomass, Is.EqualTo(100f - ZakatGrowth.CaptureCost - 35f));
            var s = SEntMan.GetComponent<ZakatStructureComponent>(spewer);
            Assert.That(s.GasMoles, Is.EqualTo(10f));
            Assert.That(s.Interval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(s.Gas, Is.EqualTo(Gas.ZakatSpores));
            s.NextAt = Now;
            victim = Human(-2, -2);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var atmos = SEntMan.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
            var total = 0f;
            for (var x = -3; x <= 3; x++)
            {
                for (var y = -3; y <= 3; y++)
                {
                    if (atmos.GetTileMixture(_grid.Owner, null, _center + new Vector2i(x, y)) is { } mix)
                        total += mix.GetMoles(Gas.ZakatSpores);
                }
            }
            Assert.That(total, Is.InRange(5f, 10.5f), "no spores let out");

            Assert.That(Infected(victim), Is.False);
            Assert.That(Contagion.TrySporeInfect(victim, 1f), Is.True, "the spores didn't infect");
            Assert.That(Infected(victim), Is.True);
        });
    }

    [Test]
    public async Task HunterDevours()
    {
        await Floor();
        EntityUid hunter = default, prey = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            hunter = SEntMan.SpawnEntity("MobZakatHunter", At(1, 0));
            Flesh.SetupCreature(hunter, Zakat);
            prey = Human(2, 0);
            var shmat = SEntMan.SpawnEntity("MobZakatBioshmat", At(-1, 0));
            Flesh.SetupCreature(shmat, Zakat);
            Assert.That(Praetorians.CanDevour(hunter, shmat), Is.False, "not its own kind");
            Assert.That(Praetorians.CanDevour(hunter, prey), Is.True);

            Hit(hunter, "Slash", 100);
            var h = (hunter, SEntMan.GetComponent<ZakatHunterComponent>(hunter));
            Praetorians.Devour(h, prey);
            Assert.That(Total(hunter), Is.EqualTo(30f).Within(0.01f));
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(prey), Is.True));
    }

    /// <summary>
    /// Every creature of the flesh (nest ones, the parasite-heart биошмат, a grown flesh body, the praetorian)
    /// counts as infected: red icon, never healthy, never a new host, flesh friend and hive mind.
    /// </summary>
    [Test]
    public async Task CreaturesCountAsInfected()
    {
        await Floor();
        await AddAtmosphere();
        var creatures = new List<EntityUid>();
        EntityUid praet = default, slave = default, healthy = default, source = default;
        await Server.WaitAssertion(() =>
        {
            Birth();
            foreach (var (proto, dx) in new[] { ("MobZakatBioshmat", -2), ("MobZakatHunter", -1), ("MobZakatFleshWalker", 1), ("MobZakatFleshBrute", 2), ("MobZakatFleshStalker", 3) })
            {
                var uid = SEntMan.SpawnEntity(proto, At(dx, 2));
                Flesh.SetupCreature(uid, Zakat);
                creatures.Add(uid);
            }

            praet = Human(-1, -1, mind: true);
            MakeHost(praet);
            Zakat.Comp.Biomass = ZakatGrowth.PraetorianCost;
            Assert.That(Flesh.TryMakePraetorian(Zakat, praet), Is.True);

            // a slave with a parasite heart, killed: its биошмат is a creature of the hive too
            slave = Human(-2, -1, mind: true);
            MakeHost(slave);
            Zakat.Comp.Biomass = ZakatGrowth.EnslaveCost;
            Assert.That(ZakatSys.TryEnslave(Zakat, slave), Is.True);
            Zakat.Comp.Biomass = 100f;
            Assert.That(ZakatSys.TryBuyMutation(Zakat, slave, ZakatMutation.ParasiteHeart), Is.True);
            Hit(slave, "Poison", 400);

            healthy = Human(3, -1);
            source = Human(-3, -2);
            MakeHost(source);
        });
        await RunTicks(3);

        EntityUid heartShmat = default;
        await Server.WaitAssertion(() =>
        {
            heartShmat = SEntMan.EntityQuery<ZakatBioshmatComponent>().Select(b => b.Owner).Single(u => !creatures.Contains(u));
            Assert.That(SEntMan.GetComponent<ZakatCreatureComponent>(heartShmat).Zakat, Is.EqualTo(SPlayer));
            creatures.Add(heartShmat);

            // the heart shmat grows into a flesh body of its own
            SEntMan.GetComponent<ZakatBioshmatComponent>(heartShmat).GrowAt = Now;
        });
        await RunTicks(3);

        EntityUid grown = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(heartShmat) || !SEntMan.EntityExists(heartShmat), Is.True);
            creatures.Remove(heartShmat);
            grown = SEntMan.EntityQuery<ZakatSlaveComponent>().Select(s => s.Owner)
                .Single(u => u != praet && u != slave);
            ZakatSys.UpdateZakat(Zakat);

            var z = Zakat.Comp;
            Assert.That(z.HealthyCount, Is.EqualTo(1), "only the plain human is healthy");
            Assert.That(z.Infected, Does.Not.Contain(SEntMan.GetNetEntity(healthy)));
            foreach (var uid in creatures.Append(praet).Append(grown))
            {
                var name = SEntMan.ToPrettyString(uid).ToString();
                Assert.That(Infected(uid), Is.True, name);
                Assert.That(z.Infected, Does.Contain(SEntMan.GetNetEntity(uid)), name);
                Assert.That(SEntMan.HasComponent<ZakatFleshFriendComponent>(uid), Is.True, $"{name}: no slowdown on own flesh");
                Assert.That(Factions.IsMember(uid, ZakatFleshSystem.Faction), Is.True, name);
                var mind = SEntMan.GetComponent<CollectiveMindComponent>(uid);
                Assert.That(mind.Channels, Does.Contain(new Robust.Shared.Prototypes.ProtoId<CollectiveMindPrototype>(z.HiveMind)), $"{name}: hive mind");
            }

            foreach (var uid in creatures)
            {
                var name = SEntMan.ToPrettyString(uid).ToString();
                // never a new host: no pick, no contagion, no forced infection, no host map
                Assert.That(ZakatSys.InfectForced(Zakat, uid), Is.False, name);
                Assert.That(SEntMan.HasComponent<ZakatHostComponent>(uid), Is.False, name);
                Assert.That(Contagion.GetSpreadProbability((source, SEntMan.GetComponent<ZakatHostComponent>(source)), uid, ZakatContagionSystem.Aerial, 1f), Is.Zero, name);
                Zakat.Comp.StartPicksLeft = 1;
                Assert.That(ZakatSys.TryStartPick(Zakat, uid), Is.False, name);
            }

            // the hunter does not eat its own
            var hunter = creatures[1];
            Assert.That(Praetorians.CanDevour(hunter, praet), Is.False);
            Assert.That(Praetorians.CanDevour(hunter, creatures[2]), Is.False);
            Assert.That(Praetorians.CanDevour(hunter, healthy), Is.True);
        });

        // the disease sees them all as red
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            foreach (var uid in creatures.Append(praet).Append(grown))
                Assert.That(ZakatIcon(uid), Is.EqualTo("ZakatIconInfected"), SEntMan.ToPrettyString(uid).ToString());
            Assert.That(ZakatIcon(healthy), Is.EqualTo("ZakatIconHealthy"));
        });
    }
}

internal sealed class ZakatTestShell(Robust.Shared.Console.IConsoleHost host) : Robust.Shared.Console.IConsoleShell
{
    public readonly List<string> Lines = new();
    public readonly List<string> Errors = new();
    public Robust.Shared.Console.IConsoleHost ConsoleHost { get; } = host;
    public bool IsLocal => true;
    public bool IsServer => true;
    public Robust.Shared.Player.ICommonSession? Player => null;
    public void ExecuteCommand(string command) => ConsoleHost.ExecuteCommand(command);
    public void RemoteExecuteCommand(string command) { }
    public void WriteLine(string text) => Lines.Add(text);
    public void WriteLine(Robust.Shared.Utility.FormattedMessage message) => Lines.Add(message.ToString());
    public void WriteError(string text) => Errors.Add(text);
    public void Clear() { }
}
