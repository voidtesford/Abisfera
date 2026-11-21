using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Chat;
using Content.Shared.Holopad;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._CorvaxGoob.Pulsar;

/// <summary>
/// Hearing.
/// Radio: while active the core gets an ActiveRadio listening on every channel whose encryption key sits in one of
/// the station's telecom servers (re-read every <see cref="PulsarAutonomousAiComponent.StationRefreshInterval"/>).
/// The normal radio system delivers the messages, so the usual rules apply (a working telecom server is needed for
/// non-long-range channels, jammers etc.).
/// Holopads: normal speech within <see cref="PulsarAutonomousAiComponent.HolopadHearingRange"/> tiles of a powered,
/// anchored station holopad. Whispers (and speech into a headset, which is a whisper) are never heard.
/// The same line may arrive both by radio and at a holopad (or from several pads/radios); it is logged each time
/// and understood once (see <see cref="PulsarAutonomousAiComponent.UtteranceDedupeWindow"/>).
/// </summary>
public sealed partial class PulsarAutonomousAiSystem
{
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;

    private void InitializeHearing()
    {
        SubscribeLocalEvent<PulsarAutonomousAiComponent, RadioReceiveEvent>(OnRadioReceive);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
    }

    private void RefreshChannels(Entity<PulsarAutonomousAiComponent> ent)
    {
        var comp = ent.Comp;
        comp.Channels.Clear();

        var query = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var keys, out var xform))
        {
            if (xform.GridUid is not { } grid || !comp.StationGrids.Contains(grid))
                continue;

            foreach (var channel in keys.Channels)
                comp.Channels.Add(channel);
        }

        if (!TryComp<ActiveRadioComponent>(ent, out var radio) || !comp.AddedRadio)
            return;

        radio.Channels.Clear();
        foreach (var channel in comp.Channels)
            radio.Channels.Add(channel);
    }

    private void RefreshHolopads(Entity<PulsarAutonomousAiComponent> ent)
    {
        var comp = ent.Comp;
        comp.Holopads.Clear();

        var query = EntityQueryEnumerator<HolopadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && comp.StationGrids.Contains(grid))
                comp.Holopads.Add(uid);
        }
    }

    private void OnRadioReceive(Entity<PulsarAutonomousAiComponent> ent, ref RadioReceiveEvent args)
    {
        if (ent.Comp.State != PulsarState.Active || args.MessageSource == ent.Owner)
            return;

        HandleSpeech(ent, args.MessageSource, args.OriginalChatMsg.Message, args.Channel.ID);
        Hear(ent, new PulsarHeardLine(
            _timing.CurTime,
            PulsarHeardKind.Radio,
            args.Channel.ID,
            args.MessageSource,
            SpeakerName(args.MessageSource),
            args.OriginalChatMsg.Message,
            args.Language.ID,
            null));
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        if (args.IsWhisper)
            return;

        // Its own replies are spoken from holopads / the core.
        if (HasComp<HolopadComponent>(args.Source) || HasComp<StationAiCoreComponent>(args.Source))
            return;

        if (!TryComp(args.Source, out TransformComponent? sourceXform))
            return;

        var sourcePos = _xforms.GetWorldPosition(sourceXform);
        var mapId = sourceXform.MapID;

        var query = EntityQueryEnumerator<PulsarAutonomousAiComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.State != PulsarState.Active || uid == args.Source)
                continue;

            var rangeSq = comp.HolopadHearingRange * comp.HolopadHearingRange;
            foreach (var pad in comp.Holopads)
            {
                if (!TryComp(pad, out TransformComponent? padXform) || padXform.MapID != mapId || !padXform.Anchored)
                    continue;

                if ((_xforms.GetWorldPosition(padXform) - sourcePos).LengthSquared() > rangeSq)
                    continue;

                if (!_power.IsPowered(pad))
                    continue;

                HandleSpeech((uid, comp), args.Source, args.Message, holopad: pad);
                Hear((uid, comp), new PulsarHeardLine(
                    _timing.CurTime,
                    PulsarHeardKind.Holopad,
                    null,
                    args.Source,
                    SpeakerName(args.Source),
                    args.Message,
                    args.Language.ID,
                    pad));
                break;
            }
        }
    }

    /// <summary>The name the speaker is heard as (voice masks etc.), like the chat does it.</summary>
    private string SpeakerName(EntityUid speaker)
    {
        if (TerminatingOrDeleted(speaker))
            return string.Empty;

        var ev = new TransformSpeakerNameEvent(speaker, MetaData(speaker).EntityName);
        RaiseLocalEvent(speaker, ev);
        return ev.VoiceName;
    }

    private void Hear(Entity<PulsarAutonomousAiComponent> ent, PulsarHeardLine line)
    {
        var heard = ent.Comp.Heard;
        heard.Add(line);
        var excess = heard.Count - Math.Max(1, ent.Comp.HeardLimit);
        if (excess > 0)
            heard.RemoveRange(0, excess);

        var ev = new PulsarHeardEvent(line);
        RaiseLocalEvent(ent, ref ev);
    }
}
