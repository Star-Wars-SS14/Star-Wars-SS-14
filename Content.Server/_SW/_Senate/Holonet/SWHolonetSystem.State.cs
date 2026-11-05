using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Holonet;

namespace Content.Server._SW._Senate.Holonet;

public sealed partial class SWHolonetSystem
{
    private const int LeadExcerpt = 220;
    private const int TickerItems = 5;

    private static int PageCount(int items, int size) => Math.Max(1, (items + size - 1) / size);

    private static int ClampPage(int page, int pages) => Math.Clamp(page, 0, pages - 1);

    /// <summary>Only fields that lead to public content. The mail section itself shows nothing without a context.</summary>
    private static void ApplyPublicNavigation(Viewer viewer, SWHolonetNavigateRequest request)
    {
        var view = viewer.View;
        view.Section = request.Section;
        view.ArticleId = request.ArticleId;
        view.NewsPage = request.NewsPage;
        view.ThreadId = request.ThreadId;
        view.ThreadPage = request.ThreadPage;
        view.ForumPage = request.ForumPage;
    }

    /// <summary>Called only after the request has passed the context gate, with the identity read there.</summary>
    private void ApplyPrivateNavigation(Viewer viewer, SWHolonetNavigateRequest request, Who who)
    {
        var view = viewer.View;
        view.MailBox = request.MailBox;
        view.Folder = request.Folder;
        view.MailPage = request.MailPage;
        view.MessageId = request.MessageId;
        view.WantDirectory = request.WantDirectory;
        view.DirectoryPage = request.DirectoryPage;

        // Oversized or invalid search input simply means no filter.
        view.Search = SWHolonetText.TryClean(request.Search, SWHolonetLimits.Search, false, out var search)
            ? search
            : string.Empty;

        MarkOpenedRead(view, who);
    }

    /// <summary>A device deleted in this tick has no network identity; such a state simply matches no view.</summary>
    private NetEntity DeviceNetEntity(Viewer viewer) =>
        EntityManager.TryGetNetEntity(viewer.Device, out var device) ? device.Value : NetEntity.Invalid;

    private SWHolonetStateEvent BuildDenied(Viewer viewer)
    {
        return new SWHolonetStateEvent
        {
            Device = DeviceNetEntity(viewer),
            Available = false,
            Ack = viewer.Ack,
            AckOk = false,
            Reason = Loc.GetString(_time.ModeActive ? "sw-holonet-denied-actor" : "sw-holonet-denied-mode"),
        };
    }

    /// <summary>
    /// Everything personal is inside the <paramref name="who"/> branch: without an identity the state holds
    /// public news and forum text only, and it can only ever carry a refusal, never a confirmation.
    /// </summary>
    private SWHolonetStateEvent BuildState(Viewer viewer, Who? who)
    {
        var view = viewer.View;
        var state = new SWHolonetStateEvent
        {
            Device = DeviceNetEntity(viewer),
            Available = true,
            Section = view.Section,
            Ack = viewer.Ack,
            AckOk = who != null && viewer.AckOk,
            Notice = viewer.Notice,
        };

        var publications = _press.GetPublications();
        foreach (var publication in publications)
        {
            if (state.Ticker.Count >= TickerItems)
                break;
            if (publication.Versions.Count > 0 && !publication.Versions[^1].Withdrawn)
                state.Ticker.Add(publication.Versions[^1].Title);
        }

        if (who is { } identity)
        {
            state.Private = true;
            state.Context = viewer.ContextId;
            state.Epoch = viewer.Epoch;
            state.Persona = identity.Persona.Name;
            state.PersonaKey = identity.Persona.Key;
            state.Role = identity.Role;

            if (view.Section == SWHolonetSection.Mail)
                BuildMail(state, view, identity);

            state.Unread = AccessibleBoxes(identity).Sum(b => b.Inbox.Count(e => !e.Read));
        }

        switch (view.Section)
        {
            case SWHolonetSection.Home:
                BuildHome(state, publications);
                break;
            case SWHolonetSection.News:
                BuildNews(state, view, publications);
                break;
            case SWHolonetSection.Forum:
                BuildForum(state, view, who?.Persona.Mind, publications);
                break;
        }

        return state;
    }

    private void BuildHome(SWHolonetStateEvent state, IReadOnlyList<PressPublicationView> publications)
    {
        var live = publications.Where(p => p.Versions.Count > 0 && !p.Versions[^1].Withdrawn).ToArray();
        if (live.Length > 0)
        {
            state.Lead = Article(live[0], false);
            var text = live[0].Versions[^1].Body;
            if (text.Length > LeadExcerpt)
            {
                // Never cut a surrogate pair in half when shortening the excerpt.
                var length = char.IsHighSurrogate(text[LeadExcerpt - 1]) ? LeadExcerpt - 1 : LeadExcerpt;
                text = text[..length].TrimEnd() + "…";
            }

            state.Lead.Body = text;
        }

        foreach (var publication in live.Skip(1).Take(2))
            state.Articles.Add(Article(publication, false));

        foreach (var thread in _threads.Values.OrderByDescending(t => t.Id).Take(1))
            state.Threads.Add(ThreadRow(thread, publications));
    }

    private void BuildNews(SWHolonetStateEvent state, SWHolonetNavigateRequest view, IReadOnlyList<PressPublicationView> publications)
    {
        var valid = publications.Where(p => p.Versions.Count > 0).ToArray();
        state.NewsPages = PageCount(valid.Length, SWHolonetLimits.NewsPageSize);
        state.NewsPage = ClampPage(view.NewsPage, state.NewsPages);
        foreach (var publication in valid.Skip(state.NewsPage * SWHolonetLimits.NewsPageSize).Take(SWHolonetLimits.NewsPageSize))
            state.Articles.Add(Article(publication, false));

        if (view.ArticleId == 0)
            return;

        var opened = valid.FirstOrDefault(p => p.Id == view.ArticleId);
        if (opened != null)
        {
            state.Article = Article(opened, true);
            return;
        }

        // Forgotten so that the notice is shown once and not on every later update.
        view.ArticleId = 0;
        if (string.IsNullOrEmpty(state.Notice))
            state.Notice = Loc.GetString("sw-holonet-news-gone");
    }

    /// <summary>Always the latest version; a withdrawn publication exposes neither its text nor quotes.</summary>
    private SWHolonetArticleDto Article(PressPublicationView publication, bool withBody)
    {
        var version = publication.Versions[^1];
        var dto = new SWHolonetArticleDto
        {
            Id = publication.Id,
            Version = version.Number,
            Title = version.Title,
            Author = publication.Author,
            Edition = EditionName(publication.Edition),
            Time = version.Time,
            Withdrawn = version.Withdrawn,
            ThreadId = _threadByArticle.GetValueOrDefault(publication.Id),
        };

        if (!withBody || version.Withdrawn)
            return dto;

        dto.Body = version.Body;
        foreach (var quote in version.Quotes)
            dto.Quotes.Add(new SWHolonetQuoteDto { Speaker = quote.DisplayName, Text = quote.Text, Time = quote.Time });
        return dto;
    }

    private static string EditionName(PressEdition edition) => Loc.GetString(edition switch
    {
        PressEdition.RepublicHerald => "sw-holonet-edition-herald",
        PressEdition.VoicesOfRepublic => "sw-holonet-edition-voices",
        PressEdition.CoruscantTribune => "sw-holonet-edition-tribune",
        PressEdition.GalacticObserver => "sw-holonet-edition-observer",
        _ => "sw-holonet-edition-unknown",
    });

    private void BuildForum(SWHolonetStateEvent state, SWHolonetNavigateRequest view, EntityUid? mind, IReadOnlyList<PressPublicationView> publications)
    {
        var ordered = _threads.Values.OrderByDescending(t => t.Id).ToArray();
        state.ForumPages = PageCount(ordered.Length, SWHolonetLimits.ForumPageSize);
        state.ForumPage = ClampPage(view.ForumPage, state.ForumPages);
        foreach (var thread in ordered.Skip(state.ForumPage * SWHolonetLimits.ForumPageSize).Take(SWHolonetLimits.ForumPageSize))
            state.Threads.Add(ThreadRow(thread, publications));

        if (view.ThreadId == 0)
            return;

        if (!_threads.TryGetValue(view.ThreadId, out var opened))
        {
            view.ThreadId = 0;
            if (string.IsNullOrEmpty(state.Notice))
                state.Notice = Loc.GetString("sw-holonet-forum-gone");
            return;
        }

        state.Thread = ThreadRow(opened, publications);
        state.CanReply = mind != null && !opened.Closed;
        state.CanClose = mind != null && !opened.Closed && opened.Author == mind;
        state.ThreadPages = PageCount(opened.Posts.Count, SWHolonetLimits.PostPageSize);
        state.ThreadPage = ClampPage(view.ThreadPage, state.ThreadPages);
        foreach (var post in opened.Posts.Skip(state.ThreadPage * SWHolonetLimits.PostPageSize).Take(SWHolonetLimits.PostPageSize))
        {
            state.Posts.Add(new SWHolonetPostDto
            {
                AuthorName = post.Name,
                AuthorRole = post.Role,
                Body = post.Body,
                Time = post.Time,
            });
        }
    }

    private static SWHolonetThreadDto ThreadRow(ForumThread thread, IReadOnlyList<PressPublicationView> publications)
    {
        var first = thread.Posts[0];
        var article = thread.ArticleId == 0 ? null : publications.FirstOrDefault(p => p.Id == thread.ArticleId);
        return new SWHolonetThreadDto
        {
            Id = thread.Id,
            Title = thread.Title,
            AuthorName = first.Name,
            AuthorRole = first.Role,
            Time = first.Time,
            Replies = thread.Posts.Count - 1,
            Participants = thread.Posts.Select(p => p.Mind).Distinct().Count(),
            LastTime = thread.Posts[^1].Time,
            Closed = thread.Closed,
            ArticleId = thread.ArticleId,
            ArticleTitle = article is { Versions.Count: > 0 } ? article.Versions[^1].Title : string.Empty,
        };
    }

    private void BuildMail(SWHolonetStateEvent state, SWHolonetNavigateRequest view, Who who)
    {
        var boxes = AccessibleBoxes(who).ToArray();
        foreach (var box in boxes)
        {
            state.Boxes.Add(new SWHolonetBoxDto
            {
                Key = box.Key,
                Title = box.Title,
                Department = box.Department,
                Unread = box.Inbox.Count(e => !e.Read),
            });
        }

        // A box that the actor no longer has rights to silently falls back to the personal one.
        var active = boxes.FirstOrDefault(b => b.Key == view.MailBox) ?? boxes[0];
        state.ActiveBox = active.Key;
        state.Folder = view.Folder;

        var folder = view.Folder == SWHolonetMailFolder.Sent ? active.Sent : active.Inbox;
        state.MailPages = PageCount(folder.Count, SWHolonetLimits.MailPageSize);
        state.MailPage = ClampPage(view.MailPage, state.MailPages);
        foreach (var entry in Enumerable.Reverse(folder).Skip(state.MailPage * SWHolonetLimits.MailPageSize).Take(SWHolonetLimits.MailPageSize))
        {
            var message = entry.Message;
            var outgoing = view.Folder == SWHolonetMailFolder.Sent;
            state.Mail.Add(new SWHolonetMailRowDto
            {
                Id = message.Id,
                Counterpart = outgoing ? message.ToTitle : message.FromTitle,
                Subject = message.Subject,
                Time = message.Time,
                Unread = !entry.Read,
            });
        }

        if (view.MessageId != 0)
        {
            var opened = folder.FirstOrDefault(e => e.Message.Id == view.MessageId);
            if (opened != null)
            {
                var message = opened.Message;
                state.Message = new SWHolonetMailDto
                {
                    Id = message.Id,
                    From = message.FromTitle,
                    FromSigner = message.Signer,
                    To = message.ToTitle,
                    Subject = message.Subject,
                    Body = message.Body,
                    Time = message.Time,
                    ReplyBox = active.Key,
                };
            }
            else
            {
                view.MessageId = 0;
                if (string.IsNullOrEmpty(state.Notice))
                    state.Notice = Loc.GetString("sw-holonet-mail-gone");
            }
        }

        if (!view.WantDirectory)
            return;

        var entries = Directory(who);
        if (view.Search.Length > 0)
            entries = entries.Where(e => e.Title.Contains(view.Search, StringComparison.CurrentCultureIgnoreCase));
        var all = entries.ToArray();
        state.DirectoryPages = PageCount(all.Length, SWHolonetLimits.DirectoryPageSize);
        state.DirectoryPage = ClampPage(view.DirectoryPage, state.DirectoryPages);
        foreach (var entry in all.Skip(state.DirectoryPage * SWHolonetLimits.DirectoryPageSize).Take(SWHolonetLimits.DirectoryPageSize))
            state.Directory.Add(new SWHolonetDirectoryDto { Key = entry.Key, Title = entry.Title, Kind = entry.Kind });
    }
}
