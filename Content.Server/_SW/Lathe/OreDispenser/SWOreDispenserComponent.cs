using Content.Shared.Whitelist;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.OreDispenser;

[RegisterComponent]
public sealed partial class SWOreDispenserComponent : Component
{
    [DataField]
    public EntityWhitelist DispenseWhitelist { get; set; }

    [DataField]
    public float DelayBetweenSpawns = 1.0f;

    [DataField]
    public string ContainerName = "storagebase";

    [DataField]
    public Vector2 SpawnPointOut = new Vector2(0.0f, -0.65f);

    [DataField]
    public string InputFixtureId = "ore_in";

    public TimeSpan NextDispense;
}
