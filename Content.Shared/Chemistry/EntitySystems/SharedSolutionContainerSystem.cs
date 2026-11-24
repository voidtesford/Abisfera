// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Content.Shared.Containers;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Localizations;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Dependency = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared.Chemistry.EntitySystems;

/// <summary>
/// The event raised whenever a solution entity is modified.
/// </summary>
[ByRefEvent]
public readonly partial record struct SolutionChangedEvent(Entity<SolutionComponent> Solution);

/// <summary>
/// The event raised whenever a solution entity is filled past its capacity.
/// </summary>
[ByRefEvent]
public partial record struct SolutionOverflowEvent(Entity<SolutionComponent> Solution, FixedPoint2 Overflow)
{
    public readonly Entity<SolutionComponent> Solution = Solution;
    public readonly FixedPoint2 Overflow = Overflow;
    public bool Handled = false;
}

[ByRefEvent]
public partial record struct SolutionAccessAttemptEvent(string SolutionName)
{
    public bool Cancelled;
}

/// <summary>
/// Part of Chemistry system deal with SolutionContainers
/// </summary>
[UsedImplicitly]
public abstract partial class SharedSolutionContainerSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;
    [Dependency] protected readonly ChemicalReactionSystem ChemicalReactionSystem = default!;
    [Dependency] protected readonly ExamineSystemShared ExamineSystem = default!;
    [Dependency] protected readonly OpenableSystem Openable = default!;
    [Dependency] protected readonly SharedAppearanceSystem AppearanceSystem = default!;
    [Dependency] protected readonly SharedHandsSystem Hands = default!;
    [Dependency] protected readonly SharedContainerSystem ContainerSystem = default!;
    [Dependency] protected readonly MetaDataSystem MetaDataSys = default!;
    [Dependency] protected readonly INetManager NetManager = default!;

 public override void Initialize()
    {
        base.Initialize();

        InitializeRelays();

        SubscribeLocalEvent<SolutionComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<SolutionComponent, ComponentStartup>(OnSolutionStartup);
        SubscribeLocalEvent<SolutionComponent, ComponentShutdown>(OnSolutionShutdown);
        SubscribeLocalEvent<SolutionContainerManagerComponent, ComponentInit>(OnContainerManagerInit);
        SubscribeLocalEvent<ExaminableSolutionComponent, ExaminedEvent>(OnExamineSolution);
        SubscribeLocalEvent<ExaminableSolutionComponent, GetVerbsEvent<ExamineVerb>>(OnSolutionExaminableVerb);
        SubscribeLocalEvent<SolutionContainerManagerComponent, MapInitEvent>(OnMapInit);

        if (NetManager.IsServer)
        {
            SubscribeLocalEvent<SolutionContainerManagerComponent, ComponentShutdown>(OnContainerManagerShutdown);
            SubscribeLocalEvent<ContainedSolutionComponent, ComponentShutdown>(OnContainedSolutionShutdown);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ResolveSolution(Entity<SolutionContainerManagerComponent?> container, string? name, [NotNullWhen(true)] ref Entity<SolutionComponent>? entity, [NotNullWhen(true)] out Solution? solution)
    {
        if (!ResolveSolution(container, name, ref entity))
        {
            solution = null;
            return false;
        }
        solution = entity.Value.Comp.Solution;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ResolveSolution(Entity<SolutionContainerManagerComponent?> container, string? name, [NotNullWhen(true)] ref Entity<SolutionComponent>? entity)
    {
        if (entity is not null)
        {
            return TryGetSolution(container, name, out var debugEnt) && debugEnt.Value.Owner == entity.Value.Owner;
        }
        return TryGetSolution(container, name, out entity);
    }

    public bool TryGetSolution(
        Entity<SolutionContainerManagerComponent?> container,
        string? name,
        [NotNullWhen(true)] out Entity<SolutionComponent>? entity,
        [NotNullWhen(true)] out Solution? solution,
        bool errorOnMissing = false)
    {
        if (!TryGetSolution(container, name, out entity, errorOnMissing: errorOnMissing))
        {
            solution = null;
            return false;
        }
        solution = entity.Value.Comp.Solution;
        return true;
    }

    public bool TryGetSolution(
        Entity<SolutionContainerManagerComponent?> container,
        string? name,
        [NotNullWhen(true)] out Entity<SolutionComponent>? entity,
        bool errorOnMissing = false)
    {
        var ev = new GetConnectedContainerEvent();
        RaiseLocalEvent(container, ref ev);
        if (ev.ContainerEntity.HasValue)
            container = ev.ContainerEntity.Value;

        EntityUid uid;
        if (name is null)
            uid = container;
        else if (
            ContainerSystem.TryGetContainer(container, $"solution@{name}", out var solutionContainer) &&
            solutionContainer is ContainerSlot solutionSlot &&
            solutionSlot.ContainedEntity is { } containedSolution
        )
        {
            var attemptEv = new SolutionAccessAttemptEvent(name);
            RaiseLocalEvent(container, ref attemptEv);
            if (attemptEv.Cancelled)
            {
                entity = null;
                return false;
            }
            uid = containedSolution;
        }
        else
        {
            entity = null;
            if (!errorOnMissing)
                return false;
            Log.Error($"{ToPrettyString(container)} does not have a solution with ID: {name}");
            return false;
        }

        if (!TryComp(uid, out SolutionComponent? comp))
        {
            entity = null;
            if (!errorOnMissing)
                return false;
            Log.Error($"{ToPrettyString(container)} does not have a solution with ID: {name}");
            return false;
        }

        entity = (uid, comp);
        return true;
    }

    public bool TryGetSolution(SolutionContainerManagerComponent container,
        string name,
        [NotNullWhen(true)] out Solution? solution,
        bool errorOnMissing = false)
    {
        solution = null;
        if (container.Solutions != null)
            return container.Solutions.TryGetValue(name, out solution);
        if (!errorOnMissing)
            return false;
        Log.Error($"{container} does not have a solution with ID: {name}");
        return false;
    }

    public IEnumerable<(string? Name, Entity<SolutionComponent> Solution)> EnumerateSolutions(Entity<SolutionContainerManagerComponent?> container, bool includeSelf = true)
    {
        if (includeSelf && TryComp(container, out SolutionComponent? solutionComp))
            yield return (null, (container.Owner, solutionComp));

        if (!Resolve(container, ref container.Comp, logMissing: false))
            yield break;

        foreach (var name in container.Comp.Containers)
        {
            var attemptEv = new SolutionAccessAttemptEvent(name);
            RaiseLocalEvent(container, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            if (ContainerSystem.GetContainer(container, $"solution@{name}") is ContainerSlot slot && slot.ContainedEntity is { } solutionId)
                yield return (name, (solutionId, Comp<SolutionComponent>(solutionId)));
        }
    }

    public IEnumerable<(string Name, Solution Solution)> EnumerateSolutions(SolutionContainerManagerComponent container)
    {
        if (container.Solutions is not { Count: > 0 } solutions)
            yield break;

        foreach (var (name, solution) in solutions)
        {
            yield return (name, solution);
        }
    }

    protected void UpdateAppearance(Entity<AppearanceComponent?> container, Entity<SolutionComponent, ContainedSolutionComponent> soln)
    {
        var (uid, appearanceComponent) = container;
        if (!HasComp<SolutionContainerVisualsComponent>(uid) || !Resolve(uid, ref appearanceComponent, logMissing: false))
            return;

        var (_, comp, relation) = soln;
        var solution = comp.Solution;

        AppearanceSystem.SetData(uid, SolutionContainerVisuals.FillFraction, solution.FillFraction, appearanceComponent);
        AppearanceSystem.SetData(uid, SolutionContainerVisuals.Color, solution.GetColor(PrototypeManager), appearanceComponent);
        AppearanceSystem.SetData(uid, SolutionContainerVisuals.SolutionName, relation.ContainerName, appearanceComponent);

        if (solution.GetPrimaryReagentId() is { } reagent)
            AppearanceSystem.SetData(uid, SolutionContainerVisuals.BaseOverride, reagent.ToString(), appearanceComponent);
    }

    public FixedPoint2 GetTotalPrototypeQuantity(EntityUid owner, string reagentId)
    {
        var reagentQuantity = FixedPoint2.New(0);
        if (Exists(owner) && TryComp(owner, out SolutionContainerManagerComponent? managerComponent))
        {
            foreach (var (_, soln) in EnumerateSolutions((owner, managerComponent)))
            {
                var solution = soln.Comp.Solution;
                reagentQuantity += solution.GetTotalPrototypeQuantity(reagentId);
            }
        }
        return reagentQuantity;
    }

    public void UpdateChemicals(Entity<SolutionComponent> soln, bool needsReactionsProcessing = true, ReactionMixerComponent? mixerComponent = null)
    {
        Dirty(soln);
        var (uid, comp) = soln;
        var solution = comp.Solution;

        if (needsReactionsProcessing && solution.CanReact)
            ChemicalReactionSystem.FullyReactSolution(soln, mixerComponent);

        var overflow = solution.Volume - solution.MaxVolume;
        if (overflow > FixedPoint2.Zero)
        {
            var overflowEv = new SolutionOverflowEvent(soln, overflow);
            RaiseLocalEvent(uid, ref overflowEv);
        }

        UpdateAppearance((uid, comp, null));
        var changedEv = new SolutionChangedEvent(soln);
        RaiseLocalEvent(uid, ref changedEv);
    }

    public void UpdateAppearance(Entity<SolutionComponent, AppearanceComponent?> soln)
    {
        var (uid, comp, appearanceComponent) = soln;
        var solution = comp.Solution;

        if (!Exists(uid) || !Resolve(uid, ref appearanceComponent, false))
            return;

        AppearanceSystem.SetData(uid, SolutionContainerVisuals.FillFraction, solution.FillFraction, appearanceComponent);
        AppearanceSystem.SetData(uid, SolutionContainerVisuals.Color, solution.GetColor(PrototypeManager), appearanceComponent);

        if (solution.GetPrimaryReagentId() is { } reagent)
            AppearanceSystem.SetData(uid, SolutionContainerVisuals.BaseOverride, reagent.ToString(), appearanceComponent);
    }

    public Solution SplitSolution(Entity<SolutionComponent> soln, FixedPoint2 quantity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var splitSol = solution.SplitSolution(quantity);
        UpdateChemicals(soln);
        return splitSol;
    }

    public Solution SplitSolutionReagentsEvenly(Entity<SolutionComponent> soln, FixedPoint2 quantity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var splitSol = solution.SplitSolutionReagentsEvenly(quantity);
        UpdateChemicals(soln);
        return splitSol;
    }

    public Solution SplitStackSolution(Entity<SolutionComponent> soln, FixedPoint2 quantity, int stackCount)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var splitSol = solution.SplitSolution(quantity / stackCount);
        solution.SplitSolution(quantity - splitSol.Volume);
        UpdateChemicals(soln);
        return splitSol;
    }

    public Solution SplitSolutionWithout(Entity<SolutionComponent> soln, FixedPoint2 quantity, params string[] reagents)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var splitSol = solution.SplitSolutionWithout(quantity, reagents);
        UpdateChemicals(soln);
        return splitSol;
    }

    public Solution SplitSolutionWithout(Entity<SolutionComponent> soln, FixedPoint2 quantity, params ProtoId<ReagentPrototype>[] reagents)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var splitSol = solution.SplitSolutionWithout(quantity, reagents);
        UpdateChemicals(soln);
        return splitSol;
    }

    public void RemoveAllSolution(Entity<SolutionComponent> soln)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (solution.Volume == 0) return;
        solution.RemoveAllSolution();
        UpdateChemicals(soln);
    }

    public void SetCapacity(Entity<SolutionComponent> soln, FixedPoint2 capacity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (solution.MaxVolume == capacity) return;
        solution.MaxVolume = capacity;
        UpdateChemicals(soln);
    }

    public bool TryAddReagent(Entity<SolutionComponent> soln, ReagentQuantity reagentQuantity, out FixedPoint2 acceptedQuantity, float? temperature = null)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        acceptedQuantity = solution.AvailableVolume > reagentQuantity.Quantity ? reagentQuantity.Quantity : solution.AvailableVolume;
        if (acceptedQuantity <= 0) return reagentQuantity.Quantity == 0;

        if (temperature == null)
            solution.AddReagent(reagentQuantity.Reagent, acceptedQuantity);
        else
        {
            var proto = PrototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
            solution.AddReagent(proto, acceptedQuantity, temperature.Value, PrototypeManager);
        }

        UpdateChemicals(soln);
        return acceptedQuantity == reagentQuantity.Quantity;
    }

    public bool TryAddReagent(Entity<SolutionComponent> soln, string prototype, FixedPoint2 quantity, float? temperature = null, List<ReagentData>? data = null)
        => TryAddReagent(soln, new ReagentQuantity(prototype, quantity, data), out _, temperature);

    public bool TryAddReagent(Entity<SolutionComponent> soln, string prototype, FixedPoint2 quantity, out FixedPoint2 acceptedQuantity, float? temperature = null, List<ReagentData>? data = null)
    {
        var reagent = new ReagentQuantity(prototype, quantity, data);
        return TryAddReagent(soln, reagent, out acceptedQuantity, temperature);
    }

    public bool TryAddReagent(Entity<SolutionComponent> soln, ReagentId reagentId, FixedPoint2 quantity, out FixedPoint2 acceptedQuantity, float? temperature = null)
    {
        var quant = new ReagentQuantity(reagentId, quantity);
        return TryAddReagent(soln, quant, out acceptedQuantity, temperature);
    }

    public FixedPoint2 RemoveReagent(Entity<SolutionComponent> soln, ReagentQuantity reagentQuantity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        var quant = solution.RemoveReagent(reagentQuantity);
        if (quant <= FixedPoint2.Zero) return FixedPoint2.Zero;
        UpdateChemicals(soln);
        return quant;
    }

    public FixedPoint2 RemoveReagent(Entity<SolutionComponent> soln, string prototype, FixedPoint2 quantity, List<ReagentData>? data = null)
        => RemoveReagent(soln, new ReagentQuantity(prototype, quantity, data));

    public FixedPoint2 RemoveReagent(Entity<SolutionComponent> soln, ReagentId reagentId, FixedPoint2 quantity)
        => RemoveReagent(soln, new ReagentQuantity(reagentId, quantity));

    public bool TryTransferSolution(Entity<SolutionComponent> soln, Solution source, FixedPoint2 quantity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (quantity < 0) throw new InvalidOperationException("Quantity must be positive");
        quantity = FixedPoint2.Min(quantity, solution.AvailableVolume, source.Volume);
        if (quantity == 0) return false;
        solution.AddSolution(source.SplitSolution(quantity), PrototypeManager);
        UpdateChemicals(soln);
        return true;
    }

    public bool TryAddSolution(Entity<SolutionComponent> soln, Solution toAdd)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (toAdd.Volume == FixedPoint2.Zero) return true;
        if (toAdd.Volume > solution.AvailableVolume) return false;
        ForceAddSolution(soln, toAdd);
        return true;
    }

    public FixedPoint2 AddSolution(Entity<SolutionComponent> soln, Solution toAdd)
    {
        var solution = soln.Comp.Solution;
        if (toAdd.Volume == FixedPoint2.Zero) return FixedPoint2.Zero;
        var quantity = FixedPoint2.Max(FixedPoint2.Zero, FixedPoint2.Min(toAdd.Volume, solution.AvailableVolume));
        if (quantity < toAdd.Volume)
            solution.AddSolution(toAdd.Clone().SplitSolution(quantity), PrototypeManager);
        else
            solution.AddSolution(toAdd, PrototypeManager);
        UpdateChemicals(soln);
        return quantity;
    }

    public bool ForceAddSolution(Entity<SolutionComponent> soln, Solution toAdd)
    {
        var solution = soln.Comp.Solution;
        if (toAdd.Volume == FixedPoint2.Zero) return false;
        solution.AddSolution(toAdd, PrototypeManager);
        UpdateChemicals(soln);
        return true;
    }

    public bool TryMixAndOverflow(Entity<SolutionComponent> soln, Solution toAdd, FixedPoint2 overflowThreshold, [MaybeNullWhen(false)] out Solution overflowingSolution)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (toAdd.Volume == 0 || overflowThreshold > solution.MaxVolume)
        {
            overflowingSolution = null;
            return false;
        }
        solution.AddSolution(toAdd, PrototypeManager);
        overflowingSolution = solution.SplitSolution(FixedPoint2.Max(FixedPoint2.Zero, solution.Volume - overflowThreshold));
        UpdateChemicals(soln);
        return true;
    }

    public Solution RemoveEachReagent(Entity<SolutionComponent> soln, FixedPoint2 quantity)
    {
        var (uid, comp) = soln;
        var solution = comp.Solution;
        if (quantity <= 0) return new Solution();
        var removedSolution = new Solution();
        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, _) = solution.Contents[i];
            var removedQuantity = solution.RemoveReagent(reagent, quantity);
            removedSolution.AddReagent(reagent, removedQuantity);
        }
        UpdateChemicals(soln);
        return removedSolution;
    }

    public void SetTemperature(Entity<SolutionComponent> soln, float temperature)
    {
        var (_, comp) = soln;
        var solution = comp.Solution;
        if (temperature == solution.Temperature) return;
        solution.Temperature = temperature;
        UpdateChemicals(soln);
    }

    public void SetThermalEnergy(Entity<SolutionComponent> soln, float thermalEnergy)
    {
        var (_, comp) = soln;
        var solution = comp.Solution;
        var heatCap = solution.GetHeatCapacity(PrototypeManager);
        solution.Temperature = heatCap == 0 ? 0 : thermalEnergy / heatCap;
        UpdateChemicals(soln);
    }

    public void AddThermalEnergy(Entity<SolutionComponent> soln, float thermalEnergy)
    {
        var (_, comp) = soln;
        var solution = comp.Solution;
        if (thermalEnergy == 0.0f) return;
        var heatCap = solution.GetHeatCapacity(PrototypeManager);
        solution.Temperature += heatCap == 0 ? 0 : thermalEnergy / heatCap;
        UpdateChemicals(soln);
    }

    public void AddThermalEnergyClamped(Entity<SolutionComponent> soln, float thermalEnergy, float min, float max)
    {
        var solution = soln.Comp.Solution;
        if (thermalEnergy == 0.0f) return;
        var heatCap = solution.GetHeatCapacity(PrototypeManager);
        var deltaT = thermalEnergy / heatCap;
        solution.Temperature = Math.Clamp(solution.Temperature + deltaT, min, max);
        UpdateChemicals(soln);
    }

    private void OnComponentInit(Entity<SolutionComponent> entity, ref ComponentInit args) => entity.Comp.Solution.ValidateSolution();

    private void OnSolutionStartup(Entity<SolutionComponent> entity, ref ComponentStartup args) => UpdateChemicals(entity);

    private void OnSolutionShutdown(Entity<SolutionComponent> entity, ref ComponentShutdown args) => RemoveAllSolution(entity);

    private void OnContainerManagerInit(Entity<SolutionContainerManagerComponent> entity, ref ComponentInit args)
    {
        if (entity.Comp.Containers is not { Count: > 0 } containers) return;
        var containerManager = EnsureComp<ContainerManagerComponent>(entity);
        foreach (var name in containers)
            ContainerSystem.EnsureContainer<ContainerSlot>(entity.Owner, $"solution@{name}", containerManager);
    }

    private void OnExamineSolution(Entity<ExaminableSolutionComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !CanSeeHiddenSolution(entity, args.Examiner) || !TryGetSolution(entity.Owner, entity.Comp.Solution, out _, out var solution))
            return;

        using (args.PushGroup(nameof(ExaminableSolutionComponent)))
        {
            var primaryReagent = solution.GetPrimaryReagentId();
            if (string.IsNullOrEmpty(primaryReagent?.Prototype) || !PrototypeManager.Resolve<ReagentPrototype>(primaryReagent.Value.Prototype, out var primary))
            {
                args.PushMarkup(Loc.GetString(entity.Comp.LocVolume, ("fillLevel", ExaminedVolumeDisplay.Empty)));
                return;
            }

            args.PushMarkup(Loc.GetString(entity.Comp.LocVolume,
                                ("fillLevel", ExaminedVolume(entity, solution, args.Examiner)),
                                ("current", solution.Volume),
                                ("max", solution.MaxVolume)));

            var colorHex = solution.GetColor(PrototypeManager).ToHexNoAlpha();

            args.PushMarkup(Loc.GetString(entity.Comp.LocPhysicalQuality,
                                        ("color", colorHex),
                                        ("desc", primary.LocalizedPhysicalDescription),
                                        ("chemCount", solution.Contents.Count)));

            var sortedReagentPrototypes = solution.GetReagentPrototypes(PrototypeManager)
                .OrderByDescending(pair => pair.Value.Value)
                .ThenBy(pair => pair.Key.LocalizedName);

            var recognized = new List<string>();
            foreach (var keyValuePair in sortedReagentPrototypes)
            {
                var proto = keyValuePair.Key;
                if (!proto.Recognizable) continue;
                recognized.Add(Loc.GetString("examinable-solution-recognized",
                                            ("color", proto.SubstanceColor.ToHexNoAlpha()),
                                            ("chemical", proto.LocalizedName)));
            }

            if (recognized.Count == 0) return;
            var msg = ContentLocalizationManager.FormatList(recognized);
            args.PushMarkup(Loc.GetString(entity.Comp.LocRecognizableReagents, ("recognizedString", msg)));
        }
    }

    public ExaminedVolumeDisplay ExaminedVolume(Entity<ExaminableSolutionComponent> ent, Solution sol, EntityUid? examiner = null)
    {
        if (ent.Comp.ExactVolume) return ExaminedVolumeDisplay.Exact;
        return (int)PercentFull(sol) switch
        {
            100 => ExaminedVolumeDisplay.Full,
            > 66 => ExaminedVolumeDisplay.MostlyFull,
            > 33 => HalfEmptyOrHalfFull(examiner),
            > 0 => ExaminedVolumeDisplay.MostlyEmpty,
            _ => ExaminedVolumeDisplay.Empty,
        };
    }

    private ExaminedVolumeDisplay HalfEmptyOrHalfFull(EntityUid? examiner = null)
    {
        if (examiner == null) return ExaminedVolumeDisplay.HalfFull;
        var meta = MetaData(examiner.Value);
        if (meta.EntityName.Length > 0 && string.Compare(meta.EntityName.Substring(0, 1), "m", StringComparison.InvariantCultureIgnoreCase) > 0)
            return ExaminedVolumeDisplay.HalfFull;
        return ExaminedVolumeDisplay.HalfEmpty;
    }

    private void OnSolutionExaminableVerb(Entity<ExaminableSolutionComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess) return;
        var scanEvent = new SolutionScanEvent();
        RaiseLocalEvent(args.User, scanEvent);
        if (!scanEvent.CanScan) return;
        if (!TryGetSolution(args.Target, entity.Comp.Solution, out _, out var solutionHolder)) return;
        if (!CanSeeHiddenSolution(entity, args.User)) return;

        var target = args.Target;
        var user = args.User;
        var verb = new ExamineVerb()
        {
            Act = () =>
            {
                var markup = GetSolutionExamine(solutionHolder);
                ExamineSystem.SendExamineTooltip(user, target, markup, false, false);
            },
            Text = Loc.GetString("scannable-solution-verb-text"),
            Message = Loc.GetString("scannable-solution-verb-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/drink.svg.192dpi.png")),
        };
        args.Verbs.Add(verb);
    }

    private FormattedMessage GetSolutionExamine(Solution solution)
    {
        var msg = new FormattedMessage();
        if (solution.Volume == 0)
        {
            msg.AddMarkupOrThrow(Loc.GetString("scannable-solution-empty-container"));
            return msg;
        }
        msg.AddMarkupOrThrow(Loc.GetString("scannable-solution-main-text"));
        var sortedReagentPrototypes = solution.GetReagentPrototypes(PrototypeManager)
            .OrderByDescending(pair => pair.Value.Value)
            .ThenBy(pair => pair.Key.LocalizedName);

        foreach (var (proto, quantity) in sortedReagentPrototypes)
        {
            msg.PushNewline();
            msg.AddMarkupOrThrow(Loc.GetString("scannable-solution-chemical"
                , ("type", proto.LocalizedName)
                , ("color", proto.SubstanceColor.ToHexNoAlpha())
                , ("amount", quantity)));
        }
        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("scannable-solution-temperature", ("temperature", Math.Round(solution.Temperature))));
        return msg;
    }

    private bool CanSeeHiddenSolution(Entity<ExaminableSolutionComponent> entity, EntityUid examiner)
    {
        if (entity.Comp.HeldOnly && !Hands.IsHolding(examiner, entity, out _)) return false;
        if (!entity.Comp.ExaminableWhileClosed && Openable.IsClosed(entity.Owner, predicted: true)) return false;
        return true;
    }

    private void OnMapInit(Entity<SolutionContainerManagerComponent> entity, ref MapInitEvent args) => EnsureAllSolutions(entity);

    private void OnContainerManagerShutdown(Entity<SolutionContainerManagerComponent> entity, ref ComponentShutdown args)
    {
        foreach (var name in entity.Comp.Containers)
        {
            if (ContainerSystem.TryGetContainer(entity, $"solution@{name}", out var solutionContainer))
                ContainerSystem.ShutdownContainer(solutionContainer);
        }
        entity.Comp.Containers.Clear();
    }

    private void OnContainedSolutionShutdown(Entity<ContainedSolutionComponent> entity, ref ComponentShutdown args)
    {
        if (TryComp(entity.Comp.Container, out SolutionContainerManagerComponent? container))
        {
            container.Containers.Remove(entity.Comp.ContainerName);
            Dirty(entity.Comp.Container, container);
        }
        if (ContainerSystem.TryGetContainer(entity, $"solution@{entity.Comp.ContainerName}", out var solutionContainer))
            ContainerSystem.ShutdownContainer(solutionContainer);
    }

    public bool EnsureSolution(
        Entity<MetaDataComponent?> entity,
        string name,
        [NotNullWhen(true)] out Solution? solution,
        FixedPoint2 maxVol = default)
        => EnsureSolution(entity, name, maxVol, null, out _, out solution);

    public bool EnsureSolution(
        Entity<MetaDataComponent?> entity,
        string name,
        out bool existed,
        [NotNullWhen(true)] out Solution? solution,
        FixedPoint2 maxVol = default)
        => EnsureSolution(entity, name, maxVol, null, out existed, out solution);

    public bool EnsureSolution(
        Entity<MetaDataComponent?> entity,
        string name,
        FixedPoint2 maxVol,
        Solution? prototype,
        out bool existed,
        [NotNullWhen(true)] out Solution? solution)
    {
        solution = null;
        existed = false;
        var (uid, meta) = entity;
        if (!Resolve(uid, ref meta))
            throw new InvalidOperationException("Attempted to ensure solution on invalid entity.");
        var manager = EnsureComp<SolutionContainerManagerComponent>(uid);
        if (meta.EntityLifeStage >= EntityLifeStage.MapInitialized)
        {
            EnsureSolutionEntity((uid, manager), name, out existed, out var solEnt, maxVol, prototype);
            solution = solEnt!.Value.Comp.Solution;
            return true;
        }
        solution = EnsureSolutionPrototype((uid, manager), name, maxVol, prototype, out existed);
        return true;
    }

    public void EnsureAllSolutions(Entity<SolutionContainerManagerComponent> entity)
    {
        if (NetManager.IsClient || entity.Comp.Solutions is not { } prototypes) return;
        foreach (var (name, prototype) in prototypes)
            EnsureSolutionEntity((entity.Owner, entity.Comp), name, out _, out _, prototype.MaxVolume, prototype);
        entity.Comp.Solutions = null;
        Dirty(entity);
    }

    public bool EnsureSolutionEntity(
        Entity<SolutionContainerManagerComponent?> entity,
        string name,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEntity,
        FixedPoint2 maxVol = default)
        => EnsureSolutionEntity(entity, name, out _, out solutionEntity, maxVol);

    public bool EnsureSolutionEntity(
        Entity<SolutionContainerManagerComponent?> entity,
        string name,
        out bool existed,
        [NotNullWhen(true)] out Entity<SolutionComponent>? solutionEntity,
        FixedPoint2 maxVol = default,
        Solution? prototype = null)
    {
        existed = true;
        solutionEntity = null;
        var (uid, container) = entity;
        var solutionSlot = ContainerSystem.EnsureContainer<ContainerSlot>(uid, $"solution@{name}", out existed);
        if (!Resolve(uid, ref container, logMissing: false))
        {
            existed = false;
            container = AddComp<SolutionContainerManagerComponent>(uid);
            container.Containers.Add(name);
            if (NetManager.IsClient) return false;
        }
        else if (!existed)
        {
            container.Containers.Add(name);
            Dirty(uid, container);
        }

        var needsInit = false;
        SolutionComponent solutionComp;
        if (solutionSlot.ContainedEntity is not { } solutionId)
        {
            if (NetManager.IsClient) return false;
            prototype ??= new() { MaxVolume = maxVol };
            prototype.Name = name;
            (solutionId, solutionComp, _) = SpawnSolutionUninitialized(solutionSlot, name, maxVol, prototype);
            existed = false;
            needsInit = true;
            Dirty(uid, container);
        }
        else
        {
            solutionComp = Comp<SolutionComponent>(solutionId);
            var solution = solutionComp.Solution;
            solution.MaxVolume = FixedPoint2.Max(solution.MaxVolume, maxVol);
            if (prototype is { Volume.Value: > 0 })
                solution.AddSolution(prototype, PrototypeManager);
            Dirty(solutionId, solutionComp);
        }

        if (needsInit)
            EntityManager.InitializeAndStartEntity(solutionId, Transform(solutionId).MapID);

        solutionEntity = (solutionId, solutionComp);
        return true;
    }

    public IEnumerable<string> EnumerateSolutionContainers(Entity<SolutionContainerManagerComponent?> ent, bool addPrefix = true)
    {
        if (!Resolve(ent, ref ent.Comp, false)) yield break;
        var prefix = addPrefix ? "solution@" : string.Empty;
        foreach (var container in ent.Comp.Containers)
            yield return prefix + container;
    }

    public Solution EnsureSolutionPrototype(Entity<SolutionContainerManagerComponent?> entity, string name, FixedPoint2 maxVol, Solution? prototype, out bool existed)
    {
        existed = true;
        var (uid, container) = entity;
        if (!Resolve(uid, ref container, logMissing: false))
        {
            container = AddComp<SolutionContainerManagerComponent>(uid);
            existed = false;
        }

        if (container.Solutions is null)
            container.Solutions = new(SolutionContainerManagerComponent.DefaultCapacity);

        if (!container.Solutions.TryGetValue(name, out var solution))
        {
            solution = prototype ?? new() { Name = name, MaxVolume = maxVol };
            container.Solutions.Add(name, solution);
            existed = false;
        }
        else
            solution.MaxVolume = FixedPoint2.Max(solution.MaxVolume, maxVol);

        Dirty(uid, container);
        return solution;
    }

    private Entity<SolutionComponent, ContainedSolutionComponent> SpawnSolutionUninitialized(ContainerSlot container, string name, FixedPoint2 maxVol, Solution prototype)
    {
        var coords = new EntityCoordinates(container.Owner, Vector2.Zero);
        var uid = EntityManager.CreateEntityUninitialized(null, coords, null);
        var solution = new SolutionComponent() { Solution = prototype };
        AddComp(uid, solution);
        var relation = new ContainedSolutionComponent() { Container = container.Owner, ContainerName = name };
        AddComp(uid, relation);
        MetaDataSys.SetEntityName(uid, $"solution - {name}", raiseEvents: false);
        ContainerSystem.Insert(uid, container, force: true);
        return (uid, solution, relation);
    }

    public void AdjustDissolvedReagent(Entity<SolutionComponent> dissolvedSolution, FixedPoint2 volume, ReagentId reagent, float concentrationChange)
    {
        if (concentrationChange == 0) return;
        var dissolvedSol = dissolvedSolution.Comp.Solution;
        var amtChange = GetReagentQuantityFromConcentration(dissolvedSolution, volume, MathF.Abs(concentrationChange));
        if (concentrationChange > 0)
            dissolvedSol.AddReagent(reagent, amtChange);
        else
            dissolvedSol.RemoveReagent(reagent, amtChange);
        UpdateChemicals(dissolvedSolution);
    }

    public FixedPoint2 GetReagentQuantityFromConcentration(Entity<SolutionComponent> dissolvedSolution, FixedPoint2 volume, float concentration)
    {
        var dissolvedSol = dissolvedSolution.Comp.Solution;
        if (volume == 0 || dissolvedSol.Volume == 0) return 0;
        return concentration * volume;
    }

    public float GetReagentConcentration(Entity<SolutionComponent> dissolvedSolution, FixedPoint2 volume, ReagentId dissolvedReagent)
    {
        var dissolvedSol = dissolvedSolution.Comp.Solution;
        if (volume == 0 || dissolvedSol.Volume == 0 || !dissolvedSol.TryGetReagentQuantity(dissolvedReagent, out var dissolvedVol))
            return 0;
        return (float)dissolvedVol / volume.Float();
    }

    public FixedPoint2 ClampReagentAmountByConcentration(Entity<SolutionComponent> dissolvedSolution, FixedPoint2 volume, ReagentId dissolvedReagent, FixedPoint2 dissolvedReagentAmount, float maxConcentration = 1f)
    {
        var dissolvedSol = dissolvedSolution.Comp.Solution;
        if (volume == 0 || dissolvedSol.Volume == 0 || !dissolvedSol.TryGetReagentQuantity(dissolvedReagent, out var dissolvedVol))
            return 0;
        volume *= maxConcentration;
        dissolvedVol += dissolvedReagentAmount;
        var overflow = volume - dissolvedVol;
        if (overflow < 0) dissolvedReagentAmount += overflow;
        return dissolvedReagentAmount;
    }
}