using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Lathe;

[Prototype("swLatheRecipe")]
public sealed partial class SWLatheRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public Dictionary<string, int> Materials = new Dictionary<string, int>();

    [DataField]
    public Dictionary<string, int> Result = new Dictionary<string, int>();

    [DataField]
    public float ProduceTime = 0.1f;
}

