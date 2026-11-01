using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.EUI;
using Content.Shared._SW._Senate.Press;
using Content.Shared._SW._Senate.Planner;
using Content.Shared.Eui;
using Content.Shared.Interaction;

namespace Content.Server._SW._Senate.Press;

public sealed class SWPressEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entities = default!;
    private readonly EntityUid _terminal;
    private readonly bool _publicOnly;
    public EntityUid Terminal => _terminal;
    private string _notice = string.Empty;
    private int _historyId;
    private string _lastAccess = string.Empty;
    private SWSenatePressSystem Press => _entities.System<SWSenatePressSystem>();
    private SWPressRecorderSystem Recorder => _entities.System<SWPressRecorderSystem>();
    private SWPressMailSystem Mail => _entities.System<SWPressMailSystem>();
    private SWPressGuestPolicySystem GuestPolicy => _entities.System<SWPressGuestPolicySystem>();
    private SWSenatePlannerSystem Planner => _entities.System<SWSenatePlannerSystem>();
    private SWSenateTimeSystem Time => _entities.System<SWSenateTimeSystem>();
    private SharedInteractionSystem Interaction => _entities.System<SharedInteractionSystem>();

    public SWPressEui(EntityUid terminal, bool publicOnly = false)
    {
        _terminal = terminal;
        _publicOnly = publicOnly;
        IoCManager.InjectDependencies(this);
    }

    public override void Opened() { base.Opened(); Press.PublicationsChanged += StateDirty; Recorder.Changed += StateDirty; GuestPolicy.Changed += StateDirty; GuestPolicy.AccessPoll += CheckAccess; Mail.Changed += StateDirty; StateDirty(); }
    public override void Closed() { Press.PublicationsChanged -= StateDirty; Recorder.Changed -= StateDirty; GuestPolicy.Changed -= StateDirty; GuestPolicy.AccessPoll -= CheckAccess; Mail.Changed -= StateDirty; _entities.System<SWPressTerminalSystem>().Forget(Player.UserId, this); base.Closed(); }

    private bool TryActor(out EntityUid actor)
    {
        actor = default;
        if (Player.AttachedEntity is not { } body || !_entities.EntityExists(_terminal)
            || !Interaction.InRangeUnobstructed(body, _terminal))
            return false;
        actor = body;
        return true;
    }

    private string AccessFingerprint()
    {
        if (!TryActor(out var actor)) return "unavailable";
        if (_publicOnly) return $"public|{actor}";
        var guest = string.Join(",", Planner.GetGuestRequests(actor).Select(g => $"{g.Id}:{g.Status}:{g.Stale}:{g.Revision}"));
        var mail = string.Join(",", Mail.GetMail(actor).Select(m => $"{m.Id}:{m.Messages.Count}"));
        var targets = string.Join(",", GuestPolicy.GetTargets(actor).Select(t => t.Key + ":" + t.Label));
        var mailTargets = string.Join(",", GuestPolicy.GetMailTargets(actor).Select(t => t.Key + ":" + t.Label));
        return $"{actor}|{Press.TryGetCorrespondent(actor, out _)}|{Press.TryGetOfficialOffice(actor, out _)}|{Press.TryLivePressActor(actor, out _)}|{guest}|{mail}|{targets}|{mailTargets}";
    }

    private void CheckAccess()
    {
        if (AccessFingerprint() != _lastAccess) StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        _lastAccess = AccessFingerprint();
        var state = new SWPressEuiState { Notice = _notice, PublicOnly = _publicOnly };
        if (!TryActor(out var actor))
            return state;
        if (!_publicOnly)
        {
            state.CanMail = Press.TryLivePressActor(actor, out _);
            foreach (var target in GuestPolicy.GetMailTargets(actor))
                state.MailTargets.Add(new SWPressGuestTarget { Key = target.Key, Label = target.Label });
            foreach (var thread in Mail.GetMail(actor))
            {
                var entry = new SWPressMailEntry { Id = thread.Id, Subject = thread.Subject,
                    FirstName = thread.FirstName, SecondName = thread.SecondName };
                foreach (var message in thread.Messages)
                    entry.Messages.Add(new SWPressMailMessageEntry { Author = message.Author, Text = message.Text, Time = message.Time });
                state.Mail.Add(entry);
            }
            state.Today = Time.Day;
            foreach (var target in GuestPolicy.GetTargets(actor))
                state.GuestTargets.Add(new SWPressGuestTarget { Key = target.Key, Label = target.Label });
            foreach (var guest in Planner.GetGuestRequests(actor))
            {
                if (guest.Kind != SWGuestRequestKind.Press) continue;
                state.GuestRequests.Add(new SWPressGuestEntry { Id = guest.Id, Revision = guest.Revision, Day = guest.Day, StartSlot = guest.StartSlot,
                    Duration = guest.Duration, Subject = guest.Subject, Description = guest.Description, Location = guest.Location,
                    Host = guest.Host, Target = guest.Target, Status = (int) guest.Status, IsOwner = guest.IsOwner,
                    IsTarget = guest.IsTarget, Stale = guest.Stale });
            }
            state.CanWrite = Press.TryGetCorrespondent(actor, out _);
            state.CanRelease = Press.TryGetOfficialOffice(actor, out var office);
            state.Office = office;
            state.IsRecording = Recorder.IsRecording(actor, _terminal);
            foreach (var quote in Recorder.GetQuotes(actor))
                state.RecordedQuotes.Add(QuoteView(quote));
            if (Press.TryGetEdition(actor, out var edition))
                state.Edition = (int) edition;
            foreach (var draft in Press.GetDrafts(actor))
            {
                var entry = new SWPressDraftEntry { Id = draft.Id, Revision = draft.Revision, Title = draft.Title, Body = draft.Body };
                foreach (var quote in draft.Quotes) entry.Quotes.Add(QuoteView(quote));
                state.Drafts.Add(entry);
            }
        }
        foreach (var release in Press.GetReleases())
            state.Releases.Add(new SWPressReleaseEntry { Id = release.Id, Title = release.Title,
                Body = release.Body, Author = release.Author, Office = release.Office, Time = release.Time });
        foreach (var publication in Press.GetPublications())
        {
            var version = publication.Versions[^1];
            var entry = new SWPressPublicationEntry
            {
                Id = publication.Id, Version = version.Number, Edition = (int) publication.Edition,
                Author = publication.Author, Title = version.Title, Body = version.Withdrawn ? string.Empty : version.Body,
                Time = version.Time, Withdrawn = version.Withdrawn, CanEdit = !_publicOnly && Press.CanEditPublication(actor, publication.Id),
            };
            if (!version.Withdrawn)
                foreach (var quote in version.Quotes) entry.Quotes.Add(QuoteView(quote));
            state.Publications.Add(entry);
        }
        if (_historyId != 0)
        {
            var publication = Press.GetPublications().FirstOrDefault(p => p.Id == _historyId);
            if (publication != null && !publication.Versions[^1].Withdrawn)
            {
                state.HistoryId = publication.Id; state.HistoryAuthor = publication.Author;
                state.HistoryEdition = (int) publication.Edition;
                foreach (var version in publication.Versions)
                {
                    var history = new SWPressHistoryEntry { Number = version.Number, Title = version.Title,
                        Body = version.Body, Time = version.Time };
                    foreach (var quote in version.Quotes) history.Quotes.Add(QuoteView(quote));
                    state.History.Add(history);
                }
            }
            else _historyId = 0;
        }
        return state;
    }

    private static SWPressQuoteEntry QuoteView(PressQuote quote) => new()
    {
        Id = quote.Id, DisplayName = quote.DisplayName, Text = quote.Text, Time = quote.Time,
    };

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is not SWPressActionMessage action || !TryActor(out var actor))
            return;
        if (_publicOnly && action.Action is not (SWPressAction.Refresh or SWPressAction.HistoryOpen
                or SWPressAction.HistoryClose))
            return;
        if (action.Action is SWPressAction.HistoryOpen or SWPressAction.HistoryClose)
        {
            _historyId = action.Action == SWPressAction.HistoryOpen ? action.Id : 0;
            StateDirty();
            return;
        }
        if (action.Action is SWPressAction.GuestCreate or SWPressAction.GuestRespond or SWPressAction.GuestCancel or SWPressAction.GuestReschedule)
        {
            var error = string.Empty;
            var changed = action.Action switch
            {
                SWPressAction.GuestCreate => Planner.TryCreateGuestRequest(actor, SWGuestRequestKind.Press, action.TargetKey,
                    action.Day, action.Slot, action.Duration, action.Title, action.Body, action.Location, out _, out error),
                SWPressAction.GuestRespond => Planner.TryRespondGuestRequest(actor, action.Id, action.Revision, action.Accept, out error),
                SWPressAction.GuestCancel => Planner.TryCancelGuestRequest(actor, action.Id, action.Revision, out error),
                SWPressAction.GuestReschedule => Planner.TryRescheduleGuestRequest(actor, action.Id, action.Revision, action.Day, action.Slot, action.Duration, out error),
                _ => false,
            };
            _notice = changed ? "Запрос встречи обновлён." : string.IsNullOrEmpty(error)
                ? "Не удалось изменить встречу: проверьте полномочия, участников и время." : error;
            if (changed) GuestPolicy.NotifyChanged();
            StateDirty();
            return;
        }
        var success = action.Action switch
        {
            SWPressAction.Refresh => true,
            SWPressAction.LeaveEdition => Press.LeaveEdition(actor),
            SWPressAction.SelectEdition => action.Edition >= 0 && action.Edition < 4
                && Press.SelectEdition(actor, (PressEdition) action.Edition),
            SWPressAction.Save => Press.SaveDraft(actor, action.Id, action.Revision, action.Title, action.Body),
            SWPressAction.Delete => Press.DeleteDraft(actor, action.Id, action.Revision),
            SWPressAction.Publish => Press.Publish(actor, action.Id, action.Revision),
            SWPressAction.Correct => Press.Correct(actor, action.Id, action.Revision, action.Title, action.Body),
            SWPressAction.Withdraw => Press.Withdraw(actor, action.Id, action.Revision),
            SWPressAction.ToggleRecording => Recorder.Toggle(actor, _terminal),
            SWPressAction.AttachQuote => Press.AttachQuote(actor, action.Id, action.Revision, action.QuoteId),
            SWPressAction.RemoveQuote => Press.RemoveQuote(actor, action.Id, action.Revision, action.QuoteId),
            SWPressAction.SendRelease => Press.PublishRelease(actor, action.Title, action.Body),
            SWPressAction.MailCreate => Mail.Create(actor, action.TargetKey, action.Title, action.Body),
            SWPressAction.MailReply => Mail.Reply(actor, action.Id, action.Body),
            SWPressAction.MailCopyToDraft => Mail.CopyToDraft(actor, action.Id),
            _ => false,
        };
        _notice = success ? "Готово." : "Действие не выполнено: проверьте права, текст и актуальность записи.";
        StateDirty();
    }
}
