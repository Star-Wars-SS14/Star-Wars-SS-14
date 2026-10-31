using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Robust.Shared.Network;

namespace Content.Server._SW._Senate.Press;

/// <summary>Private written correspondence; membership is user plus original controlled body.</summary>
public sealed class SWPressMailSystem : EntitySystem
{
    [Dependency] private readonly SWSenatePressSystem _press = default!;
    [Dependency] private readonly SWPressGuestPolicySystem _policy = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    private readonly List<MailThread> _threads = new();
    private int _nextId;
    public event Action? Changed;

    public override void Initialize() => SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
    {
        _threads.Clear(); _nextId = 0; Changed?.Invoke();
    });

    public IReadOnlyList<PressMailView> GetMail(EntityUid actor)
    {
        if (!_press.TryLivePressActor(actor, out var user)) return Array.Empty<PressMailView>();
        return _threads.Where(t => IsParticipant(t, actor, user)).OrderByDescending(t => t.Id)
            .Select(t => new PressMailView(t.Id, t.Subject, t.FirstName, t.SecondName, t.Messages.ToArray())).ToArray();
    }

    public bool Create(EntityUid actor, string targetKey, string subject, string text)
    {
        if (!_press.TryLivePressActor(actor, out var sender) || !Valid(subject, 120) || !Valid(text, 512)
            || !_policy.TryResolveMailTarget(actor, targetKey, out var target, out var recipient)
            || _threads.Count >= 48 || _threads.Count(t => t.FirstUser == sender || t.SecondUser == sender) >= 8
            || _threads.Count(t => t.FirstUser == recipient || t.SecondUser == recipient) >= 8)
            return false;
        var first = Identity.Name(actor, EntityManager, target);
        var second = Identity.Name(target, EntityManager, actor);
        var thread = new MailThread(++_nextId, subject.Trim(), sender, actor, first, recipient, target, second);
        thread.Messages.Add(new PressMailMessage(first, text.Trim(), Timestamp()));
        _threads.Add(thread); Changed?.Invoke();
        return true;
    }

    public bool Reply(EntityUid actor, int id, string text)
    {
        if (!_press.TryLivePressActor(actor, out var user) || !Valid(text, 512)) return false;
        var thread = _threads.FirstOrDefault(t => t.Id == id && IsParticipant(t, actor, user));
        if (thread == null || thread.Messages.Count >= 6) return false;
        var name = thread.FirstUser == user ? thread.FirstName : thread.SecondName;
        thread.Messages.Add(new PressMailMessage(name, text.Trim(), Timestamp()));
        Changed?.Invoke(); return true;
    }

    public bool CopyToDraft(EntityUid actor, int id)
    {
        if (!_press.TryGetCorrespondent(actor, out var user)) return false;
        var thread = _threads.FirstOrDefault(t => t.Id == id && IsParticipant(t, actor, user));
        if (thread == null) return false;
        var text = "Письменная переписка (не запись диктофона):\n\n" +
            string.Join("\n\n", thread.Messages.Select(m => $"{m.Author} ({m.Time}): {m.Text}"));
        const string suffix = "\n\n[Переписка сокращена до лимита черновика.]";
        if (text.Length > 1000)
        {
            var length = 1000 - suffix.Length;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            text = text[..length] + suffix;
        }
        return _press.SaveDraft(actor, 0, 0, thread.Subject, text);
    }

    private static bool Valid(string text, int maximum) => text != null && text.Length <= maximum && !string.IsNullOrWhiteSpace(text);
    private string Timestamp() => $"День {_time.Day}, {_time.TimeString()}";
    private static bool IsParticipant(MailThread t, EntityUid actor, NetUserId user) =>
        t.FirstUser == user && t.FirstBody == actor || t.SecondUser == user && t.SecondBody == actor;

    private sealed class MailThread(int id, string subject, NetUserId firstUser, EntityUid firstBody, string firstName,
        NetUserId secondUser, EntityUid secondBody, string secondName)
    {
        public int Id { get; } = id;
        public string Subject { get; } = subject;
        public NetUserId FirstUser { get; } = firstUser;
        public EntityUid FirstBody { get; } = firstBody;
        public string FirstName { get; } = firstName;
        public NetUserId SecondUser { get; } = secondUser;
        public EntityUid SecondBody { get; } = secondBody;
        public string SecondName { get; } = secondName;
        public List<PressMailMessage> Messages { get; } = new();
    }
}

public sealed record PressMailMessage(string Author, string Text, string Time);
public sealed record PressMailView(int Id, string Subject, string FirstName, string SecondName, IReadOnlyList<PressMailMessage> Messages);
