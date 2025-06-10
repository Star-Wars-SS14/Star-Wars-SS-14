using Content.Shared._SW;
using Content.Shared.Communications;
using Robust.Shared.Audio;

namespace Content.Server._SW.Artillery
{
    [RegisterComponent]
    public sealed partial class ArtilleryConsoleComponent : SharedCommunicationsConsoleComponent
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("artileryCalledProto")]
        public string? ArtilleryProto = default!;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("delayBetweenCallsInSeconds")]
        public float DelayBetweenCalls = 600;

        /// <summary>
        /// Announcement color
        /// </summary>
        [ViewVariables]
        [DataField]
        public Color Color = Color.Red;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public LocId Title = "artillery-console-announcement-sent-by";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public SoundSpecifier Sound = default!;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public LocId AnnouncementText = "artillery-console-announcement-text";

        /// <summary>
        ///  Показывает, сколько ударов артилерии осталось (то есть сколько раз еще ее можно вызвать)
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public int AttacksLeft = 8;

        [ViewVariables(VVAccess.ReadWrite)]
        public float CurrentDelay = 0f;
    }
}
