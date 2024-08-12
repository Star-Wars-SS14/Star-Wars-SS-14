using System.Text;
using Content.Server.Chat.Systems;
using Content.Server.Echo.Bank;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Maps;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.GameTicking.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Echo.GameTicking.Systems;

public sealed class SectorWarRuleSystem : GameRuleSystem<SectorWarRuleComponent>
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private SectorWarRuleComponent? _gameRule;

    protected override void AppendRoundEndText(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);
        if (_gameRule == null)
            return;
        var query = EntityQueryEnumerator<CapturableStationComponent>();
        var allianceCap = 0;
        var resistanceCap = 0;
        while (query.MoveNext(out var station))
        {
            switch (station.Fraction)
            {
                case SectorFractions.Alliance:
                    allianceCap++;
                    break;
                case SectorFractions.Resistance:
                    resistanceCap++;
                    break;
            }
        }

        var message = new StringBuilder();

        message.AppendLine(allianceCap > resistanceCap
            ? Loc.GetString("round-end-alliance-won")
            : allianceCap < resistanceCap
                ? Loc.GetString("round-end-resistance-won")
                : Loc.GetString("round-end-wtf-draw"));
        message.AppendLine();
        message.AppendLine(Loc.GetString("round-end-alliance-credits", ("credits", _gameRule.AllianceCredits)));
        message.AppendLine(Loc.GetString("round-end-resistance-credits", ("credits", _gameRule.ResistanceCredits)));
        args.AddLine(message.AppendLine().ToString());
        if (_gameRule.CurrentAudio != null && !Deleted(_gameRule.CurrentAudio.Value.Entity))
        {
            _audio.Stop(_gameRule.CurrentAudio?.Entity);
        }
        _gameRule.CurrentAudio = _audio.PlayGlobal(allianceCap > resistanceCap
                ? _gameRule.AllianceWinSound
                : _gameRule.ResistanceWinSound,
            Filter.Broadcast(),
            true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameRule == null || _gameRule.AlertDelays.Count == 0)
            return;

        if (_gameTiming.CurTime - _gameRule.LastAlertTime <= TimeSpan.FromMinutes(_gameRule.AlertDelays[0]))
            return;

        _gameRule.LastAlertTime = _gameTiming.CurTime;
        _gameRule.AlertDelays.RemoveAt(0);

        _audio.PlayGlobal(_gameRule.AlertSound, Filter.Broadcast(), true);
        _chat.DispatchGlobalAnnouncement(Loc.GetString("sector-war-round-end-alert",
            ("time", (TimeSpan.FromHours(3) - _gameTiming.CurTime).ToString())));
    }

    protected override void Added(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        _gameRule = component;
        var maps = new List<GameMapPrototype>();
        foreach (var map in component.Maps)
        {
            var gameMap = _prototypeManager.Index(map);
            maps.Add(gameMap);
            _map.CreateMap(out var mapId);
            GameTicker.LoadGameMap(gameMap, mapId, new MapLoadOptions { LoadMap = true, DoMapInit = true });
            //_map.InitializeMap(mapId);
            //_gameTicker.LoadGameMap(_prototype.Index<GameMapPrototype>(map.Id), mapId, null);
            _map.SetPaused(mapId, false);
            Log.Info($"Created map {mapId} for {ToPrettyString(uid):rule}");
            /*
            if (gameMap.MapPath is var path)
            {
                var options = new MapLoadOptions { LoadMap = true };
                if (!_mapLoader.TryLoad(mapId, path.ToString(), out var roots, options))
                {
                    Log.Error($"Failed to load map from {path}!");
                    Del(mapUid);
                    ForceEndSelf(uid, gameRule);
                    return;
                }

                grids = roots;
            }
            */
            //_map.SetPaused(mapId, false);
            EnsureComp<CapturableStationComponent>(_map.GetMap(mapId));
            var ev = new LoadingMapsEvent(maps);
            RaiseLocalEvent(uid, ev);
        }
    }
}
