using System.Linq;
using System.Text;
using Content.Server._SW.TaskSystem;
using Content.Server.Chat.Systems;
using Content.Server.Echo.Bank;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
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
    [Dependency] private readonly SharedGameTicker _sharedGameTicker = default!;
    [Dependency] private readonly SWFactionGameruleSystem _sWFaction = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    public SectorWarRuleComponent? GameRuleSingleton;

    protected override void AppendRoundEndText(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);
        if (GameRuleSingleton == null)
            return;

        var message = new StringBuilder();
        message.AppendLine("Очки фракций:");
        foreach (var (faction, score) in component.FactionsScore)
        {
            var entfact = _sWFaction.GetFactionEntity(faction);
            if (entfact == null)
            {
                continue;
            }
            message.AppendLine($"{entfact.Value.Comp.Name} : {score} очков");
        }
        args.AddLine(message.AppendLine().ToString());
    }
    protected override void ActiveTick(EntityUid uid, SectorWarRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);
        List<EntityUid> republicStations = new();
        List<EntityUid> empireStations = new();
        var query = EntityQueryEnumerator<CapturableStationComponent>();

        while (query.MoveNext(out var stationUid, out var fact))
        {
            if (fact.Fraction != null && fact.Fraction == "RepublicFaction")
            {
                republicStations.Add(stationUid);
            }

            if (fact.Fraction != null && fact.Fraction == "EmpireFaction")
            {
                empireStations.Add(stationUid);
            }
        }
        //There must be a better way to do it but i am too lazy
        if (_gameTiming.CurTime >= _sharedGameTicker.RoundStartTimeSpan
                                    + TimeSpan.FromSeconds(comp.StatesEndTimes[comp.CurState])
                                    && comp.CurState != SWSpaceWarGameState.AttackingFull) {
            comp.CurState++;
            foreach (var station in republicStations)
            {
                _chat.DispatchStationAnnouncement(station,
                             Loc.GetString(comp.RepublicAnnouncementTexts[comp.CurState]),
                             announcementSound: comp.AnnouncementSounds[comp.CurState],
                             sender: Loc.GetString(comp.RepublicSender),
                             colorOverride: comp.RepublicAnnouncementColor);
            }

            foreach (var station in empireStations)
            {
                _chat.DispatchStationAnnouncement(station,
                         Loc.GetString(comp.EmpireAnnouncementTexts[comp.CurState]),
                         announcementSound: comp.AnnouncementSounds[comp.CurState],
                         sender: Loc.GetString(comp.EmpireSender),
                         colorOverride: comp.EmpireAnnouncementColor);
            }
        }
    }
    protected override void Added(EntityUid uid,
        SectorWarRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        GameRuleSingleton = component;
        var maps = new List<GameMapPrototype>();


        //foreach (var map in component.Maps)
        //{
        //    var gameMap = _prototypeManager.Index(map);
        //    maps.Add(gameMap);
        //    _map.CreateMap(out var mapId);
        //    GameTicker.LoadGameMap(gameMap, mapId, new MapLoadOptions { LoadMap = true, DoMapInit = true });
        //    //_map.InitializeMap(mapId);
        //    //_gameTicker.LoadGameMap(_prototype.Index<GameMapPrototype>(map.Id), mapId, null);
        //    _map.SetPaused(mapId, false);
        //    Log.Info($"Created map {mapId} for {ToPrettyString(uid):rule}");
        //    /*
        //    if (gameMap.MapPath is var path)
        //    {
        //        var options = new MapLoadOptions { LoadMap = true };
        //        if (!_mapLoader.TryLoad(mapId, path.ToString(), out var roots, options))
        //        {
        //            Log.Error($"Failed to load map from {path}!");
        //            Del(mapUid);
        //            ForceEndSelf(uid, gameRule);
        //            return;
        //        }

        //        grids = roots;
        //    }
        //    */
        //    //_map.SetPaused(mapId, false);
        //    var station = _stationSystem.GetStationInMap(mapId);
        //    if (station != null)
        //    {
        //        EnsureComp<CapturableStationComponent>(station.Value);
        //    }
        //    var ev = new LoadingMapsEvent(maps);
        //    RaiseLocalEvent(uid, ev);
        //}
    }

    protected override void Started(EntityUid uid, SectorWarRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        var factions = _sWFaction.GetFactions();
        foreach (var faction in factions.Keys)
        {
            component.FactionMoney[faction] = component.StartBalance;
        }

        foreach (var (factionid, _) in factions)
        {
            component.FactionsScore[factionid] = 0;
        }
    }
}
