namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarDiplomacyTest
{
    [TestPrototypes]
    private const string CombatPrototypes = """
- type: entity
  parent: SWGlobalWarCaptureConsole
  id: SWCombatTestCapture
  components:
  - type: SWGlobalWarCaptureConsole
    duration: 3
    abandonmentGrace: 2
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: SWGlobalWarShipyardConsole
  id: SWCombatTestShipyard
  components:
  - type: ApcPowerReceiver
    needsPower: false
  - type: SWGlobalWarMapAnchor
    anchorId: shipyardPrimary
    kind: Shipyard
    links:
      dock: {sector: SWSpawnTestRCapital, location: Orbit, anchorId: fleetTestDock}
      clearance: {sector: SWSpawnTestRCapital, location: Orbit, anchorId: fleetTestClearance}

- type: entity
  parent: AirlockShuttle
  id: SWCombatTestDock
  components:
  - type: ApcPowerReceiver
    needsPower: false
  - type: SWGlobalWarMapAnchor
    anchorId: fleetTestDock
    kind: Dock

- type: entity
  parent: SWGlobalWarPointMarker
  id: SWCombatTestClearance
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: fleetTestClearance
    kind: Clearance
    min: -128,-128
    max: 128,128
""";
}
