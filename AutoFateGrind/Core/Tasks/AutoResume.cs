using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

// Fork: restarts a run that was going when the plugin last unloaded (reload, game exit, crash). Starting a run
// remembers it in the config; Stop or a run that ends on its own forgets it. An unload must not forget it, and
// the run-end callbacks of an unloading instance still fire after Dispose, so Plugin.Dispose raises Unloading first.
internal sealed class AutoResume : IDisposable
{
    public static bool Unloading;

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

        // Starts the moment the character is in game and every required plugin is loaded (after a game launch
        // vnavmesh/BossMod may load after AFG); the run itself waits for the navmesh.
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null || Svc.Condition[ConditionFlag.BetweenAreas]
         || !External.ExternalPlugins.AllRequiredInstalled())
        {
            return;
        }

        finished = true;
        if (Plugin.Instance.Controller.Running) return;
        RunLog.Info("Auto-resume: a run was going when the plugin last unloaded; starting it again.");
        Svc.Chat.Print("[AFG] Resuming the run that was going before the reload. Press Stop to keep it stopped next time.");
        Plugin.Instance.StartFromCommand();
    }
}
