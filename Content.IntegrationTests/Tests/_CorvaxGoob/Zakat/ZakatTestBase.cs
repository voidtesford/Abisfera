using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.StatusIcon;
using Content.Goobstation.Server._CorvaxGoob.Zakat;
using Content.Goobstation.Server.Disease;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Disease.Components;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.StatusIcon;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._CorvaxGoob.Zakat;

/// <summary>
/// Shared helpers for the «Закат» tests. The player is the disease itself.
/// </summary>
public abstract class ZakatTestBase : InteractionTest
{
    protected override string PlayerPrototype => "MobZakat";

    protected ZakatSystem ZakatSys => SEntMan.System<ZakatSystem>();
    protected ZakatContagionSystem Contagion => SEntMan.System<ZakatContagionSystem>();
    protected DiseaseSystem Disease => SEntMan.System<DiseaseSystem>();
    protected TimeSpan Now => Server.ResolveDependency<IGameTiming>().CurTime;

    protected Entity<ZakatComponent> Zakat => (SPlayer, SEntMan.GetComponent<ZakatComponent>(SPlayer));

    /// <summary>
    /// upd1: the passive spread is random, so it is off unless a test turns it on (exact flesh layouts elsewhere).
    /// </summary>
    public override async Task Setup()
    {
        await base.Setup();
        await Server.WaitPost(() => Zakat.Comp.PassiveSpreadInterval = TimeSpan.FromHours(10));
    }

    protected EntityUid Spawn(string proto, float dx = 0f, float dy = 0f)
    {
        var coords = SEntMan.GetCoordinates(TargetCoords).Offset(new System.Numerics.Vector2(dx, dy));
        return SEntMan.SpawnEntity(proto, coords);
    }

    /// <summary>
    /// Raises the free-pick action exactly like the action bar would.
    /// </summary>
    protected void Pick(EntityUid target)
    {
        var ev = new ZakatInfectActionEvent { Target = target, Performer = SPlayer };
        SEntMan.EventBus.RaiseLocalEvent(SPlayer, ev);
    }

    /// <summary>
    /// A host carrying the strain right now, without spending a pick.
    /// </summary>
    protected Entity<ZakatHostComponent> MakeHost(EntityUid uid)
    {
        Assert.That(ZakatSys.InfectForced(Zakat, uid), Is.True);
        return (uid, SEntMan.GetComponent<ZakatHostComponent>(uid));
    }

    protected bool Infected(EntityUid uid) => ZakatSys.IsInfected(Zakat.Comp, uid);

    protected DiseaseComponent HostDisease(EntityUid host)
    {
        Assert.That(Disease.TryFindDisease(host, Zakat.Comp.Genotype, out var d), Is.True, $"{SEntMan.ToPrettyString(host)} does not carry Закат");
        return SEntMan.GetComponent<DiseaseComponent>(d!.Value);
    }

    /// <summary>
    /// Time skip for one symptom timer: make it due now.
    /// </summary>
    protected void SkipTo(EntityUid host, ZakatTimer timer)
    {
        SEntMan.GetComponent<ZakatHostComponent>(host).Next[timer] = Now;
    }

    protected string? ZakatIcon(EntityUid serverUid)
    {
        var icons = CEntMan.System<StatusIconSystem>().GetStatusIcons(CEntMan.GetEntity(SEntMan.GetNetEntity(serverUid)));
        return icons.Select(i => (i as StatusIconPrototype)?.ID).FirstOrDefault(id => id != null && id.StartsWith("ZakatIcon"));
    }
}
