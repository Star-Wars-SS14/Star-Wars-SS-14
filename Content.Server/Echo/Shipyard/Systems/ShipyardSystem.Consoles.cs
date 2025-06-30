using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Echo.Bank;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.Echo.GameTicking.Systems;
using Content.Server.Maps;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Database;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.Echo.Shipyard.BUI;
using Content.Shared.Echo.Shipyard.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Content.Shared.Shipyard;
using Content.Shared.Shipyard.Components;
using Content.Shared.Shipyard.Events;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Echo.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly BankSystem _bank = default!;

    private void OnPurchaseMessage(EntityUid uid,
        ShipyardConsoleComponent component,
        ShipyardConsolePurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!_prototypeManager.TryIndex<VesselPrototype>(args.Vessel, out var vessel))
        {
            ConsolePopup(args.Actor, Loc.GetString("shipyard-console-invalid-vessel", ("vessel", args.Vessel)));
            PlayDenySound(args.Actor, uid, component);
            return;
        }

        if (!GetAvailableShuttles(uid).Contains(vessel.ID))
        {
            PlayDenySound(args.Actor, uid, component);
            _adminLogger.Add(LogType.Action,
                LogImpact.Medium,
                $"{ToPrettyString(player):player} tried to purchase a vessel that was never available.");
            return;
        }

        var name = vessel.Name;
        if (vessel.Price <= 0)
            return;

        if (_station.GetOwningStation(uid) is not { Valid: true } station)
        {
            ConsolePopup(args.Actor, Loc.GetString("shipyard-console-invalid-station"));
            PlayDenySound(args.Actor, uid, component);
            return;
        }


        if (!_bank.TryWithdraw(player, vessel.Price))
        {
            ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", vessel.Price)));
            PlayDenySound(args.Actor, uid, component);
            return;
        }

        if (!TryComp<SWFactionMemberComponent>(args.Actor, out var comp))
        {
            PlayDenySound(args.Actor, uid, component);
            return;
        }

        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        var price = vessel.Price;

        //we assume that there is only one rule sectorwar
        query.MoveNext(out _, out var rule);
        if (rule == null || rule.FactionMoney[comp.Faction] < price)
        {
            PlayDenySound(args.Actor, uid, component);
            return;
        }
        if (!TryPurchaseShuttle(station, vessel.ShuttlePath.ToString(), out var shuttle))
        {
            PlayDenySound(args.Actor, uid, component);
            return;
        }
        rule.FactionMoney[comp.Faction] -= price;
        EntityUid? shuttleStation = null;
        // setting up any stations if we have a matching game map prototype to allow late joins directly onto the vessel
        if (_prototypeManager.TryIndex<GameMapPrototype>(vessel.ID, out var stationProto))
        {
            List<EntityUid> gridUids = new()
            {
                shuttle.Owner
            };
            shuttleStation = _station.InitializeNewStation(stationProto.Stations[vessel.ID], gridUids);
            var metaData = MetaData((EntityUid)shuttleStation);
            name = metaData.EntityName;
            _shuttle.SetIFFColor(shuttle.Owner,
                new Color
                {
                    R = 10,
                    G = 50,
                    B = 100,
                    A = 100
                });
            _shuttle.AddIFFFlag(shuttle.Owner, IFFFlags.None);
        }


        // The following block of code is entirely to do with trying to sanely handle moving records from station to station.
        // it is ass.
        // This probably shouldnt be messed with further until station records themselves become more robust
        // and not entirely dependent upon linking ID card entity to station records key lookups
        // its just bad

        var stationList = EntityQueryEnumerator<StationRecordsComponent>();

        if (shuttleStation.HasValue)
        {
            _records.Synchronize(shuttleStation.Value);
        }

        _records.Synchronize(station);
        if (component.ShipyardChannel is { } channel)
            SendPurchaseMessage(uid, player, name, channel, secret: false);
        if (component.SecretShipyardChannel is { } secretChannel)
            SendPurchaseMessage(uid, player, name, secretChannel, secret: true);

        PlayConfirmSound(args.Actor, uid, component);

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(player):actor} purchased shuttle {ToPrettyString(shuttle.Owner)} for {vessel.Price} credits via {ToPrettyString(component.Owner)}");

        RefreshState(uid, (ShipyardConsoleUiKey)args.UiKey);
    }

    private void OnConsoleUIOpened(EntityUid uid, ShipyardConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!component.Initialized)
            return;

        RefreshState(uid, (ShipyardConsoleUiKey)args.UiKey);
    }

    private void ConsolePopup(EntityUid uid, string text)
    {
        _popup.PopupEntity(text, uid);
    }

    private void SendPurchaseMessage(EntityUid uid, EntityUid player, string name, string shipyardChannel, bool secret)
    {
        var channel = _prototypeManager.Index<RadioChannelPrototype>(shipyardChannel);

        if (secret)
        {
            _radio.SendRadioMessage(uid, Loc.GetString("shipyard-console-docking-secret"), channel, uid);
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("shipyard-console-docking-secret"),
                InGameICChatType.Speak,
                true);
        }
        else
        {
            _radio.SendRadioMessage(uid,
                Loc.GetString("shipyard-console-docking", ("owner", player), ("vessel", name)),
                channel,
                uid);
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("shipyard-console-docking", ("owner", player), ("vessel", name)),
                InGameICChatType.Speak,
                true);
        }
    }

    private void PlayDenySound(EntityUid playerUid, EntityUid consoleUid, ShipyardConsoleComponent component)
    {
        _audio.PlayEntity(component.ErrorSound, playerUid, consoleUid);
    }

    private void PlayConfirmSound(EntityUid playerUid, EntityUid consoleUid, ShipyardConsoleComponent component)
    {
        _audio.PlayEntity(component.ConfirmSound, playerUid, consoleUid);
    }

    /// <summary>
    /// Looks for a living, sapient being aboard a particular entity.
    /// </summary>
    /// <param name="uid">The entity to search (e.g. a shuttle, a station)</param>
    /// <param name="mobQuery">A query to get the MobState from an entity</param>
    /// <param name="xformQuery">A query to get the transform component of an entity</param>
    /// <returns>The name of the sapient being if one was found, null otherwise.</returns>
    public string? FoundOrganics(EntityUid uid,
        EntityQuery<MobStateComponent> mobQuery,
        EntityQuery<TransformComponent> xformQuery)
    {
        var xform = xformQuery.GetComponent(uid);
        var childEnumerator = xform.ChildEnumerator;

        while (childEnumerator.MoveNext(out var child))
        {
            if (mobQuery.TryGetComponent(child, out var mobState)
                && !_mobState.IsDead(child, mobState)
                && _mind.TryGetMind(child, out var mind, out var mindComp)
                && !_mind.IsCharacterDeadIc(mindComp))
                return mindComp.CharacterName;
            else
            {
                var charName = FoundOrganics(child, mobQuery, xformQuery);
                if (charName != null)
                    return charName;
            }
        }

        return null;
    }

    /// <summary>
    ///   Returns all shuttle prototype IDs the given shipyard console can offer.
    /// </summary>
    public List<string> GetAvailableShuttles(EntityUid uid,
        ShipyardConsoleUiKey? key = null,
        ShipyardListingComponent? listing = null)
    {
        var availableShuttles = new List<string>();

        if (key == null && TryComp<UserInterfaceComponent>(uid, out var ui))
        {
            // Try to find a ui key that is an instance of the shipyard console ui key
            foreach (var (k, v) in ui.Actors)
            {
                if (k is not ShipyardConsoleUiKey shipyardKey)
                    continue;

                key = shipyardKey;
                break;
            }
        }


        // No listing provided, try to get the current one from the console being used as a default.
        if (listing is null)
            TryComp(uid, out listing);

        var station = _station.GetOwningStation(uid);

        foreach (var vessel in _prototypeManager.EnumeratePrototypes<VesselPrototype>())
        {
            if (station == null)
                continue;

            EnsureComp<CapturableStationComponent>(station.Value, out var capturableStationComponent);
            // Check that the listing contains the shuttle or that the shuttle is in the group that the console is looking for
            if ((listing?.Shuttles.Contains(vessel.ID) ?? false) ||
                // if the listing contains the shuttle, add it to the list or

                // if the shuttle is in the group that the console is looking for
                vessel.Fraction == capturableStationComponent.Fraction)
            {
                availableShuttles.Add(vessel.ID);
            }
        }

        return availableShuttles;
    }

    private void RefreshState(EntityUid uid,
        ShipyardConsoleUiKey uiKey)
    {
        int balance = 0;
        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        EnsureComp<CapturableStationComponent>(station.Value, out var capturableStationComponent);

        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        while (query.MoveNext(out _, out var rule))
        {
            if (capturableStationComponent.Fraction == null)
            {
                continue;
            }
            balance = rule.FactionMoney[capturableStationComponent.Fraction];
            //balance = capturableStationComponent.Fraction switch
            //{
            //    SectorFractions.Alliance => rule.AllianceCredits,
            //    SectorFractions.Resistance => rule.ResistanceCredits,
            //    _ => balance,
            //};
        }

        var newState = new ShipyardConsoleInterfaceState(
            balance,
            (byte)uiKey,
            GetAvailableShuttles(uid, uiKey),
            uiKey.ToString());

        _ui.SetUiState(uid, uiKey, newState);
    }
}
