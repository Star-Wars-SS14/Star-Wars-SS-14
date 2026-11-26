using Content.Client.Eye;
using Content.Shared._SW._Senate.PressVideo;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.PressVideo;

// EyeLerpingSystem itself has no ownership count.
public sealed class SWPressVideoEyeSystem : EntitySystem
{
    [Dependency] private readonly EyeLerpingSystem _eyes = default!;
    private readonly Dictionary<EntityUid, int> _references = new();

    public void Acquire(EntityUid eye)
    {
        if (_references.TryGetValue(eye, out var count))
            _references[eye] = count + 1;
        else
        {
            _references[eye] = 1;
            _eyes.AddEye(eye);
        }
    }

    public void Release(EntityUid eye)
    {
        if (!_references.TryGetValue(eye, out var count))
            return;
        if (count > 1)
            _references[eye] = count - 1;
        else
        {
            _references.Remove(eye);
            if (!Deleted(eye))
                _eyes.RemoveEye(eye);
        }
    }
}

public sealed class SWPressCameraBoundUserInterface : BoundUserInterface
{
    private SWPressCameraWindow? _window;

    public SWPressCameraBoundUserInterface(EntityUid owner, Enum key) : base(owner, key) { }

    protected override void Open()
    {
        base.Open();
        _window = new SWPressCameraWindow();
        _window.OnClose += Close;
        _window.Start += topic => SendMessage(new SWPressStartMessage(topic));
        _window.Stop += () => SendMessage(new SWPressStopMessage());
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWPressVideoState video)
            _window?.SetState(video);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is SWPressVideoErrorMessage error)
            _window?.SetError(error.Text);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _window?.Dispose();
    }
}

public sealed class SWPressViewerBoundUserInterface : BoundUserInterface
{
    private SWPressViewerWindow? _window;
    private NetEntity? _target;
    private EntityUid? _eye;
    private readonly SWPressVideoEyeSystem _eyes;

    public SWPressViewerBoundUserInterface(EntityUid owner, Enum key) : base(owner, key)
    {
        _eyes = EntMan.System<SWPressVideoEyeSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _window = new SWPressViewerWindow();
        _window.OnClose += Close;
        _window.Tick += RefreshEye;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not SWPressVideoState video) return;
        if (_target != video.Camera)
        {
            ReleaseEye();
            _target = video.Camera;
        }
        _window?.SetState(video);
        RefreshEye();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is SWPressVideoSpeechMessage speech)
            _window?.AddSpeech(speech);
    }

    private void RefreshEye()
    {
        if (_window == null || _target == null) return;
        var uid = EntMan.GetEntity(_target);
        if (uid == null || !EntMan.TryGetComponent<EyeComponent>(uid, out var eye) || eye.Eye == null)
        {
            ReleaseEye();
            return;
        }
        if (_eye != uid)
        {
            ReleaseEye();
            _eye = uid;
            _eyes.Acquire(uid.Value);
        }
        _window.SetEye(eye.Eye);
    }

    private void ReleaseEye()
    {
        if (_eye is { } eye) _eyes.Release(eye);
        _eye = null;
        _window?.SetEye(null);
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseEye();
        base.Dispose(disposing);
        if (disposing) _window?.Dispose();
    }
}
