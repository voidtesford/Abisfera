using System.Linq;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._CorvaxGoob.Zakat;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// Host-only symptoms of «Закат» (host neural map). Each one is bought for a single host and fires on its own timer.
/// </summary>
public sealed partial class ZakatSymptomSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ZakatContagionSystem _contagion = default!;
    [Dependency] private SleepingSystem _sleeping = default!;
    [Dependency] private SharedMetabolizerSystem _metabolizer = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private TraumaSystem _trauma = default!;
    [Dependency] private ZakatSlaveSystem _slaves = default!;

    public static readonly TimeSpan PneumoniaBlock = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EdemaBlock = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan StuporTime = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan SeizureTime = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan SeizureSleep = TimeSpan.FromSeconds(10);
    public const float FibrosisStamina = 30f;
    public const float BloodyVomitAmount = 30f;

    /// <summary>
    /// «тахикардия»: reagents in the blood are worked off this many times slower.
    /// </summary>
    public const float TachycardiaSlowdown = 3f;

    /// <summary>
    /// «распад органов»: share of an organ's integrity lost per tick.
    /// </summary>
    public const float OrganDecayShare = 0.1f;

    public const string OrganDecayId = "ZakatOrganDecay";

    public static readonly DamageSpecifier RotBloodDamage = new()
    {
        DamageDict = new() { ["Poison"] = 5 },
    };

    private readonly List<(EntityUid Uid, ZakatTimer Timer)> _due = new();
    private readonly List<EntityUid> _sleepNow = new();
    private readonly List<EntityUid> _wakeNow = new();

    private static readonly DamageSpecifier SkinDamage = new()
    {
        DamageDict = new() { ["Piercing"] = 2, ["Slash"] = 2, ["Blunt"] = 2 },
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZakatHostComponent, RefreshStaminaCritThresholdEvent>(OnRefreshStamina);
        SubscribeLocalEvent<ZakatHostComponent, ComponentShutdown>(OnHostShutdown);
        SubscribeLocalEvent<OrganComponent, ApplyMetabolicMultiplierEvent>(OnOrganMetabolicMultiplier, after: new[] { typeof(MetabolizerSystem) });
        SubscribeLocalEvent<ZakatHemophiliaComponent, BleedModifierEvent>(OnHemophiliaBleed);
    }

    private void OnOrganMetabolicMultiplier(Entity<OrganComponent> ent, ref ApplyMetabolicMultiplierEvent args)
    {
        if (ent.Comp.Body is not { } body
            || !TryComp<MetabolizerComponent>(ent, out var metabolizer)
            || !TryComp<ZakatHostComponent>(body, out var host)
            || !host.Symptoms.Contains(ZakatSymptom.Tachycardia))
            return;

        metabolizer.UpdateIntervalMultiplier = args.Multiplier * TachycardiaSlowdown;
    }

    private void OnHemophiliaBleed(Entity<ZakatHemophiliaComponent> ent, ref BleedModifierEvent args)
    {
        args.BleedReductionAmount = 0f;
    }

    private void OnRefreshStamina(Entity<ZakatHostComponent> ent, ref RefreshStaminaCritThresholdEvent args)
    {
        if (ent.Comp.Symptoms.Contains(ZakatSymptom.LungFibrosis))
            args.ThresholdValue -= FibrosisStamina;
    }

    private void OnHostShutdown(Entity<ZakatHostComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        foreach (var symptom in ent.Comp.Symptoms.ToArray())
            RemoveSymptom(ent, symptom);
    }

    /// <summary>
    /// Takes one symptom out of a host, undoing its lasting effects.
    /// </summary>
    public void RemoveSymptom(Entity<ZakatHostComponent> ent, ZakatSymptom symptom)
    {
        if (!ent.Comp.Symptoms.Remove(symptom))
            return;

        foreach (var timer in ZakatSymptoms.Timers(symptom))
            ent.Comp.Next.Remove(timer);

        switch (symptom)
        {
            case ZakatSymptom.Pneumonia:
            case ZakatSymptom.PulmonaryEdema:
                RemComp<ZakatBreathBlockedComponent>(ent);
                break;
            case ZakatSymptom.LungFibrosis:
                RefreshStamina(ent);
                break;
            case ZakatSymptom.ImmuneSuppression:
                RemComp<PassiveRegenSuppressedComponent>(ent);
                break;
            case ZakatSymptom.Epilepsy:
                ent.Comp.SeizureSleepAt = null;
                ent.Comp.SeizureWakeAt = null;
                RemComp<ZakatBreathBlockedComponent>(ent);
                break;
            case ZakatSymptom.Tachycardia:
                _metabolizer.UpdateMetabolicMultiplier(ent);
                break;
            case ZakatSymptom.Hemophilia:
                RemComp<ZakatHemophiliaComponent>(ent);
                break;
            case ZakatSymptom.OrganDecay:
                foreach (var organ in _body.GetBodyOrgans(ent))
                    _trauma.TryRemoveOrganDamageModifier(organ.Id, ent, OrganDecayId, organ.Component);
                break;
        }
    }

    /// <summary>
    /// «порабощение»: the slave keeps only what spreads the disease.
    /// </summary>
    public void RemoveDebuffs(Entity<ZakatHostComponent> ent)
    {
        foreach (var symptom in ent.Comp.Symptoms.ToArray())
        {
            if (ZakatSymptoms.IsDebuff(symptom))
                RemoveSymptom(ent, symptom);
        }
    }

    public void AddSymptom(Entity<ZakatHostComponent> ent, ZakatSymptom symptom)
    {
        if (!ent.Comp.Symptoms.Add(symptom))
            return;

        var now = _timing.CurTime;
        foreach (var timer in ZakatSymptoms.Timers(symptom))
            ent.Comp.Next[timer] = now + ZakatSymptoms.Period(timer);

        switch (symptom)
        {
            case ZakatSymptom.LungFibrosis:
                RefreshStamina(ent);
                break;
            case ZakatSymptom.ImmuneSuppression:
                EnsureComp<PassiveRegenSuppressedComponent>(ent);
                break;
            case ZakatSymptom.Tachycardia:
                _metabolizer.UpdateMetabolicMultiplier(ent);
                break;
            case ZakatSymptom.Hemophilia:
                EnsureComp<ZakatHemophiliaComponent>(ent);
                break;
        }
    }

    /// <summary>
    /// The host got cured: everything grown in it goes away.
    /// </summary>
    public void ClearHost(Entity<ZakatHostComponent> ent)
    {
        _slaves.Release(ent);
        RemComp<ZakatHostComponent>(ent);
    }

    private void RefreshStamina(EntityUid uid)
    {
        if (!TryComp<StaminaComponent>(uid, out var stamina))
            return;

        _stamina.RefreshStaminaCritThreshold((uid, stamina));
        Dirty(uid, stamina);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var blocked = EntityQueryEnumerator<ZakatBreathBlockedComponent>();
        while (blocked.MoveNext(out var uid, out var block))
        {
            if (now >= block.Until)
                RemCompDeferred<ZakatBreathBlockedComponent>(uid);
        }

        // collect first: a symptom can infect someone (adding a host component) or kill the host
        _due.Clear();
        var query = EntityQueryEnumerator<ZakatHostComponent>();
        while (query.MoveNext(out var uid, out var host))
        {
            if (host.SeizureSleepAt is { } sleepAt && now >= sleepAt)
            {
                host.SeizureSleepAt = null;
                host.SeizureWakeAt = now + SeizureSleep;
                _sleepNow.Add(uid);
            }
            else if (host.SeizureWakeAt is { } wakeAt && now >= wakeAt)
            {
                host.SeizureWakeAt = null;
                _wakeNow.Add(uid);
            }

            foreach (var (timer, next) in host.Next)
            {
                if (now >= next)
                    _due.Add((uid, timer));
            }
        }

        foreach (var uid in _sleepNow)
        {
            if (_mobState.IsAlive(uid))
                _status.TryAddStatusEffectDuration(uid, SleepingSystem.StatusEffectForcedSleeping, SeizureSleep);
        }

        _sleepNow.Clear();

        foreach (var uid in _wakeNow)
            _sleeping.TryWaking(uid, force: true);

        _wakeNow.Clear();

        foreach (var (uid, timer) in _due)
        {
            if (!TryComp<ZakatHostComponent>(uid, out var host))
                continue;

            host.Next[timer] = now + ZakatSymptoms.Period(timer);
            if (!_mobState.IsAlive(uid))
                continue;

            host.LastFired[timer] = now;
            Fire((uid, host), timer);
        }
    }

    private void Fire(Entity<ZakatHostComponent> ent, ZakatTimer timer)
    {
        var uid = ent.Owner;
        switch (timer)
        {
            case ZakatTimer.Sneeze:
                _contagion.Sneeze(ent);
                break;
            case ZakatTimer.Cough:
                _contagion.Cough(ent);
                break;
            case ZakatTimer.Pneumonia:
                BlockBreath(uid, PneumoniaBlock);
                Popup(uid, "zakat-sym-pneumonia");
                break;
            case ZakatTimer.PulmonaryEdema:
                BlockBreath(uid, EdemaBlock);
                Popup(uid, "zakat-sym-edema");
                break;
            case ZakatTimer.Nausea:
            case ZakatTimer.Dysentery:
                _vomit.Vomit(uid);
                _contagion.VomitSplash(ent);
                break;
            case ZakatTimer.BloodyVomit:
                VomitBlood(ent);
                break;
            case ZakatTimer.Rash:
                Popup(uid, "zakat-sym-rash");
                break;
            case ZakatTimer.SkinLesions:
                _damageable.TryChangeDamage(uid, SkinDamage, ignoreResistances: true, origin: uid, canMiss: false);
                Popup(uid, "zakat-sym-skin");
                break;
            case ZakatTimer.PsychosisDrop:
                DropAll(uid);
                Popup(uid, "zakat-sym-psychosis-drop");
                break;
            case ZakatTimer.PsychosisStupor:
                _stun.TryUpdateParalyzeDuration(uid, StuporTime);
                Popup(uid, "zakat-sym-psychosis-stupor");
                break;
            case ZakatTimer.PsychosisSelfHit:
                SelfHit(uid);
                break;
            case ZakatTimer.Epilepsy:
                _stun.TryUpdateParalyzeDuration(uid, SeizureTime);
                _jitter.DoJitter(uid, SeizureTime, true, 30f, 12f);
                BlockBreath(uid, SeizureTime);
                ent.Comp.SeizureSleepAt = _timing.CurTime + SeizureTime;
                Popup(uid, "zakat-sym-epilepsy");
                break;
            case ZakatTimer.RotBlood:
                _damageable.TryChangeDamage(uid, RotBloodDamage, ignoreResistances: true, origin: uid, canMiss: false);
                Popup(uid, "zakat-sym-rotblood");
                break;
            case ZakatTimer.OrganDecay:
                DecayOrgans(uid);
                Popup(uid, "zakat-sym-organdecay");
                break;
        }
    }

    /// <summary>
    /// «распад органов»: every organ loses a tenth of its integrity.
    /// </summary>
    public void DecayOrgans(EntityUid uid)
    {
        foreach (var (organ, comp) in _body.GetBodyOrgans(uid))
        {
            var loss = comp.IntegrityCap * OrganDecayShare;
            if (!_trauma.TryChangeOrganDamageModifier(organ, loss, uid, OrganDecayId, comp))
                _trauma.TryCreateOrganDamageModifier(organ, loss, uid, OrganDecayId, comp);
        }
    }

    private void Popup(EntityUid uid, string loc)
    {
        _popup.PopupEntity(Loc.GetString(loc), uid, uid, PopupType.MediumCaution);
    }

    public void BlockBreath(EntityUid uid, TimeSpan time)
    {
        var block = EnsureComp<ZakatBreathBlockedComponent>(uid);
        var until = _timing.CurTime + time;
        if (until > block.Until)
            block.Until = until;
    }

    private void VomitBlood(Entity<ZakatHostComponent> ent)
    {
        var uid = ent.Owner;
        Popup(uid, "zakat-sym-bloody-vomit");
        if (!TryComp<BloodstreamComponent>(uid, out var blood)
            || !_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref blood.BloodSolution, out _))
            return;

        var removed = _solutions.SplitSolution(blood.BloodSolution.Value, BloodyVomitAmount);
        _puddle.TrySpillAt(uid, removed, out _);
        _contagion.VomitSplash(ent);
    }

    private void DropAll(EntityUid uid)
    {
        if (!TryComp<HandsComponent>(uid, out var hands))
            return;

        foreach (var hand in _hands.EnumerateHands((uid, hands)))
            _hands.TryDrop((uid, hands), hand, checkActionBlocker: false);
    }

    /// <summary>
    /// Hits itself with whatever is in the active hand, or with the bare hand.
    /// </summary>
    private void SelfHit(EntityUid uid)
    {
        MeleeWeaponComponent? melee = null;
        if (_hands.GetActiveItem(uid) is { } held)
            TryComp(held, out melee);
        if (melee == null)
            TryComp(uid, out melee);

        Popup(uid, "zakat-sym-psychosis-hit");
        if (melee == null || melee.Damage.Empty)
            return;

        _damageable.TryChangeDamage(uid, melee.Damage, origin: uid, canMiss: false);
        _audio.PlayPvs(melee.HitSound, uid);
    }
}
