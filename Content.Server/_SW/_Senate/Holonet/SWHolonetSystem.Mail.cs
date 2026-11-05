using System.Linq;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared.Database;

namespace Content.Server._SW._Senate.Holonet;

public sealed partial class SWHolonetSystem
{
    private const int MaxInbox = 60;
    private const int MaxSent = 60;
    private const int MaxMailPerPersona = 100;
    private const int MaxUnreadFromSender = 5;
    private static readonly TimeSpan MailCooldown = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, MailBox> _boxes = new();
    private int _nextPersonaNumber;
    private int _nextMailId;

    private void ResetMail()
    {
        // The persona number carries on: a stale address from the last round must not reach a new character.
        _boxes.Clear();
        _nextMailId = 0;
    }

    /// <summary>
    /// The only place a letter becomes read: on a navigation that passed the context gate, never while
    /// building a state. A letter is looked up in a box the current identity may open right now.
    /// </summary>
    private void MarkOpenedRead(SWHolonetNavigateRequest view, Who who)
    {
        if (view.Section != SWHolonetSection.Mail || view.MessageId == 0 || view.Folder != SWHolonetMailFolder.Inbox)
            return;

        var boxes = AccessibleBoxes(who).ToArray();
        var box = boxes.FirstOrDefault(b => b.Key == view.MailBox) ?? boxes[0];
        var entry = box.Inbox.Find(e => e.Message.Id == view.MessageId);
        if (entry is not { Read: false })
            return;

        entry.Read = true;
        _allDirty = true;
    }

    private void MarkPersonalBox(Persona persona)
    {
        _boxes[persona.BoxKey] = new MailBox(persona.BoxKey, persona.Name, false);
    }

    private MailBox OfficeBox(string key, string title)
    {
        if (!_boxes.TryGetValue(key, out var box))
        {
            box = new MailBox(key, title, true);
            _boxes[key] = box;
        }

        return box;
    }

    /// <summary>Personal box of the Mind, plus each office box only while the existing systems grant it.</summary>
    private IEnumerable<MailBox> AccessibleBoxes(Who who)
    {
        yield return _boxes[who.Persona.BoxKey];
        foreach (var (key, title) in who.Offices)
            yield return OfficeBox(key, title);
    }

    private bool TryResolveRecipient(string? key, out MailBox? box)
    {
        box = null;
        if (key == null)
            return false;

        if (key.StartsWith("p:", StringComparison.Ordinal))
            return _boxes.TryGetValue(key, out box);

        foreach (var (officeKey, label) in _press.GetPressOfficeTargets())
        {
            if (officeKey != key)
                continue;
            box = OfficeBox(officeKey, label);
            return true;
        }

        return false;
    }

    private void SendMail(Viewer viewer, Who who, SWHolonetSendMailRequest request)
    {
        if (IsRepeat(viewer, who.Persona, request.RequestId, out var invalid) || invalid)
            return;

        var persona = who.Persona;
        var from = AccessibleBoxes(who).FirstOrDefault(b => b.Key == request.FromBox);
        if (from == null)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-from"));
            return;
        }

        if (!SWHolonetText.TryClean(request.Body, SWHolonetLimits.MailBody, true, out var body))
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-body"));
            return;
        }

        MailBox? to;
        string subject;
        if (request.ReplyTo != 0)
        {
            // A reply can only come from a box that actually holds the original letter.
            var original = from.Inbox.Concat(from.Sent).FirstOrDefault(e => e.Message.Id == request.ReplyTo)?.Message;
            if (original == null)
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-original"));
                return;
            }

            var other = original.FromBox == from.Key ? original.ToBox : original.FromBox;
            if (!_boxes.TryGetValue(other, out to))
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-recipient"));
                return;
            }

            // A chain keeps one prefix: the subject of a reply is already in its final form.
            subject = original.IsReply
                ? original.Subject
                : Loc.GetString("sw-holonet-mail-reply-subject", ("subject", original.Subject));
            if (subject.Length > SWHolonetLimits.MailSubject)
                subject = original.Subject;
        }
        else
        {
            if (!TryResolveRecipient(request.ToBox, out to))
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-recipient"));
                return;
            }

            if (!SWHolonetText.TryClean(request.Subject, SWHolonetLimits.MailSubject, false, out subject))
            {
                Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-subject"));
                return;
            }
        }

        if (to == null || to.Key == from.Key)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-self"));
            return;
        }

        if (_timing.CurTime < persona.NextMail)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-cooldown"));
            return;
        }

        if (persona.MailCount >= MaxMailPerPersona)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-limit"));
            return;
        }

        if (to.Inbox.Count(e => !e.Read && e.Message.FromBox == from.Key) >= MaxUnreadFromSender)
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-unread"));
            return;
        }

        if (!MakeInboxRoom(to))
        {
            Acknowledge(viewer, request.RequestId, false, Loc.GetString("sw-holonet-mail-error-full"));
            return;
        }

        var message = new MailMessage(++_nextMailId, from.Key, from.Title, persona.Name, to.Key, to.Title,
            subject, body, Stamp(), request.ReplyTo != 0);
        to.Inbox.Add(new MailEntry(message) { Read = false });
        from.Sent.Add(new MailEntry(message) { Read = true });
        if (from.Sent.Count > MaxSent)
            from.Sent.RemoveAt(0);

        persona.MailCount++;
        persona.NextMail = _timing.CurTime + MailCooldown;
        Remember(persona, request.RequestId);
        // Private correspondence: the log records the fact of sending, not the text.
        _adminLog.Add(LogType.PdaInteract, LogImpact.Low,
            $"{ToPrettyString(who.Actor)} sent Holonet mail {message.Id} from box {from.Key} to box {to.Key}");
        Acknowledge(viewer, request.RequestId, true, Loc.GetString("sw-holonet-mail-sent-done"));
        _allDirty = true;
    }

    /// <summary>A full inbox drops its oldest read letter; unread letters are never destroyed.</summary>
    private static bool MakeInboxRoom(MailBox box)
    {
        if (box.Inbox.Count < MaxInbox)
            return true;

        var oldestRead = box.Inbox.FindIndex(e => e.Read);
        if (oldestRead < 0)
            return false;

        box.Inbox.RemoveAt(oldestRead);
        return true;
    }

    private IEnumerable<(string Key, string Title, string Kind)> Directory(Who who)
    {
        foreach (var (key, label) in _press.GetPressOfficeTargets())
        {
            if (who.Offices.All(office => office.Key != key))
                yield return (key, label, Loc.GetString("sw-holonet-mail-kind-department"));
        }

        // Only those who joined the network are listed, so the book cannot reveal unseen characters.
        foreach (var persona in _personas.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            if (persona != who.Persona)
                yield return (persona.BoxKey, persona.Name, Loc.GetString("sw-holonet-mail-kind-personal", ("number", persona.BoxKey[2..])));
        }
    }

    private sealed class MailBox(string key, string title, bool department)
    {
        public readonly string Key = key;
        public readonly string Title = title;
        public readonly bool Department = department;
        public readonly List<MailEntry> Inbox = new();
        public readonly List<MailEntry> Sent = new();
    }

    private sealed class MailEntry(MailMessage message)
    {
        public readonly MailMessage Message = message;
        public bool Read;
    }

    private sealed record MailMessage(int Id, string FromBox, string FromTitle, string Signer, string ToBox, string ToTitle,
        string Subject, string Body, string Time, bool IsReply);
}
