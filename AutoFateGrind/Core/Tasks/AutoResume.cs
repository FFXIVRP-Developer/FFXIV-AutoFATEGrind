using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

// Fork: restarts a run that was going when the plugin last unloaded (reload, game exit, crash). Starting a run
// remembers it in the config; Stop or a run that ends on its own forgets it. An unload must not forget it, and
// the run-end callbacks of an unloading instance still fire after Dispose, so Plugin.Dispose raises Unloading first.
internal sealed class AutoResume : IDisposable
{
    // Lets the previous load's game, plugins and navmesh settle before the run starts.
    private const int SettleMs = 15_000;

    public static bool Unloading;

    private long readySinceMs;
    private bool finished;

    public AutoResume() => Svc.Framework.Update += Tick;

    public void Dispose() => Svc.Framework.Update -= Tick;

    public static void MarkStarted()
    {
        if (Plugin.Cfg.ResumeRunPending) return;
        Plugin.Cfg.ResumeRunPending = true;
        Plugin.Cfg.Save();
    }

    public static void MarkEnded()
    {
        if (Unloading || !Plugin.Cfg.ResumeRunPending) return;
        Plugin.Cfg.ResumeRunPending = false;
        Plugin.Cfg.Save();
    }

    private void Tick(IFramework framework)
    {
        if (finished) return;
        if (!Plugin.Cfg.ResumeRunPending)
        {
            finished = true;
            return;
        }

        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            readySinceMs = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (readySinceMs == 0) readySinceMs = now;
        if (now - readySinceMs < SettleMs) return;

        finished = true;
        if (Plugin.Instance.Controller.Running) return;
        RunLog.Info("Auto-resume: a run was going when the plugin last unloaded; starting it again.");
        Svc.Chat.Print("[AFG] Resuming the run that was going before the reload. Press Stop to keep it stopped next time.");
        Plugin.Instance.StartFromCommand();
    }
}
