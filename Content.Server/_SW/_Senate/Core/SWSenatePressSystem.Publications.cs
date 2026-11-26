using System.Linq;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Enums;
using Robust.Shared.Utility;
using Content.Shared.IdentityManagement;
using Content.Server._SW._Senate.Press;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWSenatePressSystem
{
    private const int MaxDraftsPerUser = 5;
    private const int MaxDrafts = 100;
    private const int MaxPublications = 24;
    private const int MaxVersions = 8;
    private const int MaxTitleLength = 120;
    private const int MaxBodyLength = 1000;
    private readonly Dictionary<int, PressDraft> _drafts = new();
    private readonly Dictionary<int, PressPublication> _publications = new();
    private int _nextDraftId;
    private int _nextPublicationId;

    private void ResetPress()
    {
        _editions.Clear();
        _pressOffices.Clear();
        _knownOfficeUsers.Clear();
        _releases.Clear();
        _nextReleaseId = 0;
        _drafts.Clear();
        _publications.Clear();
        _nextDraftId = 0;
        _nextPublicationId = 0;
        PublicationsChanged?.Invoke();
    }

    public IReadOnlyList<PressDraftView> GetDrafts(EntityUid actor)
    {
        if (!TryGetCorrespondent(actor, out var user))
            return Array.Empty<PressDraftView>();
        return _drafts.Values.Where(d => d.Owner == user)
            .Select(d => new PressDraftView(d.Id, d.Revision, d.Title, d.Body, d.Quotes.ToArray())).ToArray();
    }

    public bool CanEditPublication(EntityUid actor, int id) =>
        TryGetCorrespondent(actor, out var user) && _publications.TryGetValue(id, out var publication)
        && publication.Owner == user;

    public IReadOnlyList<PressPublicationView> GetPublications() =>
        _publications.Values.OrderByDescending(p => p.Id).Select(View).ToArray();

    public bool SaveDraft(EntityUid actor, int id, int expectedRevision, string title, string body)
    {
        if (!TryGetCorrespondent(actor, out var user) || !ValidText(title, body))
            return false;
        if (id == 0)
        {
            if (_drafts.Count >= MaxDrafts || _drafts.Values.Count(d => d.Owner == user) >= MaxDraftsPerUser)
                return false;
            id = ++_nextDraftId;
            _drafts.Add(id, new PressDraft(id, user, 1, title.Trim(), body.Trim(), new List<PressQuote>()));
            return true;
        }
        if (!_drafts.TryGetValue(id, out var draft) || draft.Owner != user || draft.Revision != expectedRevision)
            return false;
        _drafts[id] = draft with { Revision = draft.Revision + 1, Title = title.Trim(), Body = body.Trim() };
        return true;
    }

    public bool AttachQuote(EntityUid actor, int id, int expectedRevision, int quoteId)
    {
        if (!TryGetCorrespondent(actor, out var user) || !_drafts.TryGetValue(id, out var draft)
            || draft.Owner != user || draft.Revision != expectedRevision || draft.Quotes.Count >= 3
            || draft.Quotes.Any(q => q.Id == quoteId)
            || !EntityManager.System<SWPressRecorderSystem>().TryGetQuote(actor, quoteId, out var quote)
            || quote == null)
            return false;
        var quotes = new List<PressQuote>(draft.Quotes) { quote };
        _drafts[id] = draft with { Revision = draft.Revision + 1, Quotes = quotes };
        return true;
    }

    public bool RemoveQuote(EntityUid actor, int id, int expectedRevision, int quoteId)
    {
        if (!TryGetCorrespondent(actor, out var user) || !_drafts.TryGetValue(id, out var draft)
            || draft.Owner != user || draft.Revision != expectedRevision || !draft.Quotes.Any(q => q.Id == quoteId))
            return false;
        _drafts[id] = draft with { Revision = draft.Revision + 1, Quotes = draft.Quotes.Where(q => q.Id != quoteId).ToList() };
        return true;
    }

    public bool DeleteDraft(EntityUid actor, int id, int expectedRevision)
    {
        if (!TryGetCorrespondent(actor, out var user) || !_drafts.TryGetValue(id, out var draft)
            || draft.Owner != user || draft.Revision != expectedRevision)
            return false;
        return _drafts.Remove(id);
    }

    public bool Publish(EntityUid actor, int draftId, int expectedRevision)
    {
        if (!TryGetCorrespondent(actor, out var user) || !TryGetEdition(actor, out var edition)
            || !_drafts.TryGetValue(draftId, out var draft) || draft.Owner != user
            || draft.Revision != expectedRevision || _publications.Count >= MaxPublications)
            return false;
        var id = ++_nextPublicationId;
        var publication = new PressPublication(id, user, Identity.Name(actor, EntityManager), edition);
        publication.Versions.Add(new PressVersion(1, draft.Title, draft.Body, Timestamp(), false, draft.Quotes.ToArray()));
        _publications.Add(id, publication);
        _drafts.Remove(draftId);
        PublicationsChanged?.Invoke();
        return true;
    }

    public bool Correct(EntityUid actor, int id, int expectedVersion, string title, string body)
    {
        if (!TryGetCorrespondent(actor, out var user) || !ValidText(title, body)
            || !_publications.TryGetValue(id, out var publication) || publication.Owner != user
            || publication.Versions.Count != expectedVersion || publication.Versions.Count >= MaxVersions - 1
            || publication.Versions[^1].Withdrawn)
            return false;
        publication.Versions.Add(new PressVersion(expectedVersion + 1, title.Trim(), body.Trim(), Timestamp(), false, publication.Versions[^1].Quotes.ToArray()));
        PublicationsChanged?.Invoke();
        return true;
    }

    public bool Withdraw(EntityUid actor, int id, int expectedVersion)
    {
        if (!TryGetCorrespondent(actor, out var user) || !_publications.TryGetValue(id, out var publication)
            || publication.Owner != user || publication.Versions.Count != expectedVersion
            || publication.Versions.Count >= MaxVersions || publication.Versions[^1].Withdrawn)
            return false;
        var previous = publication.Versions[^1];
        publication.Versions.Add(new PressVersion(expectedVersion + 1, previous.Title, previous.Body, Timestamp(), true, previous.Quotes.ToArray()));
        PublicationsChanged?.Invoke();
        return true;
    }

    private static bool ValidText(string title, string body) =>
        title != null && body != null && title.Length <= MaxTitleLength && body.Length <= MaxBodyLength
        && !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body);

    private string Timestamp() => _time.TimeString();
    private static PressPublicationView View(PressPublication p) =>
        new(p.Id, p.Author, p.Edition, p.Versions.ToArray());

    private sealed record PressDraft(int Id, NetUserId Owner, int Revision, string Title, string Body, List<PressQuote> Quotes);
    private sealed class PressPublication(int id, NetUserId owner, string author, PressEdition edition)
    {
        public int Id { get; } = id;
        public NetUserId Owner { get; } = owner;
        public string Author { get; } = author;
        public PressEdition Edition { get; } = edition;
        public List<PressVersion> Versions { get; } = new();
    }
}

public sealed record PressDraftView(int Id, int Revision, string Title, string Body, IReadOnlyList<PressQuote> Quotes);
public sealed record PressVersion(int Number, string Title, string Body, string Time, bool Withdrawn, IReadOnlyList<PressQuote> Quotes);
public sealed record PressPublicationView(int Id, string Author, PressEdition Edition, IReadOnlyList<PressVersion> Versions);
