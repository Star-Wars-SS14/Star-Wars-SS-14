using System.Globalization;
using System.Text;
using Content.Shared.CartridgeLoader;
using Content.Shared._SW._Senate.PressVideo;
using Content.Shared.Eui;
using Robust.Shared.Serialization;
using Robust.Shared.GameStates;

namespace Content.Shared._SW._Senate.Holonet;

/// <summary>Marks a stationary terminal that opens the Holonet interface.</summary>
[RegisterComponent]
public sealed partial class SWHolonetTerminalComponent : Component { }

/// <summary>Enables the Senate PDA shell without affecting other devices.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWSenatePdaStyleComponent : Component { }

/// <summary>Marks the PDA program that opens the Holonet interface.</summary>
[RegisterComponent]
public sealed partial class SWHolonetCartridgeComponent : Component { }

[Serializable, NetSerializable]
public enum SWHolonetSection : byte
{
    Home,
    News,
    Forum,
    Mail,
    Feed,
}

[Serializable, NetSerializable]
public enum SWHolonetMailFolder : byte
{
    Inbox,
    Sent,
}

public static class SWHolonetLimits
{
    public const int ThreadTitle = 100;
    public const int ForumBody = 1000;
    public const int MailSubject = 100;
    public const int MailBody = 1000;
    public const int Search = 40;
    public const int MaxLines = 30;
    public const int NewsPageSize = 6;
    public const int ForumPageSize = 8;
    public const int PostPageSize = 8;
    public const int MailPageSize = 8;
    public const int DirectoryPageSize = 6;
}

/// <summary>Reduces player text to plain, bounded text. Clients render it only as literal text.</summary>
public static class SWHolonetText
{
    public static bool TryClean(string? raw, int maxLength, bool multiline, out string clean)
    {
        clean = string.Empty;
        // A sanitized value can only shrink, so twice the limit is ample for stripped characters.
        if (string.IsNullOrEmpty(raw) || raw.Length > maxLength * 2)
            return false;

        var builder = new StringBuilder(raw.Length);
        var newlines = 0;
        var lines = 1;
        foreach (var rune in raw.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (rune == '\n')
            {
                if (!multiline)
                {
                    if (builder.Length > 0 && builder[^1] != ' ')
                        builder.Append(' ');
                    continue;
                }

                newlines++;
                if (newlines > 2 || lines >= SWHolonetLimits.MaxLines)
                    continue;
                lines++;
                builder.Append('\n');
                continue;
            }

            // Removes control characters and invisible formatting such as bidirectional overrides.
            var category = char.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse)
                continue;

            newlines = 0;
            builder.Append(rune);
        }

        clean = builder.ToString().Trim();
        return clean.Length > 0 && clean.Length <= maxLength;
    }
}

[Serializable, NetSerializable]
public abstract class SWHolonetRequest
{
    /// <summary>
    /// The private context a private action was issued under. The server also re-reads the actor and Mind,
    /// so a correct number narrows access and never grants it.
    /// </summary>
    public ulong Context;
}

/// <summary>Sets what this viewer is looking at. Every field is revalidated by the server.</summary>
[Serializable, NetSerializable]
public sealed class SWHolonetNavigateRequest : SWHolonetRequest
{
    /// <summary>Raised by the client whenever it hides private content; echoed in the private states after it.</summary>
    public ulong Epoch;
    public ulong VideoSerial;
    public SWHolonetSection Section;
    public int ArticleId;
    public int NewsPage;
    public int ThreadId;
    public int ThreadPage;
    public int ForumPage;
    public string MailBox = string.Empty;
    public SWHolonetMailFolder Folder;
    public int MailPage;
    public int MessageId;
    public bool WantDirectory;
    public string Search = string.Empty;
    public int DirectoryPage;
}

[Serializable, NetSerializable]
public sealed class SWHolonetCreateThreadRequest : SWHolonetRequest
{
    public Guid RequestId;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public int ArticleId;
}

[Serializable, NetSerializable]
public sealed class SWHolonetReplyRequest : SWHolonetRequest
{
    public Guid RequestId;
    public int ThreadId;
    public string Body = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetCloseThreadRequest : SWHolonetRequest
{
    public int ThreadId;
}

[Serializable, NetSerializable]
public sealed class SWHolonetSendMailRequest : SWHolonetRequest
{
    public Guid RequestId;
    public string FromBox = string.Empty;
    public string ToBox = string.Empty;
    public string Subject = string.Empty;
    public string Body = string.Empty;
    public int ReplyTo;
}

/// <summary>Tells the terminal window which device its private state belongs to; carries no content.</summary>
[Serializable, NetSerializable]
public sealed class SWHolonetEuiState : EuiStateBase
{
    public NetEntity Device;
}

/// <summary>Transport for requests sent from a terminal or public screen.</summary>
[Serializable, NetSerializable]
public sealed class SWHolonetEuiMessage : EuiMessageBase
{
    public readonly SWHolonetRequest Request;

    public SWHolonetEuiMessage(SWHolonetRequest request)
    {
        Request = request;
    }
}

/// <summary>Transport for requests sent from the PDA program.</summary>
[Serializable, NetSerializable]
public sealed class SWHolonetCartridgeMessage : CartridgeMessageEvent
{
    public readonly SWHolonetRequest Request;

    public SWHolonetCartridgeMessage(SWHolonetRequest request)
    {
        Request = request;
    }
}

[Serializable, NetSerializable]
public sealed class SWHolonetQuoteDto
{
    public string Speaker = string.Empty;
    public string Text = string.Empty;
    public string Time = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetArticleDto
{
    public int Id;
    public int Version;
    public string Title = string.Empty;
    /// <summary>Empty when withdrawn; only an opened article or the lead excerpt carries text.</summary>
    public string Body = string.Empty;
    public string Author = string.Empty;
    public string Edition = string.Empty;
    public string Time = string.Empty;
    public bool Withdrawn;
    public int ThreadId;
    public List<SWHolonetQuoteDto> Quotes = new();
}

[Serializable, NetSerializable]
public sealed class SWHolonetThreadDto
{
    public int Id;
    public string Title = string.Empty;
    public string AuthorName = string.Empty;
    public string AuthorRole = string.Empty;
    public string Time = string.Empty;
    public int Replies;
    public int Participants;
    public string LastTime = string.Empty;
    public bool Closed;
    public int ArticleId;
    public string ArticleTitle = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetPostDto
{
    public string AuthorName = string.Empty;
    public string AuthorRole = string.Empty;
    public string Body = string.Empty;
    public string Time = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetBoxDto
{
    public string Key = string.Empty;
    public string Title = string.Empty;
    public bool Department;
    public int Unread;
}

[Serializable, NetSerializable]
public sealed class SWHolonetMailRowDto
{
    public int Id;
    public string Counterpart = string.Empty;
    public string Subject = string.Empty;
    public string Time = string.Empty;
    public bool Unread;
}

[Serializable, NetSerializable]
public sealed class SWHolonetMailDto
{
    public int Id;
    public string From = string.Empty;
    public string FromSigner = string.Empty;
    public string To = string.Empty;
    public string Subject = string.Empty;
    public string Body = string.Empty;
    public string Time = string.Empty;
    public string ReplyBox = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetDirectoryDto
{
    public string Key = string.Empty;
    public string Title = string.Empty;
    public string Kind = string.Empty;
}

/// <summary>Tells one session that a private context has ended. Carries the number and nothing about anyone.</summary>
[Serializable, NetSerializable]
public sealed class SWHolonetContextRevokedEvent : EntityEventArgs
{
    public NetEntity Device;
    public ulong Context;
}

/// <summary>
/// Personalized view sent only to the session of its viewer. PDA interface states are shared by the
/// entity and retained for its next user, so private data must never travel through them.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWHolonetFeedEvent : EntityEventArgs
{
    public NetEntity Device;
    public ulong Epoch;
    public ulong Serial;
    public ulong Generation;
    public SWPressVideoState? State;
    public string Error = string.Empty;
    public long FeedId;
    public string Speech = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWHolonetStateEvent : EntityEventArgs
{
    public NetEntity Device;

    /// <summary>False for public content: no personal field below is filled and no action is available.</summary>
    public bool Private;

    /// <summary>Private context of this state, zero for public content. A client drops older numbers.</summary>
    public ulong Context;
    public ulong Epoch;
    public bool Available;
    public string Reason = string.Empty;
    public string Persona = string.Empty;
    /// <summary>The viewer's own opaque box key. Lets the client tell a new character from the same Mind in a new body.</summary>
    public string PersonaKey = string.Empty;
    public string Role = string.Empty;
    public SWHolonetSection Section;

    public Guid Ack;
    public bool AckOk;
    public string Notice = string.Empty;

    public List<string> Ticker = new();
    public int Unread;

    public SWHolonetArticleDto? Lead;
    public List<SWHolonetArticleDto> Articles = new();
    public SWHolonetArticleDto? Article;
    public int NewsPage;
    public int NewsPages = 1;

    public List<SWHolonetThreadDto> Threads = new();
    public SWHolonetThreadDto? Thread;
    public List<SWHolonetPostDto> Posts = new();
    public int ForumPage;
    public int ForumPages = 1;
    public int ThreadPage;
    public int ThreadPages = 1;
    public bool CanReply;
    public bool CanClose;

    public List<SWHolonetBoxDto> Boxes = new();
    public string ActiveBox = string.Empty;
    public SWHolonetMailFolder Folder;
    public List<SWHolonetMailRowDto> Mail = new();
    public int MailPage;
    public int MailPages = 1;
    public SWHolonetMailDto? Message;
    public List<SWHolonetDirectoryDto> Directory = new();
    public int DirectoryPage;
    public int DirectoryPages = 1;
}
