using Content.Shared._SW._Senate.Holonet;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Holonet;

/// <summary>
/// A private context binds one viewer to (session, device, actor, Mind) under a number that is never reused.
/// The client's copy of that number can only narrow access: rights always come from reading the current
/// actor and Mind again. Handlers below only revoke, because <c>MindSystem.TransferTo</c> raises the attach
/// events before it stores the new Mind on the entity; a new context is bound later, from Update or a request.
/// </summary>
public sealed partial class SWHolonetSystem
{
    /// <summary>64 bits can not wrap within a server's lifetime, even at one context per tick for every viewer.</summary>
    private ulong _nextContext;

    private void InitializeContext()
    {
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MindComponent, MindGotAddedEvent>(OnMindGotAdded);
        SubscribeLocalEvent<MindComponent, MindGotRemovedEvent>(OnMindGotRemoved);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        foreach (var viewer in _viewers.Values)
        {
            if (viewer.Session.UserId != args.Player.UserId)
                continue;

            viewer.Dirty = true;
            if (viewer.BoundActor != args.Entity)
                Revoke(viewer);
        }
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        foreach (var viewer in _viewers.Values)
        {
            if (viewer.Session.UserId != args.Player.UserId)
                continue;
            if (viewer.VideoSubscribed && viewer.VideoActor == args.Entity)
                StopVideo(viewer);
            if (viewer.BoundActor == args.Entity)
                Revoke(viewer);
        }
    }

    /// <summary>The only signal for another Mind entering the same actor: no attach event fires in that case.</summary>
    private void OnMindGotAdded(Entity<MindComponent> ent, ref MindGotAddedEvent args)
    {
        var container = args.Container.Owner;
        foreach (var viewer in _viewers.Values)
        {
            if (viewer.BoundActor == container && viewer.BoundMind != ent.Owner)
                Revoke(viewer);
        }
    }

    private void OnMindGotRemoved(Entity<MindComponent> ent, ref MindGotRemovedEvent args)
    {
        foreach (var viewer in _viewers.Values)
        {
            if (viewer.BoundMind == ent.Owner)
                Revoke(viewer);
        }
    }

    /// <summary>
    /// Clears private server state first and then tells that one session which number ended, immediately and
    /// without any content. Idempotent, so an event burst from one transfer sends a single notice.
    /// </summary>
    private void Revoke(Viewer viewer)
    {
        if (viewer.ContextId == 0)
            return;

        StopVideo(viewer);
        var ended = viewer.ContextId;
        viewer.ContextId = 0;
        viewer.BoundMind = null;
        viewer.BoundActor = EntityUid.Invalid;
        viewer.Ack = Guid.Empty;
        viewer.AckOk = false;
        viewer.Notice = string.Empty;
        viewer.Fingerprint = string.Empty;
        viewer.Dirty = true;

        var view = viewer.View;
        view.MailBox = string.Empty;
        view.Folder = SWHolonetMailFolder.Inbox;
        view.MailPage = 0;
        view.MessageId = 0;
        view.WantDirectory = false;
        view.Search = string.Empty;
        view.DirectoryPage = 0;

        if (viewer.Session.Status != SessionStatus.Disconnected
            && EntityManager.TryGetNetEntity(viewer.Device, out var device))
        {
            RaiseNetworkEvent(new SWHolonetContextRevokedEvent { Device = device.Value, Context = ended }, viewer.Session);
        }
    }

    /// <summary>Called only after a fresh resolve, never from an event raised in the middle of a transfer.</summary>
    private void BindContext(Viewer viewer, Who who)
    {
        if (viewer.ContextId != 0 && viewer.BoundActor == who.Actor && viewer.BoundMind == who.Persona.Mind)
            return;

        Revoke(viewer);
        viewer.ContextId = ++_nextContext;
        viewer.BoundActor = who.Actor;
        viewer.BoundMind = who.Persona.Mind;
        viewer.Dirty = true;
    }

    /// <summary>
    /// The gate in front of all private state. Current session, actor, Mind and device are read again, and
    /// the number must then match. A correct number grants nothing by itself, and a request issued under any
    /// earlier context is refused here, before it can change anything. A failed read ends the context so
    /// that the client learns of it and re-navigates.
    /// </summary>
    private bool TryAcceptContext(Viewer viewer, ulong requested, out Who who)
    {
        if (!TryResolve(viewer, out who))
        {
            Revoke(viewer);
            return false;
        }

        if (viewer.ContextId != 0 && (viewer.BoundActor != who.Actor || viewer.BoundMind != who.Persona.Mind))
            Revoke(viewer);

        return viewer.ContextId != 0 && requested == viewer.ContextId;
    }

    /// <summary>
    /// A navigation with no number is public: it moves between news and forum pages and has no way to set a
    /// mailbox, a letter or a read mark. One with a number must pass the gate. A stale one changes nothing at
    /// all, including its public part; the client is told by the revocation and sends its view again.
    /// </summary>
    private void HandleNavigation(Viewer viewer, SWHolonetNavigateRequest request)
    {
        if (request.Epoch < viewer.Epoch
            || request.Epoch == viewer.Epoch && request.VideoSerial < viewer.VideoSerial
            || !Enum.IsDefined(request.Section) || !Enum.IsDefined(request.Folder))
            return;

        // A newer epoch means the client has dropped everything older, so the old number ends with it.
        if (request.Epoch > viewer.Epoch)
        {
            Revoke(viewer);
            viewer.Epoch = request.Epoch;
        }

        StopVideo(viewer);
        viewer.VideoSerial = request.VideoSerial;
        var isPrivate = request.Context != 0;
        Who who = default;
        if (isPrivate && !TryAcceptContext(viewer, request.Context, out who))
        {
            // An invalid private request must not revive the page the user has left.
            viewer.View.Section = SWHolonetSection.Home;
            return;
        }

        ApplyPublicNavigation(viewer, request);
        if (isPrivate)
            ApplyPrivateNavigation(viewer, request, who);

        viewer.Dirty = true;
        if (request.Section == SWHolonetSection.Feed && VideoAllowed(viewer))
            StartVideo(viewer);
    }

    private static Guid RequestIdOf(SWHolonetRequest request) => request switch
    {
        SWHolonetCreateThreadRequest create => create.RequestId,
        SWHolonetReplyRequest reply => reply.RequestId,
        SWHolonetSendMailRequest mail => mail.RequestId,
        _ => Guid.Empty,
    };
}
