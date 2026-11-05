using Content.Client._SW._Senate.PressVideo;
using Content.Client.Viewport;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared._SW._Senate.PressVideo;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Holonet;

/// <summary>Embedded view of the same eye and speech used by the press viewer.</summary>
public sealed class SWHolonetFeedPanel : BoxContainer
{
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly SWPressVideoEyeSystem _eyes;
    private readonly FixedEye _emptyEye = new();
    private readonly ScalingViewport _camera;
    private readonly SWConsoleStyle.WrappingRichTextLabel _details;
    private readonly SWConsoleStyle.WrappingRichTextLabel _speech;
    private readonly List<string> _lines = new();
    private NetEntity? _target;
    private EntityUid? _eye;
    private long _feedId;
    private bool _live;

    public SWHolonetFeedPanel()
    {
        _eyes = _entities.System<SWPressVideoEyeSystem>();
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 8;
        _details = new SWConsoleStyle.WrappingRichTextLabel(400) { HorizontalExpand = true };
        AddChild(_details);
        _camera = new ScalingViewport
        {
            HorizontalExpand = true,
            SetHeight = 240,
            MouseFilter = MouseFilterMode.Ignore,
            ViewportSize = new Vector2i(640, 360),
            Eye = _emptyEye,
            Visible = false,
        };
        AddChild(_camera);
        AddChild(SWConsoleStyle.Text(Loc.GetString("sw-holonet-feed-speech"), SWHolonetPalette.Mint, SWConsoleStyle.Strong));
        _speech = new SWConsoleStyle.WrappingRichTextLabel(400) { HorizontalExpand = true };
        AddChild(_speech);
        Reset();
    }

    public void Apply(SWHolonetFeedEvent ev)
    {
        if (!string.IsNullOrEmpty(ev.Error))
        {
            Reset();
            SetText(_details, ev.Error);
            return;
        }
        if (ev.State is { } state)
        {
            if (_target != state.Camera)
            {
                ReleaseEye();
                _target = state.Camera;
            }
            if (state.Camera == null || _feedId != state.FeedId)
                _lines.Clear();
            _live = state.Camera != null;
            _feedId = _live ? state.FeedId : 0;
            var details = !_live ? Loc.GetString("sw-holonet-feed-offline") :
                $"Тема: {state.Topic}\nВедущий: {state.Presenter}\nИздание: {state.Edition}";
            SetText(_details, details);
            UpdateSpeech();
            if (!_live)
                _camera.Visible = false;
        }
        else if (_live && ev.FeedId == _feedId && !string.IsNullOrEmpty(ev.Speech))
        {
            _lines.Insert(0, ev.Speech);
            if (_lines.Count > 5)
                _lines.RemoveAt(_lines.Count - 1);
            UpdateSpeech();
        }
    }

    public void Reset()
    {
        ReleaseEye();
        _target = null;
        _live = false;
        _feedId = 0;
        _lines.Clear();
        SetText(_details, Loc.GetString("sw-holonet-feed-offline"));
        UpdateSpeech();
    }

    private void UpdateSpeech() => SetText(_speech, _lines.Count == 0
        ? Loc.GetString("sw-holonet-feed-waiting") : string.Join("\n", _lines));

    private static void SetText(RichTextLabel label, string value)
    {
        var message = new FormattedMessage();
        message.AddText(value);
        label.SetMessage(message);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_target == null)
            return;
        var uid = _entities.GetEntity(_target);
        if (uid == null || !_entities.TryGetComponent<EyeComponent>(uid, out var eye) || eye.Eye == null)
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
        _camera.Eye = eye.Eye;
        _camera.Visible = _live;
    }

    private void ReleaseEye()
    {
        if (_eye is { } eye)
            _eyes.Release(eye);
        _eye = null;
        _camera.Eye = _emptyEye;
        _camera.Visible = false;
    }

    protected override void ExitedTree()
    {
        ReleaseEye();
        base.ExitedTree();
    }
}
