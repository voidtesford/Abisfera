using System.Linq;
using Content.Goobstation.Server.Disease;
using Content.Goobstation.Server.Virology;
using Content.Goobstation.Shared._CorvaxGoob.Zakat;
using Content.Goobstation.Shared.Disease.Components;
using Content.Goobstation.Shared.Virology;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server._CorvaxGoob.Zakat;

/// <summary>
/// The only cure for «Закат»: swab the person, run the swab through the vaccinator, inject THAT person.
/// The diagnoser is the only way to find the disease (a paper slip).
/// </summary>
public sealed partial class ZakatCureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DiseaseSystem _disease = default!;
    [Dependency] private ZakatSystem _zakat = default!;
    [Dependency] private ZakatSymptomSystem _symptoms = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;

    /// <summary>
    /// One symptom recedes this long after the previous one.
    /// </summary>
    public static readonly TimeSpan RollbackTime = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A host without symptoms is cured this long after the shot.
    /// </summary>
    public static readonly TimeSpan CureTime = TimeSpan.FromSeconds(30);

    public static readonly SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZakatSwabComponent, AfterInteractEvent>(OnSwab, after: new[] { typeof(DiseaseSwabSystem) });
        SubscribeLocalEvent<ZakatVirologyComponent, VirologyMachineDoneEvent>(OnMachineDone, before: new[] { typeof(VirologyMachinesSystem) });
        SubscribeLocalEvent<ZakatVaccineComponent, AfterInteractEvent>(OnVaccineInteract, after: new[] { typeof(DiseaseSwabSystem) });
        SubscribeLocalEvent<ZakatVaccineComponent, UseInHandEvent>(OnVaccineUse);
        SubscribeLocalEvent<ZakatVaccineComponent, ExaminedEvent>(OnVaccineExamined);
    }

    #region swab

    /// <summary>
    /// The disease whose strain this organism carries, if any.
    /// </summary>
    public EntityUid? FindZakat(EntityUid uid, out EntityUid? disease)
    {
        disease = null;
        if (!TryComp<DiseaseCarrierComponent>(uid, out var carrier))
            return null;

        var query = EntityQueryEnumerator<ZakatComponent>();
        while (query.MoveNext(out var zakatUid, out var zakat))
        {
            if (_disease.TryFindDisease((uid, carrier), zakat.Genotype, out var found))
            {
                disease = found;
                return zakatUid;
            }
        }

        return null;
    }

    private bool IsZakatStrain(EntityUid? disease)
    {
        if (!TryComp<DiseaseComponent>(disease, out var comp))
            return false;

        var query = EntityQueryEnumerator<ZakatComponent>();
        while (query.MoveNext(out var zakat))
        {
            if (zakat.Genotype == comp.Genotype)
                return true;
        }

        return false;
    }

    private void OnSwab(Entity<ZakatSwabComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target is not { } target || !HasComp<DiseaseCarrierComponent>(target))
            return;

        TakeSample(ent, target);
        args.Handled = true;
    }

    /// <summary>
    /// Records the sample. The strain itself never sits in the swab: Goob's machines must not see it.
    /// </summary>
    public void TakeSample(Entity<ZakatSwabComponent> ent, EntityUid target)
    {
        ent.Comp.Subject = target;
        ent.Comp.SubjectName = Identity.Name(target, EntityManager);
        ent.Comp.Zakat = FindZakat(target, out _);
        ent.Comp.OtherDisease = false;

        if (TryComp<DiseaseSwabComponent>(ent, out var swab) && swab.DiseaseUid is { } clone)
        {
            if (IsZakatStrain(clone))
            {
                QueueDel(clone);
                swab.DiseaseUid = null;
                Dirty(ent.Owner, swab);
            }
            else
            {
                ent.Comp.OtherDisease = true;
            }
        }

        Dirty(ent);
    }

    #endregion

    #region machines

    private void OnMachineDone(Entity<ZakatVirologyComponent> ent, ref VirologyMachineDoneEvent args)
    {
        if (!args.Success
            || !TryComp<VirologyMachineComponent>(ent, out var machine)
            || !_itemSlots.TryGetSlot(ent, VirologyMachineComponent.SwabSlotId, out var slot)
            || slot.Item is not { } swabUid
            || !TryComp<ZakatSwabComponent>(swabUid, out var swab)
            || swab.Subject == null)
            return;

        // a clean sample with some other disease in it: the regular analysis handles that
        if (swab.Zakat == null && swab.OtherDisease)
            return;

        if (machine.Vaccinator)
            MakeVaccine((ent, ent.Comp), (swabUid, swab));
        else
            PrintReport((ent, ent.Comp), (swabUid, swab));

        _itemSlots.TryEject(ent, slot, null, out _);
        _audio.PlayPvs(machine.AnalyzedSound, ent);
    }

    public EntityUid PrintReport(Entity<ZakatVirologyComponent> ent, Entity<ZakatSwabComponent> swab)
    {
        var text = Loc.GetString("zakat-diagnosis-title") + "\n"
                   + Loc.GetString("zakat-diagnosis-subject", ("name", swab.Comp.SubjectName)) + "\n\n"
                   + Loc.GetString(swab.Comp.Zakat != null ? "zakat-diagnosis-positive" : "zakat-diagnosis-negative");

        var paper = Spawn(ent.Comp.PaperPrototype, Transform(ent).Coordinates);
        _paper.SetContent((paper, EnsureComp<PaperComponent>(paper)), text);
        return paper;
    }

    public EntityUid? MakeVaccine(Entity<ZakatVirologyComponent> ent, Entity<ZakatSwabComponent> swab)
    {
        if (swab.Comp.Zakat == null)
        {
            _popup.PopupEntity(Loc.GetString("zakat-vaccinator-clean"), ent, PopupType.Medium);
            return null;
        }

        var vaccine = Spawn(ent.Comp.VaccinePrototype, Transform(ent).Coordinates);
        var comp = EnsureComp<ZakatVaccineComponent>(vaccine);
        comp.Subject = swab.Comp.Subject;
        comp.SubjectName = swab.Comp.SubjectName;
        comp.Zakat = swab.Comp.Zakat;
        Dirty(vaccine, comp);
        return vaccine;
    }

    #endregion

    #region vaccine

    private void OnVaccineExamined(Entity<ZakatVaccineComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Used)
        {
            args.PushMarkup(Loc.GetString("zakat-vaccine-used"));
            return;
        }

        if (ent.Comp.Subject != null)
            args.PushMarkup(Loc.GetString("zakat-vaccine-subject", ("name", ent.Comp.SubjectName)));
    }

    private void OnVaccineInteract(Entity<ZakatVaccineComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryInject(ent, target, args.User);
    }

    private void OnVaccineUse(Entity<ZakatVaccineComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryInject(ent, args.User, args.User);
    }

    /// <summary>
    /// The shot. Only the person the swab came from; slaves, praetorians and creatures are beyond help.
    /// </summary>
    public bool TryInject(Entity<ZakatVaccineComponent> ent, EntityUid target, EntityUid user)
    {
        if (ent.Comp.Used)
        {
            _popup.PopupEntity(Loc.GetString("zakat-vaccine-used"), target, user);
            return false;
        }

        if (!HasComp<DiseaseCarrierComponent>(target))
            return false;

        if (ent.Comp.Subject != target)
        {
            _popup.PopupEntity(Loc.GetString("zakat-vaccine-wrong"), target, user);
            return false;
        }

        ent.Comp.Used = true;
        Dirty(ent);
        _audio.PlayPvs(InjectSound, target);
        _popup.PopupEntity(Loc.GetString("zakat-vaccine-injected", ("target", Identity.Entity(target, EntityManager))), target, user);

        Treat(target, ent.Comp.Rollback);
        QueueDel(ent);
        return true;
    }

    /// <summary>
    /// Starts the treatment. Returns false when there is nothing the vaccine can do.
    /// </summary>
    public bool Treat(EntityUid target, int rollback = 2)
    {
        if (!TryComp<ZakatHostComponent>(target, out var host)
            || host.Enslaved
            || HasComp<ZakatSlaveComponent>(target)
            || HasComp<ZakatPraetorianComponent>(target)
            || HasComp<ZakatCreatureComponent>(target)
            || FindZakat(target, out _) == null)
            return false;

        var treated = EnsureComp<ZakatVaccinatedComponent>(target);
        treated.Zakat = host.Zakat;
        if (host.Symptoms.Count == 0)
        {
            treated.Cure = true;
            treated.NextAt = _timing.CurTime + CureTime;
        }
        else
        {
            if (treated.Rollback == 0 && !treated.Cure)
                treated.NextAt = _timing.CurTime + RollbackTime;
            treated.Rollback += rollback;
        }

        return true;
    }

    /// <summary>
    /// Symptoms the host can lose: nothing further along the same branch depends on them.
    /// </summary>
    public static IEnumerable<ZakatSymptom> Tips(ZakatHostComponent host)
    {
        return host.Symptoms.Where(s => !host.Symptoms.Any(o => ZakatSymptoms.Previous(o) == s));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ZakatVaccinatedComponent>();
        while (query.MoveNext(out var uid, out var treated))
        {
            if (now < treated.NextAt)
                continue;

            Step(uid, treated);
        }
    }

    public void Step(EntityUid uid, ZakatVaccinatedComponent treated)
    {
        // turned into a slave or a praetorian meanwhile, or already free of it
        if (!TryComp<ZakatHostComponent>(uid, out var host)
            || host.Enslaved
            || HasComp<ZakatPraetorianComponent>(uid)
            || FindZakat(uid, out var disease) is not { } zakat
            || disease == null)
        {
            RemCompDeferred<ZakatVaccinatedComponent>(uid);
            return;
        }

        if (treated.Cure)
        {
            if (host.Symptoms.Count == 0)
            {
                _disease.TryCure(uid, disease.Value);
                _popup.PopupEntity(Loc.GetString("zakat-vaccine-cured"), uid, uid, PopupType.Medium);
            }

            RemCompDeferred<ZakatVaccinatedComponent>(uid);
            if (TryComp<ZakatComponent>(zakat, out var zakatComp))
                _zakat.UpdateUis((zakat, zakatComp));
            return;
        }

        if (treated.Rollback > 0 && Tips(host).Select(x => (ZakatSymptom?) x).FirstOrDefault() is { } tip)
        {
            // the newest growth along a branch recedes first
            _symptoms.RemoveSymptom((uid, host), tip);
            treated.Rollback--;
            _popup.PopupEntity(Loc.GetString("zakat-vaccine-rollback"), uid, uid, PopupType.Medium);
            if (TryComp<ZakatComponent>(zakat, out var zakatComp))
                _zakat.UpdateUis((zakat, zakatComp));
        }
        else
        {
            treated.Rollback = 0;
        }

        if (treated.Rollback <= 0)
            RemCompDeferred<ZakatVaccinatedComponent>(uid);
        else
            treated.NextAt = _timing.CurTime + RollbackTime;
    }

    #endregion
}
