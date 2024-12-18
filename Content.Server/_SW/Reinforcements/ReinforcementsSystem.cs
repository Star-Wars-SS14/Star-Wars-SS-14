using Content.Server._SW.Reinforcements.Components;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._SW.Reinforcements;

public sealed class ReinforcementsSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReinforcementsCardComponent, AfterInteractEvent>(AfterInteract);
    }

    private void AfterInteract(EntityUid card, ReinforcementsCardComponent component, AfterInteractEvent args)
    {
        if(!args.Target.HasValue)
            return;

        var target = args.Target;

        if(!TryComp<CommunicationsConsoleComponent>(target, out var communicationsConsoleComponent))
            return;

        if (communicationsConsoleComponent.SwFractionType != component.FractionType)
        {
            _popupSystem.PopupEntity("Отказано в доступе!", args.User, args.User, PopupType.MediumCaution);
            return;
        }

        communicationsConsoleComponent.CanReinforcements = !communicationsConsoleComponent.CanReinforcements;
        var status = communicationsConsoleComponent.CanReinforcements ? "активировала" : "деактивировала";
        var sound = communicationsConsoleComponent.CanReinforcements ? component.ConsoleSoundActivated : null;
        Dictionary<StarWarsFractionType, string> fractionLoc = new()
        {
            { StarWarsFractionType.Empire, "Империи" },
            { StarWarsFractionType.Republic, "Республики" },
            { StarWarsFractionType.Mandalorian, "Мандалорцев" }
        };

        _chatSystem.DispatchGlobalAnnouncement($"Консоль {fractionLoc} {status} кнопку вызова подкреплений!", "Система общего взаимодействия 'Сатурн'", colorOverride: Color.IndianRed, announcementSound: sound);
    }
}
