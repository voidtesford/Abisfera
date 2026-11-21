// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Reflection;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Body.Systems;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Tag;
using Content.Shared.Traits;
using Content.Shared.Traits.Assorted.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;
using JetBrains.Annotations;

namespace Content.Server.Traits;

/// <summary>Adds components only when they are not already present.</summary>
[UsedImplicitly]
public sealed partial class TraitAddComponent : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry Components { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        foreach (var entry in Components.Values)
        {
            if (entityManager.HasComponent(uid, entry.Component.GetType()))
                continue;

            var comp = (Component) serializationManager.CreateCopy(entry.Component, notNullableOverride: true);
            entityManager.AddComponent(uid, comp);
        }
    }
}

/// <summary>Replaces existing components of the same type, or adds them if missing.</summary>
[UsedImplicitly]
public sealed partial class TraitReplaceComponent : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry Components { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        entityManager.AddComponents(uid, Components, removeExisting: true);
    }
}

/// <summary>Removes the listed component types from the entity.</summary>
[UsedImplicitly]
public sealed partial class TraitRemoveComponent : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry Components { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        foreach (var (name, _) in Components)
            entityManager.RemoveComponent(uid, factory.GetComponent(name).GetType());
    }
}

[UsedImplicitly]
public sealed partial class TraitAddActions : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<EntProtoId> Actions { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var actionSystem = entityManager.System<SharedActionsSystem>();
        foreach (var id in Actions)
        {
            EntityUid? actionId = null;
            if (actionSystem.AddAction(uid, ref actionId, id))
                actionSystem.StartUseDelay(actionId);
        }
    }
}

[UsedImplicitly]
public sealed partial class TraitAddImplant : TraitFunction
{
    [DataField]
    public HashSet<EntProtoId> Implants { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        entityManager.System<SharedSubdermalImplantSystem>().AddImplants(uid, Implants);
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyLanguages : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<string>? LanguagesSpoken { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<string>? LanguagesUnderstood { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<string>? RemoveLanguagesSpoken { get; private set; }

    [DataField, AlwaysPushInheritance]
    public List<string>? RemoveLanguagesUnderstood { get; private set; }

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var language = entityManager.System<LanguageSystem>();

        if (RemoveLanguagesSpoken is not null)
        {
            foreach (var lang in RemoveLanguagesSpoken)
                language.RemoveLanguage(uid, lang, true, false);
        }

        if (RemoveLanguagesUnderstood is not null)
        {
            foreach (var lang in RemoveLanguagesUnderstood)
                language.RemoveLanguage(uid, lang, false, true);
        }

        if (LanguagesSpoken is not null)
        {
            foreach (var lang in LanguagesSpoken)
                language.AddLanguage(uid, lang, true, false);
        }

        if (LanguagesUnderstood is not null)
        {
            foreach (var lang in LanguagesUnderstood)
                language.AddLanguage(uid, lang, false, true);
        }
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyFactions : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<string> RemoveFactions { get; private set; } = new();

    [DataField, AlwaysPushInheritance]
    public List<string> AddFactions { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var factionSystem = entityManager.System<NpcFactionSystem>();
        foreach (var faction in RemoveFactions)
            factionSystem.RemoveFaction(uid, faction);
        foreach (var faction in AddFactions)
            factionSystem.AddFaction(uid, faction);
    }
}

[UsedImplicitly]
public sealed partial class TraitPushDescription : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<DescriptionExtension> DescriptionExtensions { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var descComp = entityManager.EnsureComponent<ExtendDescriptionComponent>(uid);
        descComp.DescriptionList.AddRange(DescriptionExtensions);
    }
}

[UsedImplicitly]
public sealed partial class TraitAddArmor : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<string> DamageModifierSets { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var buff = entityManager.EnsureComponent<DamageProtectionBuffComponent>(uid);
        foreach (var id in DamageModifierSets)
        {
            if (proto.TryIndex<DamageModifierSetPrototype>(id, out var set))
                buff.Modifiers[id] = set;
        }
    }
}

[UsedImplicitly]
public sealed partial class TraitRemoveArmor : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<string> DamageModifierSets { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        if (!entityManager.TryGetComponent(uid, out DamageProtectionBuffComponent? buff))
            return;

        foreach (var id in DamageModifierSets)
            buff.Modifiers.Remove(id);

        if (buff.Modifiers.Count == 0)
            entityManager.RemoveComponent<DamageProtectionBuffComponent>(uid);
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyMobThresholds : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public int CritThresholdModifier;

    [DataField, AlwaysPushInheritance]
    public int DeadThresholdModifier;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var thresholds = entityManager.System<MobThresholdSystem>();
        if (CritThresholdModifier != 0
            && thresholds.TryGetThresholdForState(uid, MobState.Critical, out var crit))
        {
            thresholds.SetMobStateThreshold(uid, crit.Value + CritThresholdModifier, MobState.Critical);
        }

        if (DeadThresholdModifier != 0
            && thresholds.TryGetThresholdForState(uid, MobState.Dead, out var dead))
        {
            thresholds.SetMobStateThreshold(uid, dead.Value + DeadThresholdModifier, MobState.Dead);
        }
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyStamina : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public float StaminaModifier;

    [DataField, AlwaysPushInheritance]
    public float DecayModifier;

    [DataField, AlwaysPushInheritance]
    public float CooldownModifier;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        if (!entityManager.TryGetComponent(uid, out StaminaComponent? stamina))
            return;

        if (StaminaModifier != 0)
        {
            stamina.CritThreshold += StaminaModifier;
            stamina.BaseCritThreshold += StaminaModifier;
        }

        if (DecayModifier != 0)
            stamina.Decay += DecayModifier;

        if (CooldownModifier != 0)
            stamina.Cooldown += CooldownModifier;

        entityManager.Dirty(uid, stamina);
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyDensity : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public float DensityModifier;

    [DataField, AlwaysPushInheritance]
    public bool Multiply;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var physicsSystem = entityManager.System<SharedPhysicsSystem>();
        if (!entityManager.TryGetComponent(uid, out FixturesComponent? fixturesComponent)
            || fixturesComponent.Fixtures.Count == 0)
            return;

        var fixture = fixturesComponent.Fixtures.First();
        var newDensity = Multiply
            ? fixture.Value.Density * DensityModifier
            : fixture.Value.Density + DensityModifier;
        physicsSystem.SetDensity(uid, fixture.Key, fixture.Value, newDensity);
    }
}

[UsedImplicitly]
public sealed partial class TraitModifySlowOnDamage : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public float DamageThresholdsModifier;

    [DataField, AlwaysPushInheritance]
    public float SpeedModifierMultiplier = 1f;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        if (!entityManager.TryGetComponent(uid, out SlowOnDamageComponent? slowOnDamage))
            return;

        var updated = new Dictionary<FixedPoint2, float>();
        foreach (var (threshold, modifier) in slowOnDamage.SpeedModifierThresholds)
        {
            var newThreshold = threshold + DamageThresholdsModifier;
            if (newThreshold <= 0)
                continue;
            updated[newThreshold] = modifier * SpeedModifierMultiplier;
        }

        slowOnDamage.SpeedModifierThresholds = updated;
        entityManager.Dirty(uid, slowOnDamage);
        entityManager.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(uid);
    }
}

[UsedImplicitly]
public sealed partial class TraitModifyUnarmed : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public SoundSpecifier? SoundHit;

    [DataField, AlwaysPushInheritance]
    public EntProtoId? Animation;

    [DataField, AlwaysPushInheritance]
    public DamageSpecifier? Damage;

    [DataField, AlwaysPushInheritance]
    public DamageSpecifier? FlatDamageIncrease;

    [DataField, AlwaysPushInheritance]
    public float? RangeModifier;

    [DataField, AlwaysPushInheritance]
    public float? AttackRateModifier;

    [DataField, AlwaysPushInheritance]
    public float? DamageMultiplier;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        if (!entityManager.TryGetComponent(uid, out MeleeWeaponComponent? melee))
            return;

        if (SoundHit is not null)
            melee.HitSound = SoundHit;

        if (Animation is not null)
            melee.Animation = Animation.Value;

        if (Damage is not null)
            melee.Damage = Damage;

        if (FlatDamageIncrease is not null)
            melee.Damage += FlatDamageIncrease;

        if (RangeModifier is not null)
            melee.Range *= RangeModifier.Value;

        if (AttackRateModifier is not null)
            melee.AttackRate *= AttackRateModifier.Value;

        if (DamageMultiplier is not null)
            melee.Damage *= DamageMultiplier.Value;

        entityManager.Dirty(uid, melee);
    }
}

[UsedImplicitly]
public sealed partial class TraitAddTag : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public List<ProtoId<TagPrototype>> Tags { get; private set; } = new();

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        entityManager.System<TagSystem>().AddTags(uid, Tags);
    }
}

[UsedImplicitly]
public sealed partial class TraitCyberneticLimbReplacement : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public BodyPartType RemoveBodyPart { get; private set; } = BodyPartType.Arm;

    [DataField, AlwaysPushInheritance]
    public BodyPartSymmetry PartSymmetry { get; private set; } = BodyPartSymmetry.Left;

    [DataField, AlwaysPushInheritance]
    public EntProtoId? ProtoId { get; private set; }

    [DataField, AlwaysPushInheritance]
    public string SlotId { get; private set; } = "right arm";

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        var bodySystem = entityManager.System<BodySystem>();
        var transformSystem = entityManager.System<SharedTransformSystem>();

        EntityUid? found = null;
        BodyPartComponent? foundPart = null;
        foreach (var (partId, part) in bodySystem.GetBodyChildren(uid))
        {
            if (part.PartType != RemoveBodyPart || part.Symmetry != PartSymmetry)
                continue;
            found = partId;
            foundPart = part;
            break;
        }

        (EntityUid Parent, string Slot)? parent = null;
        if (found is { } partUid)
        {
            parent = bodySystem.GetParentPartAndSlotOrNull(partUid);
            bodySystem.TryDetachPart(partUid, foundPart);
            entityManager.QueueDeleteEntity(partUid);
        }

        if (ProtoId is null)
            return;

        var coords = transformSystem.GetMoverCoordinates(uid);
        var newPart = entityManager.SpawnAtPosition(ProtoId.Value, coords);
        var slot = parent?.Slot ?? SlotId;
        if (parent is { } p)
            bodySystem.AttachPart(p.Parent, slot, newPart);
    }
}

/// <summary>
/// Copies only the fields that differ from the component's prototype defaults onto an existing component,
/// or adds the component if it is missing and <see cref="EnsureComp"/> is true.
/// </summary>
[UsedImplicitly]
public sealed partial class TraitModifyComponent : TraitFunction
{
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry Components { get; private set; } = new();

    [DataField, AlwaysPushInheritance]
    public bool EnsureComp { get; private set; } = true;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        foreach (var entry in Components.Values)
        {
            var entryType = entry.Component.GetType();
            var refComp = factory.GetComponent(entry);

            if (entityManager.HasComponent(uid, entryType))
            {
                var targetComp = entityManager.GetComponent(uid, entryType);
                foreach (var field in entryType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var setValue = field.GetValue(entry.Component);
                    if (Equals(setValue, field.GetValue(refComp)))
                        continue;
                    field.SetValue(targetComp, setValue);
                }

                if (targetComp.GetType().HasCustomAttribute<NetworkedComponentAttribute>())
                    entityManager.Dirty(uid, targetComp);
            }
            else if (EnsureComp)
            {
                var comp = (Component) serializationManager.CreateCopy(entry.Component, notNullableOverride: true);
                entityManager.AddComponent(uid, comp);
            }
        }
    }
}
