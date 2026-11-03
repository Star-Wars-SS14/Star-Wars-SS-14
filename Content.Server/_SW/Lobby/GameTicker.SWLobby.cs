using System.Linq;
using Content.Server.Maps;
using Content.Shared._SW.Lobby;
using Content.Shared.GameTicking;

// ReSharper disable once CheckNamespace
namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    private SWLobbyFactsEvent GetSWLobbyFacts()
    {
        var preset = Decoy ?? CurrentPreset ?? Preset;
        return new SWLobbyFactsEvent(RoundId, _playerManager.PlayerCount,
            _playerGameStatuses.Values.Count(status => status == PlayerGameStatus.ReadyToPlay),
            _gameMapManager.GetSelectedMap()?.MapName ?? string.Empty,
            preset == null ? string.Empty : Loc.GetString(preset.ModeTitle),
            preset == null ? string.Empty : Loc.GetString(preset.Description));
    }
}
