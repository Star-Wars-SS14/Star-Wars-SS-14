using System.Linq;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared.Database;

namespace Content.Server._SW._Senate.Holonet;

public sealed partial class SWHolonetSystem
{
    private const int MaxThreads = 100;
    private const int MaxThreadsPerPersona = 8;
    private const int MaxPostsPerThread = 150;
    private const int MaxPostsPerPersona = 200;
    private static readonly TimeSpan ThreadCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PostCooldown = TimeSpan.FromSeconds(5);

    private readonly Dictionary<int, ForumThread> _threads = new();
    private readonly Dictionary<int, int> _threadByArticle = new();
    private int _nextThreadId;

    private void ResetForum()
    {
        _threads.Clear();
        _threadByArticle.Clear();
        _nextThreadId = 0;
    }

    private void CreateThread(Viewer viewer, Who who, SWHolonetCreateThreadRequest request)
    {
        if (IsRepeat(viewer, who.Persona, request.RequestId, out var invalid) || invalid)
            return;

        var persona = who.Persona;
        if (!SWHolonetText.TryClean(request.Title, SWHolonetLimits.ThreadTitle, false, out var title)
            || !SWHolonetText.TryClean(request.Body, SWHolonetLimits.ForumBody, true, out var body))
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-fields"));
            return;
        }

        if (_timing.CurTime < persona.NextThread)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-thread-cooldown"));
            return;
        }

        if (_threads.Count >= MaxThreads || persona.ThreadCount >= MaxThreadsPerPersona)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-thread-limit"));
            return;
        }

        var articleId = 0;
        if (request.ArticleId != 0)
        {
            // Nothing was published, so this is a refusal: confirming it would delete the player's text.
            if (_threadByArticle.ContainsKey(request.ArticleId))
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-article-exists"));
                return;
            }

            var publication = _press.GetPublications().FirstOrDefault(p => p.Id == request.ArticleId);
            if (publication == null || publication.Versions.Count == 0 || publication.Versions[^1].Withdrawn)
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-article"));
                return;
            }

            articleId = publication.Id;
        }

        var thread = new ForumThread(++_nextThreadId, title, articleId, persona.Mind);
        thread.Posts.Add(NewPost(who, body));
        _threads[thread.Id] = thread;
        if (articleId != 0)
            _threadByArticle[articleId] = thread.Id;

        persona.ThreadCount++;
        persona.PostCount++;
        persona.NextThread = _timing.CurTime + ThreadCooldown;
        persona.NextPost = _timing.CurTime + PostCooldown;
        Remember(persona, request.RequestId);
        _adminLog.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(who.Actor)} created Holonet forum thread {thread.Id}: '{title}'");

        viewer.View.Section = SWHolonetSection.Forum;
        viewer.View.ThreadId = thread.Id;
        viewer.View.ThreadPage = 0;
        Acknowledge(viewer, request.RequestId, true, Loc.GetString("sw-holonet-forum-thread-created"));
        _allDirty = true;
    }

    private void Reply(Viewer viewer, Who who, SWHolonetReplyRequest request)
    {
        if (IsRepeat(viewer, who.Persona, request.RequestId, out var invalid) || invalid)
            return;

        var persona = who.Persona;
        if (!_threads.TryGetValue(request.ThreadId, out var thread))
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-missing"));
            return;
        }

        if (thread.Closed)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-closed"));
            return;
        }

        if (!SWHolonetText.TryClean(request.Body, SWHolonetLimits.ForumBody, true, out var body))
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-body"));
            return;
        }

        if (_timing.CurTime < persona.NextPost)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-reply-cooldown"));
            return;
        }

        if (persona.PostCount >= MaxPostsPerPersona)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-forum-error-reply-limit"));
            return;
        }

        thread.Posts.Add(NewPost(who, body));
        if (thread.Posts.Count >= MaxPostsPerThread)
            thread.Closed = true;

        persona.PostCount++;
        persona.NextPost = _timing.CurTime + PostCooldown;
        Remember(persona, request.RequestId);
        _adminLog.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(who.Actor)} replied in Holonet forum thread {thread.Id}: '{body}'");

        viewer.View.Section = SWHolonetSection.Forum;
        viewer.View.ThreadId = thread.Id;
        viewer.View.ThreadPage = PageCount(thread.Posts.Count, SWHolonetLimits.PostPageSize) - 1;
        Acknowledge(viewer, request.RequestId, true, Loc.GetString("sw-holonet-forum-replied"));
        _allDirty = true;
    }

    private void CloseThread(Viewer viewer, Who who, SWHolonetCloseThreadRequest request)
    {
        if (!_threads.TryGetValue(request.ThreadId, out var thread) || thread.Author != who.Persona.Mind)
        {
            viewer.Notice = Loc.GetString("sw-holonet-forum-error-close");
            return;
        }

        if (thread.Closed)
            return;

        thread.Closed = true;
        viewer.Notice = Loc.GetString("sw-holonet-forum-closed-done");
        _allDirty = true;
    }

    private ForumPost NewPost(Who who, string body) =>
        new(who.Persona.Mind, who.Persona.Name, who.Role, body, Stamp());

    private sealed class ForumThread(int id, string title, int articleId, EntityUid author)
    {
        public readonly int Id = id;
        public readonly string Title = title;
        public readonly int ArticleId = articleId;
        public readonly EntityUid Author = author;
        public readonly List<ForumPost> Posts = new();
        public bool Closed;
    }

    private sealed record ForumPost(EntityUid Mind, string Name, string Role, string Body, string Time);
}
