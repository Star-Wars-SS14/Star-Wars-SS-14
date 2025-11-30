using Content.Shared._SW.Lathe;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe;

[RegisterComponent]
public sealed partial class SWLatheComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public List<ProtoId<SWLatheRecipePrototype>> Recipes = new List<ProtoId<SWLatheRecipePrototype>>();

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string FixtureInPrefix = "in";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string FixtureOutPrefix = "out";

    [DataField]
    public Vector2 SpawnPointOut = new Vector2(0.5f, 0.2f); 

    /// <summary>
    /// Maximum amount per ONE material
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxMaterialAmount = 3;

    public Dictionary<EntProtoId, int> StashedMaterials = new Dictionary<EntProtoId, int>();

    [ViewVariables(VVAccess.ReadOnly)]
    public SWLatheRecipePrototype? CurrentRecipeProducting;

    public TimeSpan ProductionEndTime;
}
