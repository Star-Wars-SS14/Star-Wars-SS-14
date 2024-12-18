using Content.Server._SW.Force.Components;
using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.Force.Systems;

public sealed class StarHelmetSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly RadioDeviceSystem _radioDevice = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StarHelmetComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<StarHelmetComponent, GotUnequippedEvent>(OnGotUnequipped);

        SubscribeLocalEvent<WearingStarHelmetComponent, EntitySpokeEvent>(OnSpeak);
        SubscribeLocalEvent<StarHelmetComponent, RadioReceiveEvent>(OnHelmetReceive);
    }

    private void OnHelmetReceive(EntityUid helmet, StarHelmetComponent component, RadioReceiveEvent args)
    {
        if (TryComp(Transform(helmet).ParentUid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    private void OnSpeak(EntityUid helmet, WearingStarHelmetComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null)
        {
            _radio.SendRadioMessage(helmet, args.Message, args.Channel, component.Helmet);
            args.Channel = null;
        }
    }

    private void OnGotEquipped(EntityUid helmet, StarHelmetComponent component, GotEquippedEvent args)
    {
        EnsureComp<WearingStarHelmetComponent>(args.Equipee).Helmet = helmet;
        EnsureComp<ActiveRadioComponent>(helmet).Channels = new(component.Channels);

        if(!TryComp<RadioSpeakerComponent>(helmet, out var radioSpeakerComponent))
            return;

        if(radioSpeakerComponent.Enabled)
            _radioDevice.ToggleRadioSpeaker(helmet, helmet);
    }

    private void OnGotUnequipped(EntityUid helmet, StarHelmetComponent component, GotUnequippedEvent args)
    {
        RemComp<ActiveRadioComponent>(helmet);
        RemComp<WearingStarHelmetComponent>(args.Equipee);

        if(!TryComp<RadioSpeakerComponent>(helmet, out var radioSpeakerComponent))
            return;

        if(radioSpeakerComponent.Enabled == false)
            _radioDevice.ToggleRadioSpeaker(helmet, helmet);
    }
}
