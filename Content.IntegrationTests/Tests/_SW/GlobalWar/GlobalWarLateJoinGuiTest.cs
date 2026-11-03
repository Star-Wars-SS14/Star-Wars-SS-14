#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._SW.LateJoin;
using Content.Client.LateJoin;
using Content.Client.Lobby;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Players;
using Content.Shared.Players.JobWhitelist;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarLateJoinGuiTest
{
    [TestPrototypes]
    private static readonly string Prototypes = BuildPrototypes();

    private static string BuildPrototypes()
    {
        var jobs = string.Join("\n", Enumerable.Range(0, 36).Select(i => $"""
- type: job
  id: SWLateJoinRow{i}
  name: job-name-passenger
  description: job-description-passenger
  playTimeTracker: SWLateJoinTracker{i}
  icon: JobIconCaptain
{(i == 2 ? "  whitelisted: true" : "")}
{(i == 3 ? "  requirements:\n  - !type:OverallPlaytimeRequirement\n    time: 999h" : "")}
{(i == 4 ? "  requirements:\n  - !type:SpeciesRequirement\n    species: [Zeltron]" : "")}

- type: playTimeTracker
  id: SWLateJoinTracker{i}

"""));
        return jobs + "\n- type: department\n  id: SWLateJoinDepartment\n  name: department-Command\n  description: department-Command-description\n  color: " + "\"#3344AA\"" + "\n  editorHidden: true\n  roles:\n" +
               string.Join("\n", Enumerable.Range(0, 36).Select(i => $"  - SWLateJoinRow{i}"));
    }

    [Test]
    public async Task OpenWindowTracksFullSnapshotsAndRestrictionsWhileKeepingTheViewedStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var player = server.PlayerMan.Sessions.Single();
        var net = server.ResolveDependency<IServerNetManager>();
        var prefs = client.ResolveDependency<IClientPreferencesManager>();
        var originalProfile = (HumanoidCharacterProfile)prefs.Preferences!.SelectedCharacter;
        var originalTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var originalWhitelist = server.CfgMan.GetCVar(CCVars.GameRoleWhitelist);
        var first = new NetEntity(50001);
        var second = new NetEntity(50002);
        var third = new NetEntity(50003);
        var names = new Dictionary<NetEntity, string> { [first] = "First station", [second] = "Second station" };
        var jobs = new Dictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>>
        {
            [first] = new() { ["SWLateJoinRow0"] = 1 },
            [second] = Enumerable.Range(0, 35).ToDictionary(i => (ProtoId<JobPrototype>)$"SWLateJoinRow{i}", _ => (int?)1),
        };
        LateJoinGui? gui = null;
        Dictionary<NetEntity, SWLateJoinScrollContainer> Sections()
        {
            return (Dictionary<NetEntity, SWLateJoinScrollContainer>)typeof(LateJoinGui)
                .GetField("_jobLists", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gui)!;
        }
        void Layout()
        {
            // Headless integration ticks do not drain the normal rendered-frame layout queue.
            foreach (var control in Descendants(gui!).Reverse())
                control.InvalidateMeasure();
            gui!.InvalidateMeasure();
            gui.Measure(new Vector2(640, 560));
            gui.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(640, 560)));
        }
        ContainerButton Row(NetEntity station, int index)
        {
            return Descendants(Sections()[station]).OfType<ContainerButton>().Single(c =>
                c.GetType().GetProperty("JobId")?.GetValue(c) as string == $"SWLateJoinRow{index}");
        }
        async Task Publish()
        {
            // Send a complete server DTO; the client updates both dictionaries before notifying the open GUI.
            var snapshot = new TickerJobsAvailableEvent(new(names), jobs.ToDictionary(p => p.Key,
                p => new Dictionary<ProtoId<JobPrototype>, int?>(p.Value)));
            await server.WaitPost(() => server.System<SWLateJoinSnapshotTestSystem>().Publish(snapshot));
            await pair.RunTicksSync(5);
        }
        try
        {
            await pair.RunTicksSync(5);
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, true);
                server.CfgMan.SetCVar(CCVars.GameRoleWhitelist, true);
                net.ServerSendMessage(new MsgRoleBans { JobBans = ["SWLateJoinRow1"] }, player.Channel);
                net.ServerSendMessage(new MsgJobWhitelist(), player.Channel);
                net.ServerSendMessage(new MsgPlayTime(), player.Channel);
            });
            await client.WaitPost(() => prefs.UpdateCharacter(originalProfile.WithSpecies("Human"), 0));
            await Publish();
            await client.WaitAssertion(() =>
            {
                gui = new LateJoinGui();
                gui.OpenCentered();
                Layout();
                Sections()[first].Visible = false;
                Sections()[second].Visible = true;
                Layout();
                Sections()[second].SetScrollValue(new Vector2(0, 200));
                Assert.That(Sections()[second].VScroll, Is.EqualTo(200));
            });
            foreach (var amount in new int?[] { 0, 1, 0, null })
            {
                for (var i = 0; i < 5; i++)
                    jobs[second][$"SWLateJoinRow{i}"] = amount;
                await Publish();
                await client.WaitAssertion(() =>
                {
                    Layout();
                    Assert.That(Sections()[second].Visible, Is.True);
                    Assert.That(Sections()[second].VScroll, Is.EqualTo(200));
                    Assert.That(Row(second, 0).Disabled, Is.EqualTo(amount == 0));
                    Assert.That(Row(second, 0).GetType().GetProperty("Amount")!.GetValue(Row(second, 0)), Is.EqualTo(amount));
                    for (var i = 1; i < 5; i++)
                    {
                        Assert.That(Row(second, i).Disabled, Is.True, $"Restriction {i} was lost on vacancy update.");
                        Assert.That(Row(second, i).TooltipSupplier, Is.Not.Null);
                    }
                });
            }
            // Requirement messages also rebuild the window and must preserve its viewing state.
            await server.WaitPost(() =>
            {
                net.ServerSendMessage(new MsgRoleBans(), player.Channel);
                net.ServerSendMessage(new MsgJobWhitelist { Whitelist = ["SWLateJoinRow2"] }, player.Channel);
                net.ServerSendMessage(new MsgPlayTime { Trackers = new() { ["Overall"] = TimeSpan.FromHours(1000) } }, player.Channel);
            });
            await client.WaitPost(() => prefs.UpdateCharacter(originalProfile.WithSpecies("Zeltron"), 0));
            await Publish();
            await client.WaitAssertion(() =>
            {
                Layout();
                Assert.That(Sections()[second].Visible, Is.True);
                Assert.That(Sections()[second].VScroll, Is.EqualTo(200));
                for (var i = 0; i < 5; i++)
                    Assert.That(Row(second, i).Disabled, Is.False, $"Restriction {i} did not clear.");
            });
            names[second] = "Renamed station";
            names.Add(third, "New station");
            jobs.Add(third, new() { ["SWLateJoinRow0"] = 1 });
            jobs[second].Remove("SWLateJoinRow10");
            jobs[second]["SWLateJoinRow35"] = 1;
            await Publish();
            await client.WaitAssertion(() =>
            {
                Layout();
                Assert.That(Sections().Keys, Is.EquivalentTo(names.Keys));
                Assert.That(Descendants(gui!).OfType<Label>().Any(l => l.Text == "Renamed station"), Is.True);
                Assert.That(Descendants(Sections()[second]).OfType<ContainerButton>().Any(c =>
                    c.GetType().GetProperty("JobId")?.GetValue(c) as string == "SWLateJoinRow10"), Is.False);
                Assert.That(Row(second, 35).Disabled, Is.False);
                Assert.That(Sections()[second].Visible, Is.True);
                Assert.That(Sections()[second].VScroll, Is.EqualTo(200));
            });
            jobs[second] = new() { ["SWLateJoinRow0"] = 1 };
            await Publish();
            await client.WaitAssertion(() =>
            {
                Layout();
                Assert.That(Sections()[second].Visible, Is.True);
                Assert.That(Sections()[second].VScroll, Is.Zero, "Shrink must clamp to the new extent.");
            });
            names.Remove(second);
            jobs.Remove(second);
            jobs[first].Clear();
            await Publish();
            await client.WaitAssertion(() =>
            {
                Layout();
                Assert.That(Sections().ContainsKey(second), Is.False);
                Assert.That(Sections()[first].Visible, Is.False);
                Assert.That(Sections()[third].Visible, Is.True, "Choose the first nonempty section after removal.");
            });
        }
        finally
        {
            await client.WaitPost(() =>
            {
                gui?.Dispose();
                prefs.UpdateCharacter(originalProfile, 0);
            });
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, originalTimers);
                server.CfgMan.SetCVar(CCVars.GameRoleWhitelist, originalWhitelist);
                net.ServerSendMessage(new MsgRoleBans(), player.Channel);
                net.ServerSendMessage(new MsgJobWhitelist(), player.Channel);
                net.ServerSendMessage(new MsgPlayTime(), player.Channel);
            });
            await pair.RunTicksSync(5);
        }
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}

public sealed class SWLateJoinSnapshotTestSystem : EntitySystem
{
    public void Publish(TickerJobsAvailableEvent snapshot)
    {
        RaiseNetworkEvent(snapshot);
    }
}
