using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Director;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Ghost;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Economy;

/// <summary>Shared public cache. Opening or filtering a map never triggers a new economic calculation.</summary>
public sealed class SWRepublicEconomyClientSystem : EntitySystem
{
    [Dependency] private readonly IBaseClient _client = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextRequest;
    private string _localFaction = "none";
    public SWEconomyView View { get; private set; } = new() { Paused = true };
    public event Action? Changed;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SWRepublicEconomyUpdateEvent>(OnUpdated);
        _client.RunLevelChanged += OnRunLevelChanged;
        _players.LocalPlayerAttached += OnBodyChanged;
        _players.LocalPlayerDetached += OnBodyChanged;
    }

    public override void Shutdown()
    {
        _client.RunLevelChanged -= OnRunLevelChanged;
        _players.LocalPlayerAttached -= OnBodyChanged;
        _players.LocalPlayerDetached -= OnBodyChanged;
        View = new SWEconomyView { Paused = true };
        Changed = null;
        base.Shutdown();
    }

    public void Request()
    {
        if (_client.RunLevel is not (ClientRunLevel.Connected or ClientRunLevel.InGame)
            || _timing.RealTime < _nextRequest)
            return;
        _nextRequest = _timing.RealTime + TimeSpan.FromSeconds(2);
        RaiseNetworkEvent(new SWRepublicEconomyRequestEvent());
    }

    private void OnBodyChanged(EntityUid entity)
    {
        View = new SWEconomyView { Paused = true };
        _nextRequest = TimeSpan.Zero;
        Changed?.Invoke();
        Request();
    }

    private string LocalFaction()
    {
        if (_players.LocalEntity is not { } entity || HasComp<GhostComponent>(entity)) return "none";
        if (HasComp<SWDarkCouncilMemberComponent>(entity)) return "empire";
        if (TryComp<SWFactionMemberComponent>(entity, out var member))
            return member.Faction switch { "EmpireFaction" => "empire", "RepublicFaction" => "republic", _ => "none" };
        return "republic";
    }

    public override void Update(float frameTime)
    {
        var faction = LocalFaction();
        if (_localFaction != faction)
        {
            _localFaction = faction;
            View = new SWEconomyView { Paused = true };
            _nextRequest = TimeSpan.Zero;
            Changed?.Invoke();
        }
        if (View.ViewerFaction != faction) Request();
    }

    private void OnUpdated(SWRepublicEconomyUpdateEvent message)
    {
        var body = _players.LocalEntity is { } entity ? GetNetEntity(entity) : (NetEntity?) null;
        if (message.ViewerEntity != body || message.View.ViewerFaction != LocalFaction()) return;
        if (message.View.ViewerFaction == View.ViewerFaction && message.View.Revision < View.Revision)
            return;
        View = message.View;
        Changed?.Invoke();
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel is ClientRunLevel.Initialize or ClientRunLevel.Connecting)
        {
            View = new SWEconomyView { Paused = true };
            _nextRequest = TimeSpan.Zero;
            Changed?.Invoke();
        }
        else if (args.NewLevel is ClientRunLevel.Connected or ClientRunLevel.InGame)
            Request();
    }
}
