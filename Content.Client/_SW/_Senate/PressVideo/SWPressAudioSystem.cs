using Content.Shared._SW._Senate.PressVideo;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.GameTicking;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.PressVideo;

/// <summary>Plays only opt-in feed audio and retains its handle for immediate cancellation.</summary>
public sealed class SWPressAudioSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    private static readonly MemoryContentRoot Content = new();
    private static readonly ResPath Prefix = ResPath.Root / "SWPressTTS";
    private static bool _mounted;
    private readonly Dictionary<EntityUid, long> _listening = new();
    private long _nextToken;
    private EntityUid? _stream;
    private int _nextFile;
    public bool PlaybackFailed { get; private set; }
    public event Action<bool>? PlaybackFailureChanged;

    public override void Initialize()
    {
        if (!_mounted)
        {
            _resources.AddRoot(Prefix, Content);
            _mounted = true;
        }
        SubscribeNetworkEvent<SWPressAudioPlayEvent>(OnPlay);
        SubscribeNetworkEvent<SWPressAudioStopEvent>(OnStop);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnd);
    }

    public long SetListening(EntityUid screen, bool enabled)
    {
        if (enabled)
            _listening[screen] = ++_nextToken;
        else
        {
            _listening.Remove(screen);
            if (_listening.Count == 0)
                Stop();
        }
        return enabled ? _listening[screen] : 0;
    }

    private void OnPlay(SWPressAudioPlayEvent ev)
    {
        if (!_listening.ContainsValue(ev.Token) || ev.Data.Length == 0 || ev.Data.Length > 1024 * 1024)
            return;

        Stop();
        var path = new ResPath($"{_nextFile++}.ogg");
        try
        {
            Content.AddOrUpdateFile(path, ev.Data);
            var resource = new AudioResource();
            resource.Load(IoCManager.Instance!, Prefix / path);
            var specifier = new ResolvedPathSpecifier(Prefix / path);
            var volume = -10f + SharedAudioSystem.GainToVolume(_config.GetCVar(CCCVars.TTSVolume));
            _stream = _audio.PlayGlobal(resource.AudioStream, specifier, AudioParams.Default.WithVolume(volume))?.Entity;
            SetPlaybackFailed(_stream == null);
        }
        catch (Exception)
        {
            Stop();
            SetPlaybackFailed(true);
        }
        finally
        {
            Content.RemoveFile(path);
        }
    }

    private void SetPlaybackFailed(bool failed)
    {
        if (PlaybackFailed == failed)
            return;
        PlaybackFailed = failed;
        PlaybackFailureChanged?.Invoke(failed);
    }

    private void OnStop(SWPressAudioStopEvent ev) => Stop();

    private void OnRoundEnd(RoundRestartCleanupEvent ev)
    {
        _listening.Clear();
        Stop();
    }

    private void Stop()
    {
        _audio.Stop(_stream);
        _stream = null;
    }

    public override void Shutdown()
    {
        Stop();
        _listening.Clear();
        base.Shutdown();
    }
}
