using Robust.Shared.Serialization;

namespace Content.Shared._SW.Lobby;

/// <summary>
/// What the lobby shows about the round, field by field. The upstream info blob is one piece of text, which can only
/// be shown as a wall of it.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWLobbyFactsEvent(int roundId, int players, int ready, string map, string mode, string description) : EntityEventArgs
{
    public readonly int RoundId = roundId;
    public readonly int Players = players;
    public readonly int Ready = ready;
    public readonly string Map = map;
    public readonly string Mode = mode;
    public readonly string Description = description;
}
