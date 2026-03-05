using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;
public sealed partial class SWSpeciesJobRequirement : JobRequirement
{
    [DataField]
    public List<ProtoId<SpeciesPrototype>>? AllowedSpecies = null;

    [DataField]
    public List<ProtoId<SpeciesPrototype>>? DisallowedSpecies = null;

    public override bool Check(IEntityManager entManager,
       IPrototypeManager protoManager,
       HumanoidCharacterProfile? profile,
       IReadOnlyDictionary<string, TimeSpan> playTimes,
       [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (profile == null)
        {
            reason = null;
            return true;
        }

        var species = profile.Species;

        var message = FormattedMessage.FromMarkupPermissive("");
        bool allowed = true;

        if (AllowedSpecies != null && !AllowedSpecies.Contains(species))
        {
            message.AddMarkupPermissive($"{Loc.GetString("sw-allowed-species")}\n");
            foreach (var speciesId in AllowedSpecies)
            {
                var proto = protoManager.Index(speciesId);
                message.AddMarkupPermissive($"- {Loc.GetString(proto.Name)}\n");
            }
            allowed = false;
        }

        if (DisallowedSpecies != null && DisallowedSpecies.Contains(species))
        {
            message.AddMarkupPermissive($"{Loc.GetString("sw-disallowed-species")}\n");
            foreach (var speciesId in DisallowedSpecies)
            {
                var proto = protoManager.Index(speciesId);
                message.AddMarkupPermissive($"- {Loc.GetString(proto.Name)}\n");
            }
            allowed = false;
        }
        reason = message;
        return allowed;
    }
}
