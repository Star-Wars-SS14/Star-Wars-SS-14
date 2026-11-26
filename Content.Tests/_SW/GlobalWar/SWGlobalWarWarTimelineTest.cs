using System;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using NUnit.Framework;

namespace Content.Tests._SW.GlobalWar;

[TestFixture]
[TestOf(typeof(SWGlobalWarWarTimeline))]
public sealed class SWGlobalWarWarTimelineTest
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Test]
    public void ANewGenerationStartsOver()
    {
        var timeline = new SWGlobalWarWarTimeline();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        timeline.Record(first, Minute, SWGlobalWarFaction.Republic);
        timeline.Record(first, Minute * 2, SWGlobalWarFaction.Empire);

        Assert.That(timeline.Snapshot(first), Has.Count.EqualTo(2));
        Assert.That(timeline.Snapshot(second), Is.Empty, "A verdict must not show another campaign's changes.");

        timeline.Record(second, Minute, SWGlobalWarFaction.Empire);
        Assert.That(timeline.Snapshot(second), Has.Count.EqualTo(1));
        Assert.That(timeline.Snapshot(first), Is.Empty);
    }

    [Test]
    public void ChangesPastTheLimitAreNotRecorded()
    {
        var timeline = new SWGlobalWarWarTimeline();
        var generation = Guid.NewGuid();
        for (var i = 0; i < SWGlobalWarWarTimeline.Limit + 50; i++)
            timeline.Record(generation, Minute * i, SWGlobalWarFaction.Republic);

        var ticks = timeline.Snapshot(generation);
        Assert.That(ticks, Has.Count.EqualTo(SWGlobalWarWarTimeline.Limit));
        Assert.That(ticks[^1].At, Is.EqualTo(Minute * (SWGlobalWarWarTimeline.Limit - 1)));
    }

    [Test]
    public void ASnapshotIsACopyAndClearEmptiesIt()
    {
        var timeline = new SWGlobalWarWarTimeline();
        var generation = Guid.NewGuid();
        timeline.Record(generation, Minute, SWGlobalWarFaction.Republic);

        timeline.Snapshot(generation).Clear();
        Assert.That(timeline.Snapshot(generation), Has.Count.EqualTo(1));

        timeline.Clear();
        Assert.That(timeline.Snapshot(generation), Is.Empty);
    }
}
