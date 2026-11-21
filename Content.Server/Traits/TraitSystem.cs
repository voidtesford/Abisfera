// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._EinsteinEngines.Language;
using Content.Shared.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Traits;

public sealed class TraitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly SharedHandsSystem _sharedHandsSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    /// <summary>
    /// Applies the profile's traits to a freshly spawned mob. Invalid sets are silently trimmed.
    /// Called from <c>StationSpawningSystem.SpawnPlayerMob</c> so every spawn path is covered once.
    /// </summary>
    public void ApplyToMob(EntityUid mob, HumanoidCharacterProfile profile, JobPrototype? job)
    {
        if (!_cfg.GetCVar(CCVars.GameTraitsEnabled))
            return;

        if (job != null && !job.ApplyTraits)
            return;

        var traits = TraitPreferences.MapAndTrim(
            profile.TraitPreferences,
            profile,
            _prototypeManager,
            _cfg,
            EntityManager,
            job);

        var ordered = new List<TraitPrototype>();
        foreach (var traitId in traits)
        {
            if (_prototypeManager.TryIndex(traitId, out var traitPrototype))
                ordered.Add(traitPrototype);
        }
        ordered.Sort();

        foreach (var traitPrototype in ordered)
        {
            if (_whitelistSystem.IsWhitelistFail(traitPrototype.Whitelist, mob) ||
                _whitelistSystem.IsWhitelistPass(traitPrototype.Blacklist, mob))
                continue;

            if (traitPrototype.IncludedSpecies.Count > 0 && !traitPrototype.IncludedSpecies.Contains(profile.Species) ||
                traitPrototype.ExcludedSpecies.Contains(profile.Species))
                continue;

            foreach (var function in traitPrototype.Functions)
            {
                function.OnPlayerSpawn(mob, _componentFactory, EntityManager, _serialization);
            }

            if (traitPrototype.Components.Count > 0)
                EntityManager.AddComponents(mob, traitPrototype.Components, false);

            foreach (var special in traitPrototype.Specials)
            {
                special.AfterEquip(mob);
            }

            var language = EntityManager.System<LanguageSystem>();

            if (traitPrototype.RemoveLanguagesSpoken is not null)
            {
                foreach (var lang in traitPrototype.RemoveLanguagesSpoken)
                    language.RemoveLanguage(mob, lang, true, false);
            }

            if (traitPrototype.RemoveLanguagesUnderstood is not null)
            {
                foreach (var lang in traitPrototype.RemoveLanguagesUnderstood)
                    language.RemoveLanguage(mob, lang, false, true);
            }

            if (traitPrototype.LanguagesSpoken is not null)
            {
                foreach (var lang in traitPrototype.LanguagesSpoken)
                    language.AddLanguage(mob, lang, true, false);
            }

            if (traitPrototype.LanguagesUnderstood is not null)
            {
                foreach (var lang in traitPrototype.LanguagesUnderstood)
                    language.AddLanguage(mob, lang, false, true);
            }

            if (traitPrototype.TraitGear == null)
                continue;

            if (!TryComp(mob, out HandsComponent? handsComponent))
                continue;

            var coords = Transform(mob).Coordinates;
            var inhandEntity = Spawn(traitPrototype.TraitGear, coords);
            _sharedHandsSystem.TryPickup(mob,
                inhandEntity,
                checkActionBlocker: false,
                handsComp: handsComponent);
        }
    }
}
