using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server.Maps;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.Lobby;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    private SWLobbyFactsEvent GetSWLobbyFacts()
    {
        var preset = Decoy ?? CurrentPreset ?? Preset;
        var (map, mapCaption) = GetSWLobbyMap(CurrentPreset ?? Preset);
        return new SWLobbyFactsEvent(RoundId, _playerManager.PlayerCount,
            _playerGameStatuses.Values.Count(status => status == PlayerGameStatus.ReadyToPlay),
            map,
            mapCaption,
            preset == null ? string.Empty : Loc.GetString(preset.ModeTitle),
            preset == null ? string.Empty : Loc.GetString(preset.Description));
    }

    /// <summary>
    /// The map the round will load, or for the campaign the worlds its players start on. The campaign's game map is an
    /// empty placeholder: its players are spawned on their own side's capital, so the lobby names those.
    /// </summary>
    private (string Map, string Caption) GetSWLobbyMap(GamePresetPrototype? preset)
    {
        foreach (var rule in preset?.Rules ?? [])
        {
            if (!_prototypeManager.TryIndex(rule, out var proto)
                || !proto.TryGetComponent<SWGlobalWarGameruleComponent>(out var campaign, EntityManager.ComponentFactory))
            {
                continue;
            }

            var capitals = campaign.Planets
                .Select(id => _prototypeManager.Index(id))
                .Where(world => world.Kind == SWGlobalWarSectorKind.Capital && world.InitialOwner != SWGlobalWarFaction.Neutral)
                .OrderBy(world => world.InitialOwner)
                .Select(world => world.Name)
                .ToArray();
            if (capitals.Length > 0)
                return (string.Join(" · ", capitals), Loc.GetString("sw-lobby-map-campaign"));
        }

        return (_gameMapManager.GetSelectedMap()?.MapName ?? Loc.GetString("sw-lobby-map-none"), Loc.GetString("sw-lobby-map"));
    }
}
