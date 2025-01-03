using Content.Shared._SW;
using Content.Shared.Communications;
using Robust.Shared.Audio;

namespace Content.Server._SW.Artillery
{
    [RegisterComponent]
    public sealed partial class ArtilleryConsoleComponent : SharedCommunicationsConsoleComponent
    {
        [ViewVariables]
        [DataField("artileryCalledProto")]
        public string? ArtilleryProto = default!;

        [ViewVariables]
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

        public float CurrentDelay = 0f;
    }
}
