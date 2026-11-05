using Content.Shared._SW._Senate.Holonet;
using Robust.Shared.Enums;

namespace Content.Server._SW._Senate.Holonet;

public sealed partial class SWHolonetSystem
{
    // The portal is public, but it is never a substitute for resolving the current device and actor.
    private bool VideoAllowed(Viewer viewer)
    {
        if (viewer.VideoSubscribed && viewer.VideoActor != viewer.Actor
            || viewer.View.Section != SWHolonetSection.Feed || !CanShowPublic(viewer))
            return false;
        if (viewer.ContextId == 0)
            return true;
        return TryResolve(viewer, out var who) && who.Persona.Mind == viewer.BoundMind
            && viewer.Actor == viewer.BoundActor;
    }

    private void StartVideo(Viewer viewer)
    {
        if (viewer.VideoSubscribed)
            return;
        if (!_video.AddPortalViewer(viewer.Actor))
        {
            if (EntityManager.TryGetNetEntity(viewer.Device, out var device))
                RaiseNetworkEvent(new SWHolonetFeedEvent
                {
                    Device = device.Value, Epoch = viewer.Epoch, Serial = viewer.VideoSerial,
                    Generation = viewer.VideoGeneration, Error = Loc.GetString("sw-holonet-feed-full"),
                }, viewer.Session);
            return;
        }
        viewer.VideoSubscribed = true;
        viewer.VideoActor = viewer.Actor;
        viewer.VideoGeneration++;
        SendFeed(viewer);
    }

    private void StopVideo(Viewer viewer)
    {
        if (!viewer.VideoSubscribed)
            return;
        viewer.VideoSubscribed = false;
        _video.RemovePortalViewer(viewer.VideoActor, viewer.Session);
        viewer.VideoGeneration++;
        if (viewer.Session.Status == SessionStatus.InGame
            && EntityManager.TryGetNetEntity(viewer.Device, out var device))
            RaiseNetworkEvent(new SWHolonetFeedEvent
            {
                Device = device.Value, Epoch = viewer.Epoch, Serial = viewer.VideoSerial,
                Generation = viewer.VideoGeneration,
                Error = Loc.GetString("sw-holonet-feed-reconnecting"),
            }, viewer.Session);
    }

    private void SendFeed(Viewer viewer)
    {
        if (!VideoAllowed(viewer) || !EntityManager.TryGetNetEntity(viewer.Device, out var device))
        {
            StopVideo(viewer);
            return;
        }
        RaiseNetworkEvent(new SWHolonetFeedEvent
        {
            Device = device.Value, Epoch = viewer.Epoch, Serial = viewer.VideoSerial,
            Generation = viewer.VideoGeneration, State = _video.CurrentFeed(),
        }, viewer.Session);
    }

    private void OnFeedChanged()
    {
        foreach (var viewer in _viewers.Values)
            if (viewer.VideoSubscribed)
                SendFeed(viewer);
    }

    private void OnFeedSpeech(long feedId, string speech)
    {
        foreach (var viewer in _viewers.Values)
        {
            if (!viewer.VideoSubscribed)
                continue;
            if (!VideoAllowed(viewer) || !EntityManager.TryGetNetEntity(viewer.Device, out var device))
            {
                StopVideo(viewer);
                continue;
            }
            RaiseNetworkEvent(new SWHolonetFeedEvent
            {
                Device = device.Value, Epoch = viewer.Epoch, Serial = viewer.VideoSerial,
                Generation = viewer.VideoGeneration, FeedId = feedId, Speech = speech,
            }, viewer.Session);
        }
    }
}
