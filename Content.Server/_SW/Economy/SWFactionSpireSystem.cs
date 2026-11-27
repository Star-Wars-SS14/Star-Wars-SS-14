using Content.Server._SW.Economy.BankSystem;
using Content.Server.Access.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Access.Components;
using Content.Shared.Containers;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.Economy;

/// <summary>
/// Server-side spire logic: capture timers, payouts, radio warning, sounds, BUI state.
/// Sprite layers and glow are applied client-side from the networked fields.
/// Capture gate is card-only: the inserted card's PresetIdCard job decides the faction.
/// UI button sounds are server pings: beep_board on any button press, spire_cancel
/// only when the card is pulled while a rewrite is in progress.
/// </summary>
public sealed class SWFactionSpireSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SWBankSystem _bank = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    /// <summary>
    /// How often the BUI state is re-pushed while something is in progress, so the
    /// progress bars and countdowns move without waiting for a transition.
    /// </summary>
    private const float PushInterval = 0.5f;

    private static readonly Dictionary<string, string> FactionChannel = new()
    {
        { "RepublicFaction", "SWRepublic" },
        { "EmpireFaction", "SWEmpireRegular" },
        { "MandalorianFaction", "SWMandalorian" },
        { "ExchangeFaction", "SWExchange" },
        { "BlackSunFaction", "SWBlackSun" },
    };

    private static readonly SoundPathSpecifier RewritingSound = new("/Audio/_SW/Spire/rewriting.ogg");
    private static readonly SoundPathSpecifier EndSound = new("/Audio/_SW/Spire/rewriting_end.ogg");
    private static readonly SoundPathSpecifier CancelSound = new("/Audio/_SW/Spire/spire_cancel.ogg");
    private static readonly SoundPathSpecifier BeepSound = new("/Audio/_SW/board/beep_board.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFactionSpireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWFactionSpireComponent, SWFactionSpireCaptureMessage>(OnCaptureMessage);
        SubscribeLocalEvent<SWFactionSpireComponent, SWFactionSpireEjectMessage>(OnEjectMessage);
        SubscribeLocalEvent<SWFactionSpireComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<SWFactionSpireComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<SWFactionSpireComponent> ent, ref MapInitEvent args)
    {
        // Single prototype, many uniquely named spires: apply the per-placement
        // name on the server so radio warnings report exactly this entity name.
        if (!string.IsNullOrWhiteSpace(ent.Comp.SpireName))
            _metaData.SetEntityName(ent.Owner, ent.Comp.SpireName);

        ent.Comp.HadCard = TryGetCard(ent, out _);
        Dirty(ent.Owner, ent.Comp);
        PushState(ent);
    }

    private void OnShutdown(Entity<SWFactionSpireComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.RewritingStream = _audio.Stop(ent.Comp.RewritingStream);
    }

    private void OnCaptureMessage(Entity<SWFactionSpireComponent> ent, ref SWFactionSpireCaptureMessage args)
    {
        // Any capture-button press beeps, even when the attempt is refused below.
        _audio.PlayPvs(BeepSound, ent.Owner);

        var comp = ent.Comp;
        if (comp.IsCapturing)
            return;

        if (!TryGetCard(ent, out var cardUid))
        {
            _popup.PopupEntity(Loc.GetString("sw-spire-no-card"), ent.Owner, args.Actor);
            return;
        }

        // The card alone decides the faction: preset/player cards carry their job
        // in PresetIdCardComponent.JobName (server-only), never in IdCard.JobPrototype.
        if (!TryComp<PresetIdCardComponent>(cardUid, out var preset) || preset.JobName is not { } cardJob)
        {
            _popup.PopupEntity(Loc.GetString("sw-spire-card-no-job"), ent.Owner, args.Actor);
            return;
        }

        if (!TryMatchFaction(cardJob, out var factionId, out var factionName))
        {
            _popup.PopupEntity(Loc.GetString("sw-spire-no-access"), ent.Owner, args.Actor);
            return;
        }

        if (factionId == comp.OwnerFaction)
        {
            _popup.PopupEntity(Loc.GetString("sw-spire-own-faction"), ent.Owner, args.Actor);
            return;
        }

        comp.CapturingFaction = factionId;
        comp.IsCapturing = true;
        comp.CaptureProgress = 0f;

        comp.RewritingStream = _audio.PlayPvs(RewritingSound, ent.Owner, RewritingSound.Params.WithLoop(true))?.Entity;

        // Warn the current owner over its faction radio, naming this exact spire.
        if (comp.OwnerFaction != string.Empty && FactionChannel.TryGetValue(comp.OwnerFaction, out var channel))
        {
            var message = Loc.GetString("sw-spire-under-attack",
                ("spire", MetaData(ent.Owner).EntityName),
                ("faction", factionName));
            _radio.SendRadioMessage(ent.Owner, message, channel, ent.Owner, false);
        }

        Dirty(ent.Owner, comp);
        PushState(ent);
    }

    private void OnEjectMessage(Entity<SWFactionSpireComponent> ent, ref SWFactionSpireEjectMessage args)
    {
        // Sample the capture flag BEFORE ejecting: spire_cancel plays only when the
        // card is pulled mid-rewrite, a plain eject beeps instead.
        var wasCapturing = ent.Comp.IsCapturing;
        if (!TryEject(ent, args.Actor))
            return;

        _audio.PlayPvs(wasCapturing ? CancelSound : BeepSound, ent.Owner);
        PushState(ent);
    }

    private void OnGetVerbs(Entity<SWFactionSpireComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryGetCard(ent, out _))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("sw-spire-eject-verb"),
            Act = () =>
            {
                var wasCapturing = ent.Comp.IsCapturing;
                if (!TryEject(ent, user))
                    return;

                _audio.PlayPvs(wasCapturing ? CancelSound : BeepSound, ent.Owner);
                PushState(ent);
            },
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWFactionSpireComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = new Entity<SWFactionSpireComponent>(uid, comp);
            var changed = false;

            // Single-card invariant: drop anything beyond the first inserted card.
            EjectExtraCards(ent);

            // Card presence changed (inserted or ejected) => refresh the UI so the
            // capture button and card panel react immediately.
            var hasCard = TryGetCard(ent, out _);
            if (hasCard != comp.HadCard)
            {
                comp.HadCard = hasCard;
                PushState(ent);
            }

            // Card removed mid-capture => abort (anyone can pull the card).
            if (comp.IsCapturing && !hasCard)
            {
                comp.IsCapturing = false;
                comp.CapturingFaction = string.Empty;
                comp.CaptureProgress = 0f;
                comp.RewritingStream = _audio.Stop(comp.RewritingStream);
                changed = true;
            }

            if (comp.IsCapturing)
            {
                comp.CaptureProgress += frameTime / comp.CaptureTime;
                if (comp.CaptureProgress >= 1f)
                {
                    comp.OwnerFaction = comp.CapturingFaction;
                    comp.CapturingFaction = string.Empty;
                    comp.IsCapturing = false;
                    comp.CaptureProgress = 0f;
                    comp.PayoutProgress = 0f; // payout restarts under the new owner
                    comp.RewritingStream = _audio.Stop(comp.RewritingStream);
                    _audio.PlayPvs(EndSound, uid);
                    changed = true;
                }
            }
            else if (comp.OwnerFaction != string.Empty)
            {
                // Payout is frozen while capturing (this branch is skipped then).
                comp.PayoutProgress += frameTime / comp.PayoutInterval;
                if (comp.PayoutProgress >= 1f)
                {
                    comp.PayoutProgress = 0f;
                    comp.BeamActive = true;
                    comp.BeamTimer = comp.BeamDuration;
                    Pay(ent);
                    changed = true;
                }
            }

            if (comp.BeamActive)
            {
                comp.BeamTimer -= frameTime;
                if (comp.BeamTimer <= 0f)
                {
                    comp.BeamActive = false;
                    changed = true;
                }
            }

            // Periodic UI refresh so bars/counters move between transitions.
            if (comp.IsCapturing || comp.OwnerFaction != string.Empty)
            {
                comp.PushTimer += frameTime;
                if (comp.PushTimer >= PushInterval)
                {
                    comp.PushTimer = 0f;
                    PushState(ent);
                }
            }

            if (changed)
            {
                Dirty(ent.Owner, comp);
                PushState(ent);
            }
        }
    }

    /// <summary>
    /// Keeps the slot single-card: the 2-cell grid already blocks a second card,
    /// this is a belt-and-braces guard against orientation/shape quirks.
    /// </summary>
    private void EjectExtraCards(Entity<SWFactionSpireComponent> ent)
    {
        if (!TryComp<ContainerManagerComponent>(ent, out var containers))
            return;
        if (!containers.Containers.TryGetValue("storagebase", out var slot))
            return;

        while (slot.ContainedEntities.Count > 1)
        {
            _containers.Remove(slot.ContainedEntities[^1], slot);
        }
    }

    private bool TryEject(Entity<SWFactionSpireComponent> ent, EntityUid actor)
    {
        if (!TryGetCard(ent, out var card))
            return false;
        if (!TryComp<ContainerManagerComponent>(ent, out var containers))
            return false;
        if (!containers.Containers.TryGetValue("storagebase", out var container))
            return false;

        _containers.Remove(card, container);

        // Prefer handing the card straight to the actor; fall back to dropping it.
        _hands.TryPickupAnyHand(actor, card);
        return true;
    }

    private void Pay(Entity<SWFactionSpireComponent> ent)
    {
        if (!TryFindFactionEntity(ent.Comp.OwnerFaction, out var factionEnt))
            return;
        if (!TryComp<SWBankAccountContainerComponent>(factionEnt, out var account))
            return;

        var add = new Dictionary<ProtoId<SWCurrencyPrototype>, float>
        {
            { ent.Comp.PayoutCurrency, ent.Comp.PayoutAmount },
        };
        account.CurrentAmount = _bank.Sum(account.CurrentAmount, add);
        _bank.UpdateAllInterfaces();
    }

    private bool TryGetCard(Entity<SWFactionSpireComponent> ent, out EntityUid card)
    {
        card = default;
        if (!TryComp<ContainerManagerComponent>(ent, out var containers))
            return false;
        if (!containers.Containers.TryGetValue("storagebase", out var container))
            return false;
        if (container.ContainedEntities.Count == 0)
            return false;
        card = container.ContainedEntities[0];
        return true;
    }

    /// <summary>
    /// Job-to-faction match over entity prototypes (SWFactionComponent.RelatedJobs),
    /// independent of whether faction entities are spawned this round.
    /// </summary>
    private bool TryMatchFaction(ProtoId<JobPrototype> job, out string factionId, out string factionName)
    {
        factionId = string.Empty;
        factionName = string.Empty;
        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<SWFactionComponent>(out var faction, _componentFactory))
                continue;
            if (!faction.RelatedJobs.Contains(job))
                continue;

            factionId = proto.ID;
            factionName = faction.Name;
            return true;
        }
        return false;
    }

    private bool TryFindFactionEntity(string factionId, out EntityUid uid)
    {
        uid = default;
        var query = EntityQueryEnumerator<SWFactionComponent>();
        while (query.MoveNext(out var candidate, out _))
        {
            if (MetaData(candidate).EntityPrototype?.ID == factionId)
            {
                uid = candidate;
                return true;
            }
        }
        return false;
    }

    private void PushState(Entity<SWFactionSpireComponent> ent)
    {
        var comp = ent.Comp;
        var state = new SWFactionSpireBoundUserInterfaceState
        {
            OwnerFaction = comp.OwnerFaction,
            OwnerName = comp.OwnerFaction == string.Empty ? Loc.GetString("sw-spire-neutral") : NameOf(comp.OwnerFaction),
            CapturingName = comp.CapturingFaction == string.Empty ? string.Empty : NameOf(comp.CapturingFaction),
            IsCapturing = comp.IsCapturing,
            CaptureProgress = comp.CaptureProgress,
            PayoutProgress = comp.PayoutProgress,
            CaptureRemainingSeconds = comp.IsCapturing ? comp.CaptureTime * (1f - comp.CaptureProgress) : 0f,
            PayoutRemainingSeconds = comp.OwnerFaction != string.Empty ? comp.PayoutInterval * (1f - comp.PayoutProgress) : 0f,
        };

        if (TryGetCard(ent, out var card) && TryComp<IdCardComponent>(card, out var id))
        {
            state.HasCard = true;
            state.CardName = id.FullName ?? string.Empty;
            state.CardJob = id.LocalizedJobTitle ?? string.Empty;
            state.CardIcon = id.JobIcon;
        }

        _ui.SetUiState(ent.Owner, SWFactionSpireUiKey.Key, state);
    }

    private string NameOf(string factionId)
    {
        if (_prototype.TryIndex<EntityPrototype>(factionId, out var proto) &&
            proto.TryGetComponent<SWFactionComponent>(out var faction, _componentFactory))
        {
            return faction.Name;
        }
        return factionId;
    }
}