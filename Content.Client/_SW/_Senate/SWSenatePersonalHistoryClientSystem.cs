using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate;

public sealed class SWSenatePersonalHistoryClientSystem : EntitySystem
{
    public event Action<SWSenatePersonalHistoryResponse>? Received;

    public override void Initialize() => SubscribeNetworkEvent<SWSenatePersonalHistoryResponse>(OnResponse);

    public void Request() => RaiseNetworkEvent(new SWSenatePersonalHistoryRequest());

    private void OnResponse(SWSenatePersonalHistoryResponse ev) => Received?.Invoke(ev);
}
