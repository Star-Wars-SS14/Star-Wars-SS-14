using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._SW._Senate.Debate;

public sealed class SWSenateFloorSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;

    private static readonly Color TribuneColor = Color.Gold;
    private static readonly Color GrantColor = Color.FromHex("#9B59B6");

    private EntityUid? _speaker;
    private EntityUid? _chancellor;
    private EntityUid? _floorHolder;
    private bool _speakerActive;
    private readonly List<EntityUid> _queue = new();

    public EntityUid? Speaker => _speaker;
    public EntityUid? Chancellor => _chancellor;
    public EntityUid? FloorHolder => _floorHolder;
    public IReadOnlyList<EntityUid> Queue => _queue;

    public override void Initialize()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _speaker = null;
        _chancellor = null;
        _floorHolder = null;
        _speakerActive = false;
        _queue.Clear();
    }

    public void SetSpeaker(EntityUid? uid)
    {
        _speaker = uid;
        if (uid == null)
            _speakerActive = false;
    }

    public void SetChancellor(EntityUid? uid) => _chancellor = uid;

    public bool ToggleSpeaker(EntityUid user)
    {
        _speaker = user;
        _speakerActive = !_speakerActive;
        return _speakerActive;
    }

    public bool RequestFloor(EntityUid requester, bool priority)
    {
        if (requester == _floorHolder || _queue.Contains(requester))
            return false;

        if (priority)
            _queue.Insert(0, requester);
        else
            _queue.Add(requester);

        return true;
    }

    public int GetQueuePosition(EntityUid requester)
    {
        var idx = _queue.IndexOf(requester);
        return idx < 0 ? 0 : idx + 1;
    }

    public void GrantFloor(EntityUid uid)
    {
        _queue.Remove(uid);
        _floorHolder = uid;
        AnnounceGrant(uid);
    }

    public void RevokeFloor()
    {
        _floorHolder = null;
    }

    public void GrantChancellor()
    {
        if (_chancellor is not { } chancellor)
            return;

        _queue.Remove(chancellor);
        _floorHolder = chancellor;
        AnnounceGrant(chancellor);
    }

    private void AnnounceGrant(EntityUid uid)
    {
        var msg = Loc.GetString("sw-senate-floor-granted-announce", ("name", Name(uid)));
        _chat.ChatMessageToManyFiltered(Filter.Broadcast(), ChatChannel.Radio, msg, msg, uid, false, true, GrantColor);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (args.Channel != null)
            return;

        var source = args.Source;
        var onTribune = source == _floorHolder || (source == _speaker && _speakerActive);
        if (!onTribune)
            return;

        var wrapped = Loc.GetString("sw-senate-tribune-wrap",
            ("name", Name(source)),
            ("message", FormattedMessage.EscapeText(args.Message)));

        _chat.ChatMessageToManyFiltered(Filter.Broadcast(), ChatChannel.Radio, args.Message, wrapped, source, false, true, TribuneColor);
    }
}
