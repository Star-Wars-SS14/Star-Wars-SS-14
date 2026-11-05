using System.Linq;
using Content.Shared._SW._Senate.Holonet;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Holonet;

public sealed partial class SWHolonetView
{
    private static readonly string[] NewsArt = { "news-port.png", "news-coruscant.png", "news-senate.png" };

    /// <summary>The picture is decorative and chosen by publication number, not a claim about its content.</summary>
    private static string ArtFor(int articleId) => NewsArt[Math.Abs(articleId % NewsArt.Length)];

    /// <summary>
    /// The 16:9 source shown at 2.2:1 keeps the crop inside the central 80% safe area of the asset manifest.
    /// The width limit holds that ratio in a maximized window without an oversized picture.
    /// </summary>
    private const float HeroAspect = 2.2f;
    private const float HeroMaxWidth = 720f;

    private Control Hero(int articleId, string caption)
    {
        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            HorizontalAlignment = HAlignment.Left,
            MaxWidth = HeroMaxWidth,
        };
        var art = new SWHolonetArt(ArtFor(articleId), HeroAspect);
        box.AddChild(art);
        var label = SWConsoleStyle.Text(caption, SWHolonetPalette.Background, SWConsoleStyle.Strong);
        label.ClipText = true;
        box.AddChild(Card(label, SWHolonetPalette.Mint));
        // Published stories have no pictures of their own, so the reader is told this is not event footage.
        if (art.Loaded)
            box.AddChild(Caption(Loc.GetString("sw-holonet-art-note")));
        return box;
    }

    private void RenderHome(SWHolonetStateEvent state)
    {
        if (_compact)
        {
            RenderHomeLead(state, _content);
            Show(Rule());
            RenderHomeLinks(state, _content);
            Show(Rule());
            Show(_map);
            RenderHomeServices(state, _content);
            RenderHomeVoice(state, _content);
            Show(_advert);
            return;
        }

        var row = Row();
        var left = Column(210f);
        left.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-eyebrow")));
        left.AddChild(Heading(Loc.GetString("sw-holonet-home-title"), 1, 210f));
        left.AddChild(Caption(Loc.GetString("sw-holonet-home-subtitle")));
        left.AddChild(Rule());
        RenderHomeLinks(state, left);
        row.AddChild(left);

        var center = Column(0f, true);
        RenderHomeLead(state, center);
        row.AddChild(center);

        var right = Column(210f);
        right.AddChild(_map);
        RenderHomeServices(state, right);
        row.AddChild(right);
        Show(row);

        Show(Rule());
        var bottom = Row();
        var voice = Column(0f, true);
        RenderHomeVoice(state, voice);
        bottom.AddChild(voice);
        bottom.AddChild(_advert);
        Show(bottom);
    }

    private void RenderHomeLead(SWHolonetStateEvent state, BoxContainer target)
    {
        if (state.Lead is not { } lead)
        {
            target.AddChild(Hero(0, Loc.GetString("sw-holonet-home-lead-waiting-caption")));
            target.AddChild(Heading(Loc.GetString("sw-holonet-home-lead-waiting-title"), 2));
            target.AddChild(Paragraph(Loc.GetString("sw-holonet-home-lead-waiting-text"), SWHolonetPalette.Dim));
            return;
        }

        target.AddChild(Hero(lead.Id, Loc.GetString("sw-holonet-home-lead-caption", ("edition", lead.Edition))));
        target.AddChild(Heading(lead.Title, 1));
        target.AddChild(Paragraph(lead.Body, SWHolonetPalette.Dim));
        target.AddChild(Detail(Loc.GetString("sw-holonet-home-byline", ("author", lead.Author), ("time", lead.Time)), SWHolonetPalette.Gold));
        target.AddChild(Primary(Loc.GetString("sw-holonet-home-read"), () => OpenArticle(lead.Id)));
    }

    private void RenderHomeLinks(SWHolonetStateEvent state, BoxContainer target)
    {
        target.AddChild(Quiet(Loc.GetString("sw-holonet-home-link-news"), () => OpenSection(SWHolonetSection.News)));
        target.AddChild(Quiet(Loc.GetString("sw-holonet-home-link-forum"), () => OpenSection(SWHolonetSection.Forum)));
        target.AddChild(Rule());
        target.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-now"), SWHolonetPalette.Gold));
        if (state.Articles.Count == 0)
            target.AddChild(Caption(Loc.GetString("sw-holonet-home-now-empty")));
        foreach (var article in state.Articles)
        {
            var id = article.Id;
            target.AddChild(Entry(article.Title, Loc.GetString("sw-holonet-home-byline", ("author", article.Edition), ("time", article.Time)),
                false, false, () => OpenArticle(id)));
        }
    }

    private void RenderHomeServices(SWHolonetStateEvent state, BoxContainer target)
    {
        target.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-network")));
        target.AddChild(Heading(Loc.GetString("sw-holonet-home-coruscant"), 2, 210f));
        target.AddChild(Caption(Loc.GetString("sw-holonet-home-open-sector")));
        if (!state.Private)
            return;

        target.AddChild(Rule());
        target.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-mail")));
        var unread = state.Unread == 0
            ? Loc.GetString("sw-holonet-home-mail-none")
            : Loc.GetString("sw-holonet-home-mail-unread", ("count", state.Unread));
        target.AddChild(Entry(unread, Loc.GetString("sw-holonet-home-mail-boxes"), false, state.Unread > 0,
            () => OpenSection(SWHolonetSection.Mail)));
    }

    private void RenderHomeVoice(SWHolonetStateEvent state, BoxContainer target)
    {
        target.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-voices"), SWHolonetPalette.Gold));
        if (state.Threads.FirstOrDefault() is not { } thread)
        {
            target.AddChild(Entry(Loc.GetString("sw-holonet-home-voices-empty-title"), Loc.GetString("sw-holonet-home-voices-empty-text"),
                false, false, () => OpenSection(SWHolonetSection.Forum)));
            return;
        }

        var id = thread.Id;
        target.AddChild(Entry(thread.Title, Loc.GetString("sw-holonet-home-byline", ("author", thread.AuthorName), ("time", thread.LastTime)),
            false, false, () => OpenThread(id)));
    }

    private void OpenArticle(int id)
    {
        Go(n =>
        {
            n.Section = SWHolonetSection.News;
            n.ArticleId = id;
        });
    }

    private void OpenThread(int id)
    {
        Go(n =>
        {
            n.Section = SWHolonetSection.Forum;
            n.ThreadId = id;
            n.ThreadPage = 0;
        });
    }

    private void RenderNews(SWHolonetStateEvent state)
    {
        if (state.Article is not { } article)
        {
            Show(Eyebrow(Loc.GetString("sw-holonet-news-eyebrow")));
            Show(Heading(Loc.GetString("sw-holonet-news-title"), 1));
            if (state.Articles.Count == 0)
            {
                Show(Paragraph(Loc.GetString("sw-holonet-news-empty"), SWHolonetPalette.Dim));
                return;
            }

            foreach (var entry in state.Articles)
            {
                var id = entry.Id;
                var title = entry.Withdrawn ? Loc.GetString("sw-holonet-news-withdrawn-row", ("title", entry.Title)) : entry.Title;
                Show(Entry(title, Loc.GetString("sw-holonet-news-row", ("edition", entry.Edition), ("author", entry.Author), ("time", entry.Time)),
                    false, entry.Withdrawn, () => OpenArticle(id)));
            }

            Show(Pager(state.NewsPage, state.NewsPages, page => Go(n => n.NewsPage = page)));
            return;
        }

        Show(Quiet(Loc.GetString("sw-holonet-news-back"), () => Go(n => n.ArticleId = 0)));
        var main = Column(0f, true);
        main.AddChild(Eyebrow(Loc.GetString("sw-holonet-news-report", ("edition", article.Edition))));
        main.AddChild(Heading(article.Title, 1));
        main.AddChild(Detail(Loc.GetString("sw-holonet-news-meta", ("author", article.Author), ("time", article.Time), ("version", article.Version)), SWHolonetPalette.Gold));
        if (article.Withdrawn)
        {
            main.AddChild(Rule());
            main.AddChild(Paragraph(Loc.GetString("sw-holonet-news-withdrawn"), SWHolonetPalette.Warn));
        }
        else
        {
            main.AddChild(Hero(article.Id, article.Edition));
            main.AddChild(Paragraph(article.Body));
            foreach (var quote in article.Quotes)
            {
                var block = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
                block.AddChild(Paragraph(Loc.GetString("sw-holonet-news-quote", ("text", quote.Text))));
                block.AddChild(Detail(Loc.GetString("sw-holonet-news-quote-meta", ("speaker", quote.Speaker), ("time", quote.Time))));
                main.AddChild(Card(block));
            }
        }

        var side = _compact ? main : Column(220f);
        if (_compact)
            side.AddChild(Rule());
        side.AddChild(Eyebrow(Loc.GetString("sw-holonet-news-source")));
        side.AddChild(SideParagraph(article.Edition));
        side.AddChild(SideParagraph(article.Author, SWHolonetPalette.Dim));
        side.AddChild(Rule());
        side.AddChild(Eyebrow(Loc.GetString("sw-holonet-home-voices"), SWHolonetPalette.Gold));
        if (article.ThreadId != 0)
        {
            var thread = article.ThreadId;
            side.AddChild(Primary(Loc.GetString("sw-holonet-news-open-thread"), () => OpenThread(thread)));
        }
        else if (article.Withdrawn)
        {
            side.AddChild(SideParagraph(Loc.GetString("sw-holonet-news-no-thread"), SWHolonetPalette.Dim));
        }
        else if (state.Private)
        {
            side.AddChild(Primary(Loc.GetString("sw-holonet-news-create-thread"), () => BeginThread(article.Id, article.Title)));
        }

        if (_compact)
        {
            Show(main);
            return;
        }

        var row = Row();
        row.AddChild(main);
        row.AddChild(side);
        Show(row);
    }

    private void RenderForum(SWHolonetStateEvent state)
    {
        var heading = Row();
        var titles = Column(0f, true);
        titles.AddChild(Eyebrow(Loc.GetString("sw-holonet-forum-eyebrow")));
        titles.AddChild(Heading(Loc.GetString("sw-holonet-forum-title"), 1));
        heading.AddChild(titles);
        if (state.Private)
            heading.AddChild(Primary(Loc.GetString("sw-holonet-forum-create"), () => BeginThread(0, string.Empty)));
        Show(heading);
        if (state.Private)
            ShowDrafts(DraftKind.Thread, DraftKind.ForumReply);

        var list = Column(_compact ? 0f : 280f);
        list.AddChild(Eyebrow(Loc.GetString("sw-holonet-forum-list")));
        if (state.Threads.Count == 0)
            list.AddChild(Paragraph(Loc.GetString("sw-holonet-forum-empty"), SWHolonetPalette.Dim));
        var number = state.ForumPage * SWHolonetLimits.ForumPageSize;
        foreach (var thread in state.Threads)
        {
            number++;
            var id = thread.Id;
            var stats = Loc.GetString("sw-holonet-forum-stats", ("replies", thread.Replies), ("participants", thread.Participants));
            if (thread.Closed)
                stats += Loc.GetString("sw-holonet-forum-closed-suffix");
            list.AddChild(Entry($"{number:00}  {thread.Title}", stats, id == state.Thread?.Id, false, () => OpenThread(id)));
        }

        list.AddChild(Pager(state.ForumPage, state.ForumPages, page => Go(n => n.ForumPage = page)));

        if (_compact)
        {
            if (state.Thread == null)
            {
                Show(list);
                return;
            }

            Show(Quiet(Loc.GetString("sw-holonet-forum-back"), () => Go(n => n.ThreadId = 0)));
            Show(ThreadPane(state, state.Thread));
            return;
        }

        var row = Row();
        row.AddChild(list);
        var pane = Column(0f, true);
        if (state.Thread is { } opened)
            pane.AddChild(ThreadPane(state, opened));
        else
            pane.AddChild(Paragraph(Loc.GetString("sw-holonet-forum-pick"), SWHolonetPalette.Dim));
        row.AddChild(pane);
        Show(row);
    }

    private Control ThreadPane(SWHolonetStateEvent state, SWHolonetThreadDto thread)
    {
        var pane = Column(0f, true);
        pane.AddChild(Eyebrow(Loc.GetString(thread.ArticleId != 0 ? "sw-holonet-forum-article-thread" : "sw-holonet-forum-thread")));
        pane.AddChild(Heading(thread.Title, 2));
        pane.AddChild(Detail(Loc.GetString("sw-holonet-forum-thread-meta", ("replies", thread.Replies), ("time", thread.LastTime))));
        pane.AddChild(Rule());
        foreach (var post in state.Posts)
        {
            pane.AddChild(Byline(post.AuthorName));
            pane.AddChild(Detail(Loc.GetString("sw-holonet-forum-post-meta", ("role", post.AuthorRole), ("time", post.Time))));
            pane.AddChild(Paragraph(post.Body));
            pane.AddChild(Rule());
        }

        pane.AddChild(Pager(state.ThreadPage, state.ThreadPages, page => Go(n => n.ThreadPage = page)));

        var actions = Row();
        if (state.CanReply)
            actions.AddChild(Primary(Loc.GetString("sw-holonet-forum-reply"), () => BeginReply(thread.Id, thread.Title)));
        else if (thread.Closed)
            pane.AddChild(Detail(Loc.GetString("sw-holonet-forum-closed"), SWHolonetPalette.Warn));
        if (thread.ArticleId != 0)
        {
            var article = thread.ArticleId;
            actions.AddChild(Quiet(Loc.GetString("sw-holonet-forum-read-article"), () => OpenArticle(article)));
        }

        if (state.CanClose)
            actions.AddChild(Quiet(Loc.GetString("sw-holonet-forum-close"), () => _send(new SWHolonetCloseThreadRequest { Context = _context, ThreadId = thread.Id })));
        pane.AddChild(actions);
        return pane;
    }

    private void RenderMail(SWHolonetStateEvent state)
    {
        if (!state.Private)
        {
            Show(Heading(Loc.GetString("sw-holonet-mail-title"), 1));
            Show(Paragraph(Loc.GetString("sw-holonet-private-required"), SWHolonetPalette.Dim));
            return;
        }

        var heading = Row();
        var titles = Column(0f, true);
        titles.AddChild(Eyebrow(Loc.GetString("sw-holonet-mail-eyebrow")));
        titles.AddChild(Heading(Loc.GetString("sw-holonet-mail-title"), 1));
        titles.AddChild(Caption(Loc.GetString("sw-holonet-mail-subtitle")));
        heading.AddChild(titles);
        heading.AddChild(Primary(Loc.GetString("sw-holonet-mail-new"), () => BeginMail(null)));
        Show(heading);
        ShowDrafts(DraftKind.Mail, DraftKind.MailReply);

        var boxes = Column(_compact ? 0f : 170f);
        foreach (var box in state.Boxes)
        {
            var key = box.Key;
            boxes.AddChild(Eyebrow(Loc.GetString(box.Department ? "sw-holonet-mail-box-department" : "sw-holonet-mail-box-personal"),
                box.Department ? SWHolonetPalette.Gold : SWHolonetPalette.Mint));
            var unread = box.Unread == 0
                ? Loc.GetString("sw-holonet-mail-box-none")
                : Loc.GetString("sw-holonet-mail-box-unread", ("count", box.Unread));
            boxes.AddChild(Entry(box.Title, unread, key == state.ActiveBox, box.Unread > 0,
                () => Go(n =>
                {
                    n.MailBox = key;
                    n.MailPage = 0;
                    n.MessageId = 0;
                })));
        }

        var inboxOpen = state.Folder == SWHolonetMailFolder.Inbox;
        boxes.AddChild(FolderButton(Loc.GetString("sw-holonet-mail-folder-inbox"), inboxOpen, SWHolonetMailFolder.Inbox));
        boxes.AddChild(FolderButton(Loc.GetString("sw-holonet-mail-folder-sent"), !inboxOpen, SWHolonetMailFolder.Sent));

        var rows = Column(_compact ? 0f : 230f);
        rows.AddChild(Eyebrow(Loc.GetString(inboxOpen ? "sw-holonet-mail-inbox" : "sw-holonet-mail-sent")));
        if (state.Mail.Count == 0)
            rows.AddChild(Paragraph(Loc.GetString(inboxOpen ? "sw-holonet-mail-inbox-empty" : "sw-holonet-mail-sent-empty"), SWHolonetPalette.Dim));
        foreach (var mail in state.Mail)
        {
            var id = mail.Id;
            rows.AddChild(Entry(mail.Subject, Loc.GetString("sw-holonet-mail-row", ("name", mail.Counterpart), ("time", mail.Time)),
                id == state.Message?.Id, mail.Unread, () => Go(n => n.MessageId = id)));
        }

        rows.AddChild(Pager(state.MailPage, state.MailPages, page => Go(n => n.MailPage = page)));

        if (_compact)
        {
            if (state.Message is { } single)
            {
                Show(Quiet(Loc.GetString("sw-holonet-mail-back"), () => Go(n => n.MessageId = 0)));
                Show(MessagePane(single));
                return;
            }

            Show(boxes);
            Show(Rule());
            Show(rows);
            return;
        }

        var row = Row();
        row.AddChild(boxes);
        row.AddChild(rows);
        var pane = Column(0f, true);
        if (state.Message is { } message)
            pane.AddChild(MessagePane(message));
        else
            pane.AddChild(Paragraph(Loc.GetString("sw-holonet-mail-pick"), SWHolonetPalette.Dim));
        row.AddChild(pane);
        Show(row);
    }

    /// <summary>The open folder is marked by the primary button style alone.</summary>
    private Button FolderButton(string title, bool open, SWHolonetMailFolder folder) =>
        open ? Primary(title, () => OpenFolder(folder)) : Quiet(title, () => OpenFolder(folder));

    private void OpenFolder(SWHolonetMailFolder folder)
    {
        Go(n =>
        {
            n.Folder = folder;
            n.MailPage = 0;
            n.MessageId = 0;
        });
    }

    private Control MessagePane(SWHolonetMailDto message)
    {
        var pane = Column(0f, true);
        pane.AddChild(Eyebrow(Loc.GetString("sw-holonet-mail-message"), SWHolonetPalette.Gold));
        pane.AddChild(Heading(message.Subject, 2));
        pane.AddChild(Detail(Loc.GetString("sw-holonet-mail-from", ("title", message.From), ("signer", message.FromSigner))));
        pane.AddChild(Detail(Loc.GetString("sw-holonet-mail-to", ("title", message.To))));
        pane.AddChild(Detail(message.Time));
        pane.AddChild(Rule());
        pane.AddChild(Paragraph(message.Body));
        pane.AddChild(Rule());
        pane.AddChild(Caption(Loc.GetString("sw-holonet-mail-visibility")));
        pane.AddChild(Primary(Loc.GetString("sw-holonet-mail-reply"), () => BeginMail(message)));
        return pane;
    }
}
