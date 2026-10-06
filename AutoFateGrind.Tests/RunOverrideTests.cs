using AutoFateGrind.Core.Fork;

namespace AutoFateGrind.Tests;

/// <summary>
///     Fork: a run with another plugin's settings (BoatRunner's FATE levelling, 2026-10-06), for that run only. The user's own
///     settings must come back, and a save while the run goes must write the user's, never the run's.
/// </summary>
public sealed class RunOverrideTests : IDisposable
{
    private readonly List<string> saved = [];
    private readonly Configuration cfg = new()
    {
        SelectedZones = [956, 957],
        ModeId = "maxgemstones",
        ApplyClassOnStart = false,
        LevelRangeFilterEnabled = false,
        MaxLevelAbove = 5,
        MaxLevelBelow = 5,
        StopAfterFatesEnabled = true,
    };

    public RunOverrideTests()
    {
        RunOverride.Persist = c => saved.Add($"{string.Join(",", c.SelectedZones)}|{c.ModeId}|{c.ClassQueue.Count}");
        cfg.ClassQueue = [new ClassQueueEntry { GearsetIndex = 1, StopAtLevel = 0 }];
    }

    public void Dispose() => RunOverride.Restore(cfg);

    private static RunOverride.Request Levelling() => new()
    {
        Gearsets = [7], StopAtLevel = 15, Zones = [134, 135], MaxLevelAbove = 2, MaxLevelBelow = 10,
        SkipFateRules = ["Escort", "Chase", "EventFate", "ConcertedWorks", "Fete", "NoSuchRule"], SwapZoneWaitSec = 10,
    };

    [Fact]
    public void GivenALevellingRequest_ExpectItsSettingsForTheRun()
    {
        Assert.True(RunOverride.Apply(cfg, Levelling()));
        Assert.Equal([134u, 135u], cfg.SelectedZones);
        var entry = Assert.Single(cfg.ClassQueue);
        Assert.Equal((byte)7, entry.GearsetIndex);
        Assert.Equal(15, entry.StopAtLevel);
        Assert.Equal(AfterClassQueueDone.StopRun, cfg.AfterClassQueueDone);
        Assert.True(cfg.ApplyClassOnStart);
        Assert.True(cfg.LevelRangeFilterEnabled);
        Assert.Equal(2, cfg.MaxLevelAbove);
        Assert.False(cfg.StopAfterFatesEnabled);
    }

    [Fact]
    public void GivenTheRunEnded_ExpectTheUsersOwnSettingsBack()
    {
        RunOverride.Apply(cfg, Levelling());
        RunOverride.Restore(cfg);
        Assert.False(RunOverride.Active);
        Assert.Equal([956u, 957u], cfg.SelectedZones);
        Assert.Equal("maxgemstones", cfg.ModeId);
        Assert.Equal((byte)1, Assert.Single(cfg.ClassQueue).GearsetIndex);
        Assert.False(cfg.ApplyClassOnStart);
        Assert.Equal(5, cfg.MaxLevelAbove);
        Assert.True(cfg.StopAfterFatesEnabled);
    }

    [Fact]
    public void GivenASaveDuringTheRun_ExpectTheUsersSettingsWritten_TheRunsKept()
    {
        RunOverride.Apply(cfg, Levelling());
        Assert.True(RunOverride.SaveOriginal(cfg));
        Assert.Equal("956,957|maxgemstones|1", Assert.Single(saved)); // the user's, never the run's
        Assert.Equal([134u, 135u], cfg.SelectedZones);               // the run goes on with its own
    }

    [Fact]
    public void GivenNoOverride_ExpectAnOrdinarySave() =>
        Assert.False(RunOverride.SaveOriginal(cfg));

    [Fact]
    public void GivenAnOverrideAlreadyRunning_ExpectASecondOneRefused()
    {
        Assert.True(RunOverride.Apply(cfg, Levelling()));
        Assert.False(RunOverride.Apply(cfg, Levelling()));
    }

    [Fact]
    public void GivenAnEmptyRequest_ExpectItRefused_TheSettingsUntouched()
    {
        Assert.False(RunOverride.Apply(cfg, new RunOverride.Request { Gearsets = [], Zones = [134] }));
        Assert.Equal([956u, 957u], cfg.SelectedZones);
    }

    // ---- Solo, nonstop, FATEs the character can do (user, 2026-10-06) -------------------------------------------------------

    [Fact]
    public void GivenALevellingRun_ExpectItSolo_NeverFollowingOrLeading()
    {
        cfg.MultiboxRole = Core.Multibox.MultiboxRole.Follower;
        RunOverride.Apply(cfg, Levelling());
        Assert.Equal(Core.Multibox.MultiboxRole.Solo, cfg.MultiboxRole);
    }

    [Fact]
    public void GivenALevellingRun_ExpectItNonstop_NoGoalCapBreakTradeOrLogout()
    {
        cfg.HumanizerEnabled = true;
        cfg.TradeOnCap = true;
        cfg.SwapZonesWhenEmpty = false;
        cfg.AfterRun = AfterRunAction.Logout;
        RunOverride.Apply(cfg, Levelling());
        Assert.Equal("plainfates", cfg.ModeId);
        Assert.False(cfg.StopAfterFatesEnabled);
        Assert.False(cfg.StopAfterMinutesEnabled);
        Assert.False(cfg.HumanizerEnabled);
        Assert.False(cfg.TradeOnCap);
        Assert.True(cfg.SwapZonesWhenEmpty);
        Assert.Equal(10, cfg.SwapZoneWaitSec);
        Assert.Equal(AfterRunAction.StayLoggedIn, cfg.AfterRun);
    }

    [Fact]
    public void GivenALevellingRun_ExpectTheKindsItCannotDoAloneSkipped_AnUnknownNameIgnored()
    {
        RunOverride.Apply(cfg, Levelling());
        Assert.Equal(5, cfg.SkippedFateRules.Count);
        Assert.DoesNotContain(RunOverride.Rules(["Normal"]).Single(), cfg.SkippedFateRules);
        Assert.DoesNotContain(RunOverride.Rules(["Collect"]).Single(), cfg.SkippedFateRules);
        Assert.Contains(RunOverride.Rules(["Escort"]).Single(), cfg.SkippedFateRules);
    }

    [Fact]
    public void GivenTheRunEnded_ExpectTheUsersRoleSkipsAndBreaksBack()
    {
        cfg.MultiboxRole = Core.Multibox.MultiboxRole.Follower;
        cfg.HumanizerEnabled = true;
        cfg.SkippedFateRules = [1];
        cfg.AfterRun = AfterRunAction.Logout;
        RunOverride.Apply(cfg, Levelling());
        RunOverride.Restore(cfg);
        Assert.Equal(Core.Multibox.MultiboxRole.Follower, cfg.MultiboxRole);
        Assert.True(cfg.HumanizerEnabled);
        Assert.Equal([1], cfg.SkippedFateRules);
        Assert.Equal(AfterRunAction.Logout, cfg.AfterRun);
    }
}