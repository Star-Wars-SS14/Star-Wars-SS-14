using System.Linq;
using Content.Shared._SW._Senate.Holonet;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Holonet;

/// <summary>
/// Forms live on their own screens and are not rebuilt by state updates. Text is removed in only three ways:
/// a confirmation of the exact version sent, the player's own delete button, or another character taking over.
/// A full store refuses a new draft and keeps every draft that holds text.
/// </summary>
public sealed partial class SWHolonetView
{
    private const int MaxDraftsPerKind = 8;
    private const float SendTimeoutSeconds = 10f;

    private enum DraftKind : byte
    {
        Thread,
        ForumReply,
        Mail,
        MailReply,
    }

    private readonly List<Draft> _drafts = new();
    private Draft? _activeDraft;

    /// <summary>The form to bring back when access returns to the same Mind.</summary>
    private Draft? _resumeDraft;

    /// <summary>Whose drafts these are. Stays set while access is denied, since the same Mind may come back.</summary>
    private string _draftOwner = string.Empty;

    private LineEdit? _titleEdit;
    private SWConsoleTextEdit? _bodyEdit;
    private LineEdit? _searchEdit;
    private BoxContainer? _formDynamic;
    private Label? _recipientLabel;
    private Button? _submitButton;
    private Button? _deleteButton;
    private Control? _sendingNotice;

    private bool FormOpen => _activeDraft != null;

    /// <summary>
    /// Another character on this client gets nothing typed by the previous one. A denied state has no key
    /// and changes nothing, because lost access alone must not cost the same Mind its text.
    /// </summary>
    private void ApplyPersona(SWHolonetStateEvent state)
    {
        if (!state.Private || state.PersonaKey == _draftOwner)
            return;

        var changed = _draftOwner.Length > 0;
        _draftOwner = state.PersonaKey;
        if (!changed)
            return;

        _drafts.Clear();
        _activeDraft = null;
        _resumeDraft = null;
        _nav.ArticleId = 0;
        _nav.ThreadId = 0;
        _nav.MailBox = string.Empty;
        _nav.MessageId = 0;
        _nav.WantDirectory = false;
        _nav.Search = string.Empty;
    }

    /// <summary>
    /// Takes private content off the screen the moment the controlled entity changes, before the server has
    /// said who is in control now. The drafts stay in memory until a state proves whose they are.
    /// </summary>
    public void HidePrivateContent()
    {
        SuspendForm();
        _context = 0;
        _epoch = _holonet?.NextEpoch() ?? _epoch + 1;

        // These belonged to the context that just ended; the next one starts from its own mailbox.
        _nav.MailBox = string.Empty;
        _nav.Folder = SWHolonetMailFolder.Inbox;
        _nav.MailPage = 0;
        _nav.MessageId = 0;
        _nav.WantDirectory = false;
        _nav.Search = string.Empty;
        _nav.DirectoryPage = 0;

        // Any answer still on its way is now discarded, so nothing would ever unlock these otherwise.
        // A retry stays safe: same Mind and same text are recognized by the request id.
        foreach (var draft in _drafts)
            draft.Pending = false;

        _state = null;
        _pendingScroll = null;
        ClearContent();
        Show(new Label { Text = Loc.GetString("sw-holonet-connecting"), FontColorOverride = SWHolonetPalette.Dim });
        _persona.Text = string.Empty;
        _role.Text = string.Empty;
        _noticeBar.Visible = false;
        foreach (var button in _tabs.Values)
            SWConsoleStyle.StyleTabButton(button, false);
    }

    /// <summary>The new epoch makes the server end whatever it bound before hearing from this view.</summary>
    public void OnActorAttached()
    {
        HidePrivateContent();
        SendNavigation();
    }

    /// <summary>
    /// A notice for an older number than the one on screen is stale, and with no context on screen the view is
    /// already hidden. Ignoring both keeps the re-navigation below from triggering another revocation in a loop.
    /// </summary>
    public void OnContextRevoked(ulong context)
    {
        if (_context == 0 || context < _context)
            return;

        HidePrivateContent();
        SendNavigation();
    }

    /// <summary>Closes the form on screen so that it can return with its text once access is back.</summary>
    private void SuspendForm()
    {
        StoreForm();
        if (_activeDraft != null)
            _resumeDraft = _activeDraft;
        _activeDraft = null;
    }

    private bool TryResumeForm()
    {
        if (_resumeDraft is not { } draft)
            return false;

        _resumeDraft = null;
        if (!_drafts.Contains(draft))
            return false;

        OpenDraft(draft);
        return true;
    }

    /// <summary>
    /// A refusal unlocks the form. A confirmation removes the draft only while it still equals the version
    /// that was sent, because after a timeout the player may have edited it. Returns whether the form closed.
    /// </summary>
    private bool ApplyAcknowledgement(SWHolonetStateEvent state)
    {
        if (state.Ack == Guid.Empty)
            return false;

        var draft = _drafts.Find(d => d.RequestId == state.Ack);
        if (draft == null)
            return false;

        draft.Pending = false;
        var active = draft == _activeDraft;
        if (!state.AckOk)
        {
            if (active)
                UpdateFormLock();
            return false;
        }

        if (active)
            StoreForm();

        // A confirmation only counts under the context that issued the action.
        if (draft.SentContext != state.Context)
        {
            if (active)
                UpdateFormLock();
            return false;
        }

        if (draft.Content() != draft.SentContent)
        {
            draft.RequestId = Guid.NewGuid();
            draft.SentContent = null;
            ShowLocalNotice(Loc.GetString("sw-holonet-draft-edited-after-send"));
            if (active)
                UpdateFormLock();
            return false;
        }

        _drafts.Remove(draft);
        if (_resumeDraft == draft)
            _resumeDraft = null;
        if (!active)
            return false;

        _activeDraft = null;
        if (draft.IsMail)
        {
            // The directory is no longer needed now that the composer has closed.
            _nav.WantDirectory = false;
            _nav.Search = string.Empty;
            SendNavigation();
        }

        return true;
    }

    /// <summary>A lost answer must not lock the text for ever; a retry of the same version stays idempotent.</summary>
    private void UpdateDraftTimeouts(float deltaSeconds)
    {
        foreach (var draft in _drafts)
        {
            if (!draft.Pending)
                continue;

            draft.PendingSeconds += deltaSeconds;
            if (draft.PendingSeconds < SendTimeoutSeconds)
                continue;

            draft.Pending = false;
            if (draft != _activeDraft)
                continue;

            UpdateFormLock();
            ShowLocalNotice(Loc.GetString("sw-holonet-draft-timeout"));
        }
    }

    private void ShowLocalNotice(string text)
    {
        _notice.Text = text;
        _noticeBar.Visible = true;
    }

    /// <summary>Returns the existing draft for this target, or a new one unless that would cost someone's text.</summary>
    private Draft? DraftFor(DraftKind kind, int target, string targetBox, Action<Draft> initialize)
    {
        var draft = _drafts.Find(d => d.Kind == kind && d.Target == target && d.TargetBox == targetBox);
        if (draft != null)
            return draft;

        if (_drafts.Count(d => d.Kind == kind) >= MaxDraftsPerKind)
        {
            var empty = _drafts.Find(d => d.Kind == kind && !d.IsDirty && !d.Pending && d != _resumeDraft);
            if (empty == null)
            {
                ShowLocalNotice(Loc.GetString("sw-holonet-draft-limit", ("limit", MaxDraftsPerKind)));
                return null;
            }

            _drafts.Remove(empty);
        }

        draft = new Draft(kind, target, targetBox);
        initialize(draft);
        _drafts.Add(draft);
        return draft;
    }

    /// <summary>Lists stored texts, reachable even while the letter, topic or box itself is out of reach.</summary>
    private void ShowDrafts(DraftKind first, DraftKind second)
    {
        var stored = _drafts.Where(d => (d.Kind == first || d.Kind == second) && d.IsDirty).ToArray();
        if (stored.Length == 0)
            return;

        Show(Eyebrow(Loc.GetString("sw-holonet-drafts-title"), SWHolonetPalette.Gold));
        foreach (var draft in stored)
        {
            var title = draft.Subject.Length > 0 ? draft.Subject : draft.Label;
            if (title.Length == 0)
                title = Loc.GetString(DraftKindKey(draft.Kind));
            Show(Entry(title, Loc.GetString(DraftKindKey(draft.Kind)), false, draft.Pending, () => OpenDraft(draft)));
        }

        Show(Rule());
    }

    private static string DraftKindKey(DraftKind kind) => kind switch
    {
        DraftKind.Thread => "sw-holonet-draft-kind-thread",
        DraftKind.ForumReply => "sw-holonet-draft-kind-forum-reply",
        DraftKind.MailReply => "sw-holonet-draft-kind-mail-reply",
        _ => "sw-holonet-draft-kind-mail",
    };

    /// <summary>Copies the editors into the draft before their controls are disposed.</summary>
    private void StoreForm()
    {
        if (_activeDraft is not { } draft)
            return;

        if (draft.Kind != DraftKind.ForumReply)
            draft.Subject = _titleEdit?.Text ?? draft.Subject;
        draft.Body = _bodyEdit?.Text ?? draft.Body;
    }

    private void OpenDraft(Draft draft)
    {
        StoreForm();
        _activeDraft = draft;
        _resumeDraft = null;
        ClearContent();
        _noticeBar.Visible = false;
        _titleEdit = null;
        _searchEdit = null;
        _formDynamic = null;
        _recipientLabel = null;

        switch (draft.Kind)
        {
            case DraftKind.Thread:
                RenderThreadForm(draft);
                break;
            case DraftKind.ForumReply:
                RenderReplyForm(draft);
                break;
            default:
                RenderMailForm(draft);
                break;
        }

        UpdateFormLock();
        _scroll.SetScrollValue(default);
    }

    private void AddFormActions(string submitTitle)
    {
        _sendingNotice = Detail(Loc.GetString("sw-holonet-draft-sending"), SWHolonetPalette.Warn);
        Show(_sendingNotice);
        var actions = Row();
        _submitButton = Primary(submitTitle, SubmitDraft);
        actions.AddChild(_submitButton);
        actions.AddChild(Quiet(Loc.GetString("sw-holonet-cancel"), CancelForm));
        _deleteButton = Quiet(Loc.GetString("sw-holonet-draft-delete"), DeleteDraft);
        actions.AddChild(_deleteButton);
        Show(actions);
    }

    /// <summary>While an answer is awaited the text can not change, so it always matches what was sent.</summary>
    private void UpdateFormLock()
    {
        if (_activeDraft is not { } draft)
            return;

        var locked = draft.Pending;
        if (_titleEdit != null)
            _titleEdit.Editable = !locked && draft.Kind != DraftKind.MailReply;
        if (_bodyEdit != null)
            _bodyEdit.Editor.Editable = !locked;
        if (_submitButton != null)
            _submitButton.Disabled = locked;
        if (_deleteButton != null)
            _deleteButton.Disabled = locked;
        if (_sendingNotice != null)
            _sendingNotice.Visible = locked;
    }

    private string PublishAs() =>
        Loc.GetString("sw-holonet-publish-as", ("name", _state?.Persona ?? string.Empty), ("role", _state?.Role ?? string.Empty));

    private void BeginThread(int articleId, string title)
    {
        var draft = DraftFor(DraftKind.Thread, articleId, string.Empty, d =>
        {
            d.Label = title;
            d.Subject = title.Length > SWHolonetLimits.ThreadTitle ? title[..SWHolonetLimits.ThreadTitle] : title;
            d.InitialSubject = d.Subject;
        });

        if (draft != null)
            OpenDraft(draft);
    }

    private void RenderThreadForm(Draft draft)
    {
        Show(Eyebrow(Loc.GetString(draft.Target != 0 ? "sw-holonet-forum-new-article" : "sw-holonet-forum-new")));
        Show(Heading(Loc.GetString("sw-holonet-forum-new-title"), 1));
        Show(Caption(Loc.GetString("sw-holonet-forum-title-hint", ("limit", SWHolonetLimits.ThreadTitle))));
        _titleEdit = new LineEdit { HorizontalExpand = true, Text = draft.Subject };
        Show(_titleEdit);
        Show(Caption(Loc.GetString("sw-holonet-markup-hint", ("limit", SWHolonetLimits.ForumBody))));
        _bodyEdit = new SWConsoleTextEdit { MinHeight = 170, Text = draft.Body };
        Show(_bodyEdit);
        Show(Detail(PublishAs()));
        AddFormActions(Loc.GetString("sw-holonet-forum-publish"));
    }

    private void BeginReply(int thread, string title)
    {
        var draft = DraftFor(DraftKind.ForumReply, thread, string.Empty, d => d.Label = title);
        if (draft != null)
            OpenDraft(draft);
    }

    private void RenderReplyForm(Draft draft)
    {
        Show(Eyebrow(Loc.GetString("sw-holonet-forum-reply-eyebrow")));
        Show(Heading(draft.Label, 2));
        Show(Caption(Loc.GetString("sw-holonet-markup-hint", ("limit", SWHolonetLimits.ForumBody))));
        _bodyEdit = new SWConsoleTextEdit { MinHeight = 170, Text = draft.Body };
        Show(_bodyEdit);
        Show(Detail(PublishAs()));
        AddFormActions(Loc.GetString("sw-holonet-forum-reply"));
    }

    private void BeginMail(SWHolonetMailDto? replyTo)
    {
        var draft = replyTo != null
            ? DraftFor(DraftKind.MailReply, replyTo.Id, replyTo.ReplyBox, d =>
            {
                var ownTitle = _state?.Boxes.FirstOrDefault(b => b.Key == replyTo.ReplyBox)?.Title ?? string.Empty;
                d.ToTitle = replyTo.From == ownTitle ? replyTo.To : replyTo.From;
                d.Subject = replyTo.Subject;
                d.InitialSubject = replyTo.Subject;
                d.FromBox = replyTo.ReplyBox;
                d.FromTitle = ownTitle;
            })
            : DraftFor(DraftKind.Mail, 0, string.Empty, d => d.FromBox = _nav.MailBox);

        if (draft != null)
            OpenDraft(draft);
    }

    private void RenderMailForm(Draft draft)
    {
        var isReply = draft.Kind == DraftKind.MailReply;
        Show(Eyebrow(Loc.GetString(isReply ? "sw-holonet-mail-compose-reply" : "sw-holonet-mail-new")));
        Show(Heading(Loc.GetString(isReply ? "sw-holonet-mail-compose-reply-title" : "sw-holonet-mail-compose-new-title"), 1));
        _formDynamic = Column(0f, true);
        Show(_formDynamic);

        if (!isReply)
        {
            _searchEdit = new LineEdit
            {
                HorizontalExpand = true,
                PlaceHolder = Loc.GetString("sw-holonet-mail-search"),
                Text = _nav.Search,
            };
            _searchEdit.OnTextEntered += _ => SearchDirectory();
            var search = Row();
            search.AddChild(_searchEdit);
            search.AddChild(Quiet(Loc.GetString("sw-holonet-mail-search-action"), SearchDirectory));
            Show(search);
        }

        _recipientLabel = Caption(string.Empty, SWHolonetPalette.Bright);
        Show(_recipientLabel);
        Show(Caption(Loc.GetString("sw-holonet-mail-subject-hint", ("limit", SWHolonetLimits.MailSubject))));
        _titleEdit = new LineEdit { HorizontalExpand = true, Text = draft.Subject };
        Show(_titleEdit);
        Show(Caption(Loc.GetString("sw-holonet-markup-hint", ("limit", SWHolonetLimits.MailBody))));
        _bodyEdit = new SWConsoleTextEdit { MinHeight = 150, Text = draft.Body };
        Show(_bodyEdit);
        AddFormActions(Loc.GetString("sw-holonet-mail-send"));

        if (_state != null)
            UpdateForm(_state);
        Go(n =>
        {
            n.Section = SWHolonetSection.Mail;
            n.WantDirectory = !isReply;
        });
    }

    private void SearchDirectory()
    {
        var text = _searchEdit?.Text ?? string.Empty;
        Go(n =>
        {
            n.Search = text;
            n.DirectoryPage = 0;
        });
    }

    /// <summary>Refreshes only the sender and recipient parts; the editors below keep focus and text.</summary>
    private void UpdateForm(SWHolonetStateEvent state)
    {
        if (_activeDraft is not { IsMail: true } draft || _formDynamic == null || _formDynamic.Disposed)
            return;

        _formDynamic.DisposeAllChildren();
        _formDynamic.AddChild(Eyebrow(Loc.GetString("sw-holonet-mail-sender"), SWHolonetPalette.Gold));
        var hasRights = state.Boxes.Count == 0 || state.Boxes.Any(b => b.Key == draft.FromBox);
        var isReply = draft.Kind == DraftKind.MailReply;

        // A reply belongs to the box holding the original letter, so its sender is shown but not selectable.
        if (isReply)
        {
            _formDynamic.AddChild(Detail(draft.FromTitle, SWHolonetPalette.Bright));
            if (!hasRights)
                _formDynamic.AddChild(Paragraph(Loc.GetString("sw-holonet-mail-rights-lost-reply"), SWHolonetPalette.Warn));
        }
        else
        {
            foreach (var box in state.Boxes)
            {
                var key = box.Key;
                var kind = Loc.GetString(box.Department ? "sw-holonet-mail-box-department" : "sw-holonet-mail-box-personal");
                _formDynamic.AddChild(Entry(box.Title, kind, key == draft.FromBox, false, () =>
                {
                    if (draft.Pending)
                        return;
                    draft.FromBox = key;
                    if (_state != null)
                        UpdateForm(_state);
                }));
            }

            if (!hasRights)
                _formDynamic.AddChild(Paragraph(Loc.GetString("sw-holonet-mail-rights-lost"), SWHolonetPalette.Warn));
        }

        if (_recipientLabel != null && !_recipientLabel.Disposed)
        {
            _recipientLabel.Text = draft.ToTitle.Length > 0
                ? Loc.GetString("sw-holonet-mail-to", ("title", draft.ToTitle))
                : Loc.GetString("sw-holonet-mail-no-recipient");
        }

        if (isReply)
            return;

        _formDynamic.AddChild(Eyebrow(Loc.GetString("sw-holonet-mail-directory")));
        if (state.Directory.Count == 0)
            _formDynamic.AddChild(Caption(Loc.GetString("sw-holonet-mail-directory-empty")));
        foreach (var entry in state.Directory)
        {
            var key = entry.Key;
            var title = entry.Title;
            _formDynamic.AddChild(Entry(title, entry.Kind, key == draft.ToBox, false, () =>
            {
                if (draft.Pending)
                    return;
                draft.ToBox = key;
                draft.ToTitle = title;
                if (_state != null)
                    UpdateForm(_state);
            }));
        }

        _formDynamic.AddChild(Pager(state.DirectoryPage, state.DirectoryPages, page => Go(n => n.DirectoryPage = page)));
    }

    /// <summary>
    /// A request id stands for one exact version: changed text gets a new id, unchanged text keeps it, so a
    /// retry is idempotent on the server and a late confirmation can only ever match what it confirmed.
    /// </summary>
    private void SubmitDraft()
    {
        if (_activeDraft is not { } draft || draft.Pending)
            return;

        StoreForm();
        var content = draft.Content();
        if (draft.SentContent != null && draft.SentContent != content)
            draft.RequestId = Guid.NewGuid();

        draft.SentContent = content;
        draft.SentContext = _context;
        draft.Pending = true;
        draft.PendingSeconds = 0f;
        UpdateFormLock();

        switch (draft.Kind)
        {
            case DraftKind.Thread:
                _send(new SWHolonetCreateThreadRequest
                {
                    Context = _context,
                    RequestId = draft.RequestId,
                    Title = draft.Subject,
                    Body = draft.Body,
                    ArticleId = draft.Target,
                });
                break;
            case DraftKind.ForumReply:
                _send(new SWHolonetReplyRequest
                {
                    Context = _context,
                    RequestId = draft.RequestId,
                    ThreadId = draft.Target,
                    Body = draft.Body,
                });
                break;
            default:
                _send(new SWHolonetSendMailRequest
                {
                    Context = _context,
                    RequestId = draft.RequestId,
                    FromBox = draft.FromBox,
                    ToBox = draft.ToBox,
                    Subject = draft.Subject,
                    Body = draft.Body,
                    ReplyTo = draft.Kind == DraftKind.MailReply ? draft.Target : 0,
                });
                break;
        }
    }

    private void CancelForm()
    {
        StoreForm();
        LeaveForm();
    }

    private void DeleteDraft()
    {
        if (_activeDraft is not { Pending: false } draft)
            return;

        _drafts.Remove(draft);
        LeaveForm();
    }

    private void LeaveForm()
    {
        var mail = _activeDraft is { IsMail: true };
        _activeDraft = null;
        if (mail)
        {
            _nav.WantDirectory = false;
            SendNavigation();
        }
        else if (_state != null)
        {
            Render(_state);
        }
    }

    private sealed class Draft(DraftKind kind, int target, string targetBox)
    {
        public readonly DraftKind Kind = kind;

        /// <summary>Article, topic or original letter this draft belongs to; zero when there is none.</summary>
        public readonly int Target = target;

        /// <summary>The box a reply is answered from: one letter in two boxes gives two drafts.</summary>
        public readonly string TargetBox = targetBox;

        public Guid RequestId = Guid.NewGuid();
        public string Label = string.Empty;
        public string Subject = string.Empty;

        /// <summary>The subject this draft was created with, so that only the player's own change counts as text.</summary>
        public string InitialSubject = string.Empty;
        public string Body = string.Empty;
        public string FromBox = string.Empty;
        public string FromTitle = string.Empty;
        public string ToBox = string.Empty;
        public string ToTitle = string.Empty;

        /// <summary>The version last handed to the server, compared with the text when its answer arrives.</summary>
        public string? SentContent;

        /// <summary>The private context that version was sent under.</summary>
        public ulong SentContext;
        public bool Pending;
        public float PendingSeconds;

        public bool IsMail => Kind is DraftKind.Mail or DraftKind.MailReply;

        /// <summary>
        /// The one test for "holds the player's own text". An untouched prefill with no body is not; a subject
        /// the player changed or erased is, even with no body. Listing and eviction both rely on this.
        /// </summary>
        public bool IsDirty => !string.IsNullOrWhiteSpace(Body)
                               || !string.Equals(Subject.Trim(), InitialSubject.Trim(), StringComparison.Ordinal);

        public string Content() => $"{Subject}\0{Body}\0{FromBox}\0{ToBox}";
    }
}
