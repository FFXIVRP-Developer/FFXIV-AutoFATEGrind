using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Fork;

// Fork (README-FORK item 17): "Resume the run after a reload" (Settings → General, saved). A run going when the plugin
// unloads (a rebuild, a crash, a game restart) starts again on the next load, once in the world, out of a duty, and not
// while BoatRunner is busy with the boat. Item 6 did this before and was removed because it fought BoatRunner by
// restarting AFG when BoatRunner did not want it; this one only resumes a run that was really going, once, and holds
// back during BoatRunner's boat phases. Off: never starts on its own.
internal static class ReloadResume
{
    private static readonly TimeSpan FirstTry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
    private const int MaxTries = 120; // ten minutes of loading screens, logins and duties

    private static readonly HashSet<string> BoatPhases = ["StoppingFillers", "BeforeBoat", "OnBoat", "AfterBoat"];

    private static CancellationTokenSource? lifetime;

    public static void OnLoad(Plugin plugin)
    {
        if (!Plugin.Cfg.ResumeAfterReload || !Plugin.Cfg.ResumeAfterReloadPending)
        {
            Clear();
            return;
        }
        lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        Svc.Framework.RunOnTick(() => TryResume(plugin, 1, token), FirstTry, cancellationToken: token);
    }

    public static void OnUnload(Plugin plugin)
    {
        lifetime?.Cancel();
        lifetime = null;
        var pending = Plugin.Cfg.ResumeAfterReload && plugin.Controller.Running;
        if (Plugin.Cfg.ResumeAfterReloadPending == pending) return;
        Plugin.Cfg.ResumeAfterReloadPending = pending;
        Plugin.Cfg.Save();
    }

    private static void TryResume(Plugin plugin, int attempt, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        if (!Plugin.Cfg.ResumeAfterReload)
        {
            Clear();
            return;
        }

        if (BoatRunnerBusy())
        {
            // The boat comes first; BoatRunner starts its fillers again afterwards.
            Svc.Log.Info("[AFG] Resume after reload: BoatRunner is busy with the boat; not resuming.");
            Clear();
            return;
        }

        var ready = Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer is not null
                 && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BoundByDuty];
        if (!ready)
        {
            if (attempt < MaxTries) Svc.Framework.RunOnTick(() => TryResume(plugin, attempt + 1, token), Retry, cancellationToken: token);
            else Clear();
            return;
        }

        Clear();
        if (plugin.Controller.Running) return;
        Svc.Chat.Print("[AFG] Resuming the run that was going before the reload (Settings → General → Resume the run after a reload).");
        plugin.StartFromCommand();
    }

    private static void Clear()
    {
        if (!Plugin.Cfg.ResumeAfterReloadPending) return;
        Plugin.Cfg.ResumeAfterReloadPending = false;
        Plugin.Cfg.Save();
    }

    private static bool BoatRunnerBusy()
    {
        try
        {
            var gate = Svc.PluginInterface.GetIpcSubscriber<string>("BoatRunner.Phase");
            return gate.HasFunction && BoatPhases.Contains(gate.InvokeFunc());
        }
        catch
        {
            return false;
        }
    }
}
