using Content.Goobstation.Common.MartialArts;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Goobstation.Shared._CorvaxGoob.MartialArts.Components;
using Content.Goobstation.Shared._CorvaxGoob.MartialArts.Events;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;

namespace Content.Goobstation.Shared.MartialArts;

// Abisfera - «Искусство панча» (The Art of the Punch)
public partial class SharedMartialArtsSystem
{
    private static readonly SoundSpecifier PunchComboSound = new SoundPathSpecifier("/Audio/Weapons/genhit2.ogg");

    private void InitializeArtOfThePunch()
    {
        SubscribeLocalEvent<CanPerformComboComponent, ArtOfThePunchSeriesPerformedEvent>(OnPunchSeries);
        SubscribeLocalEvent<CanPerformComboComponent, ArtOfThePunchAbsCheckPerformedEvent>(OnPunchAbsCheck);
        SubscribeLocalEvent<CanPerformComboComponent, ArtOfThePunchTeethPerformedEvent>(OnPunchTeeth);

        SubscribeLocalEvent<GrantArtOfThePunchComponent, UseInHandEvent>(OnGrantCQCUse);
        SubscribeLocalEvent<GrantArtOfThePunchComponent, MapInitEvent>(OnGrantPunchMapInit);

        SubscribeLocalEvent<ArtOfThePunchComponent, ComponentStartup>(OnPunchStartup);
        SubscribeLocalEvent<ArtOfThePunchComponent, ComponentShutdown>(OnPunchShutdown);
        SubscribeLocalEvent<ArtOfThePunchComponent, GetMeleeAttackRateEvent>(OnPunchAttackRate);
    }

    #region Learning

    /// <summary>On a mob (job special, admin) the art is learned at once.</summary>
    private void OnGrantPunchMapInit(Entity<GrantArtOfThePunchComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<MobStateComponent>(ent))
            return;

        TryGrantMartialArt(ent, ent.Comp);
    }

    private void OnPunchStartup(Entity<ArtOfThePunchComponent> ent, ref ComponentStartup args)
    {
        if (_netManager.IsServer)
            _alerts.ShowAlert(ent.Owner, ent.Comp.PendulumAlert);
    }

    private void OnPunchShutdown(Entity<ArtOfThePunchComponent> ent, ref ComponentShutdown args)
    {
        if (_netManager.IsServer)
            _alerts.ClearAlert(ent.Owner, ent.Comp.PendulumAlert);
    }

    #endregion

    #region Passives

    /// <summary>Does <paramref name="uid"/> practise the art right now (known and not blocked).</summary>
    public bool KnowsArtOfThePunch(EntityUid uid)
    {
        return TryComp<MartialArtsKnowledgeComponent>(uid, out var knowledge)
               && knowledge.MartialArtsForm == MartialArtsForms.ArtOfThePunch
               && !knowledge.Blocked;
    }

    /// <summary>Unarmed for the art: the fists, or worn gloves (boxing gloves) being the melee weapon.</summary>
    private bool IsPunchWeapon(EntityUid user, EntityUid weapon)
    {
        return weapon == user || IsWornGloves(user, weapon);
    }

    /// <summary>Is the current punch of <paramref name="user"/> thrown in gloves.</summary>
    private bool IsPunchingInGloves(EntityUid user)
    {
        return _melee.TryGetWeapon(user, out var weapon, out _) && weapon != user && IsWornGloves(user, weapon);
    }

    private void OnPunchAttackRate(Entity<ArtOfThePunchComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (args.User == ent.Owner && IsPunchWeapon(ent, args.Weapon) && KnowsArtOfThePunch(ent))
            args.Multipliers *= ent.Comp.AttackRateMultiplier;
    }

    #endregion

    #region Combos

    /// <summary>«Серия панчей»: +5 Blunt; in gloves 10 stamina instead.</summary>
    private void OnPunchSeries(Entity<CanPerformComboComponent> ent, ref ArtOfThePunchSeriesPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        if (IsPunchingInGloves(ent))
            _stamina.TakeStaminaDamage(target, proto.StaminaDamage, source: ent);
        else
            DoDamage(ent, target, proto.DamageType, proto.ExtraDamage, out _);

        _audio.PlayPredicted(PunchComboSound, target, ent);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    /// <summary>«Проверка пресса»: 35 stamina, the same in gloves.</summary>
    private void OnPunchAbsCheck(Entity<CanPerformComboComponent> ent, ref ArtOfThePunchAbsCheckPerformedEvent args)
    {
        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        _stamina.TakeStaminaDamage(target, proto.StaminaDamage, source: ent);

        _audio.PlayPredicted(PunchComboSound, target, ent);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    /// <summary>
    /// «Долой зубы»: the closing «Тяжёлая атака» has to be aimed at the head (targeting doll). +8 Blunt to the head;
    /// in gloves 16 stamina instead. Knockout: if the target's total damage after this hit and the bonus is at least
    /// <see cref="ArtOfThePunchComponent.KnockoutDamage"/>, it falls asleep for <see cref="ArtOfThePunchComponent.KnockoutTime"/>
    /// (gloves too). Not aimed at the head: the combo doesn't happen (and «Серия панчей» may fire instead).
    /// </summary>
    private void OnPunchTeeth(Entity<CanPerformComboComponent> ent, ref ArtOfThePunchTeethPerformedEvent args)
    {
        if (!TryComp<TargetingComponent>(ent, out var targeting) || targeting.Target != TargetBodyPart.Head)
            return;

        if (!_proto.TryIndex(ent.Comp.BeingPerformed, out var proto)
            || !TryUseMartialArt(ent, proto, out var target, out _))
            return;

        if (IsPunchingInGloves(ent))
            _stamina.TakeStaminaDamage(target, proto.StaminaDamage, source: ent);
        else
            DoDamage(ent, target, proto.DamageType, proto.ExtraDamage, out _, TargetBodyPart.Head, canMiss: false);

        if (_netManager.IsServer
            && TryComp<ArtOfThePunchComponent>(ent, out var art)
            && TryComp<DamageableComponent>(target, out var damageable)
            && damageable.TotalDamage.Float() >= art.KnockoutDamage)
        {
            _newStatus.TryUpdateStatusEffectDuration(target, SleepingSystem.StatusEffectForcedSleeping, art.KnockoutTime);
        }

        _audio.PlayPredicted(PunchComboSound, target, ent);
        ComboPopup(ent, target, proto.ID);
        ent.Comp.LastAttacks.Clear();
    }

    #endregion
}
