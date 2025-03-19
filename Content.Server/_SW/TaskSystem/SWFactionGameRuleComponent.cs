using Content.Shared._SW.TaskSystem;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWFactionGameruleComponent : Component
{
    [DataField(required: true)]
    public List<string> FactionsPrototypes = new();

    public Dictionary<string, Entity<SWFactionComponent>?> FactionProtoToEntityDictionary = new();
}
