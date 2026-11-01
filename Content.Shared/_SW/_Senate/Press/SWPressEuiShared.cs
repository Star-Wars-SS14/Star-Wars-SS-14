using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Press;

[Serializable, NetSerializable]
public sealed class SWPressEuiState : EuiStateBase
{
    public bool PublicOnly;
    public bool CanWrite;
    public bool CanRelease;
    public string Office = string.Empty;
    public List<SWPressReleaseEntry> Releases = new();
    public bool CanMail;
    public List<SWPressGuestTarget> MailTargets = new();
    public List<SWPressMailEntry> Mail = new();
    public int Today;
    public List<SWPressGuestTarget> GuestTargets = new();
    public List<SWPressGuestEntry> GuestRequests = new();
    public bool IsRecording;
    public List<SWPressQuoteEntry> RecordedQuotes = new();
    public int Edition = -1;
    public string Notice = string.Empty;
    public List<SWPressDraftEntry> Drafts = new();
    public List<SWPressPublicationEntry> Publications = new();
    public int HistoryId;
    public string HistoryAuthor = string.Empty;
    public int HistoryEdition;
    public List<SWPressHistoryEntry> History = new();
}

[Serializable, NetSerializable]
public sealed class SWPressDraftEntry
{
    public int Id;
    public int Revision;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public List<SWPressQuoteEntry> Quotes = new();
}

[Serializable, NetSerializable]
public sealed class SWPressPublicationEntry
{
    public int Id;
    public int Version;
    public int Edition;
    public string Author = string.Empty;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public string Time = string.Empty;
    public bool Withdrawn;
    public bool CanEdit;
    public List<SWPressQuoteEntry> Quotes = new();
}

[Serializable, NetSerializable]
public sealed class SWPressHistoryEntry
{
    public int Number;
    public string Title = string.Empty, Body = string.Empty, Time = string.Empty;
    public List<SWPressQuoteEntry> Quotes = new();
}

[Serializable, NetSerializable]
public sealed class SWPressMailEntry
{
    public int Id;
    public string Subject = string.Empty, FirstName = string.Empty, SecondName = string.Empty;
    public List<SWPressMailMessageEntry> Messages = new();
}

[Serializable, NetSerializable]
public sealed class SWPressMailMessageEntry
{
    public string Author = string.Empty, Text = string.Empty, Time = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWPressGuestTarget
{
    public string Key = string.Empty;
    public string Label = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWPressGuestEntry
{
    public int Id, Day, StartSlot, Duration, Revision;
    public string Subject = string.Empty, Description = string.Empty, Location = string.Empty;
    public string Host = string.Empty, Target = string.Empty;
    public int Status;
    public bool IsOwner, IsTarget, Stale;
}

[Serializable, NetSerializable]
public sealed class SWPressReleaseEntry
{
    public int Id;
    public string Title = string.Empty;
    public string Body = string.Empty;
    public string Author = string.Empty;
    public string Office = string.Empty;
    public string Time = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWPressQuoteEntry
{
    public int Id;
    public string DisplayName = string.Empty;
    public string Text = string.Empty;
    public string Time = string.Empty;
}

[Serializable, NetSerializable]
public enum SWPressAction : byte { Refresh, SelectEdition, Save, Delete, Publish, Correct, Withdraw, Print, ToggleRecording, AttachQuote, RemoveQuote, SendRelease, PrintRelease, GuestCreate, GuestRespond, GuestCancel, GuestReschedule, MailCreate, MailReply, MailCopyToDraft, HistoryOpen, HistoryClose, LeaveEdition }

[Serializable, NetSerializable]
public sealed class SWPressActionMessage : EuiMessageBase
{
    public SWPressAction Action;
    public int Id;
    public int Revision;
    public int Edition;
    public int QuoteId;
    public int Day, Slot, Duration;
    public bool Accept;
    public string TargetKey = string.Empty;
    public string Location = string.Empty;
    public string Title = string.Empty;
    public string Body = string.Empty;
}
