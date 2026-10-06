using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Ipc;

// Fork: lets another local plugin (the boat orchestrator) drive a run like /afg start, /afg stop soft and /afg stop.
//   AutoFateGrind.IsRunning    () -> bool    a run is going (paused counts)
//   AutoFateGrind.Start        () -> bool    starts the selected zones like /afg start; true when a run is going after
//   AutoFateGrind.StopWhenSafe ()            ends the run once the current FATE is over and combat is clear
//   AutoFateGrind.Stop         ()            ends the run now
//   AutoFateGrind.Phase        () -> string  Idle, Grinding, Repairing, Trading, Humanizing, Finishing, Paused
//   AutoFateGrind.IsBusy       () -> bool    the local plugins' standard "doing something" (a run, incl. finishing the
//                                            FATE after StopWhenSafe); callers wait for false
//   AutoFateGrind.GoalReached  () -> bool    the last run ended because its goal was met (e.g. every Yo-kai zone done),
//                                            not a stop or a fault: restarting it would end again at once
//   AutoFateGrind.StartLevelling (json) -> bool  a run with these settings for this run only (Core.Fork.RunOverride.Request:
//                                            gear sets, the level to stop at, zones, the FATE level band); the user's settings
//                                            come back when it ends; false when it did not start
//   AutoFateGrind.IsLevelling  () -> bool    a run started by StartLevelling is going (a caller reloaded mid-run takes it back)
internal sealed class AfgIpcProvider : IDisposable
{
    private const string Prefix = "AutoFateGrind";

    private readonly ICallGateProvider<bool> isBusy;
    private readonly ICallGateProvider<bool> goalReached;
    private readonly ICallGateProvider<bool> isRunning;
    private readonly ICallGateProvider<bool> start;
    private readonly ICallGateProvider<object> stopWhenSafe;
    private readonly ICallGateProvider<object> stop;
    private readonly ICallGateProvider<string> phase;
    private readonly ICallGateProvider<string, bool> startLevelling;
    private readonly ICallGateProvider<bool> isLevelling;
    private readonly Plugin plugin;

    public AfgIpcProvider(Plugin plugin)
    {
        isRunning    = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.IsRunning");
        start        = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.Start");
        stopWhenSafe = Svc.PluginInterface.GetIpcProvider<object>($"{Prefix}.StopWhenSafe");
        stop         = Svc.PluginInterface.GetIpcProvider<object>($"{Prefix}.Stop");
        phase        = Svc.PluginInterface.GetIpcProvider<string>($"{Prefix}.Phase");

        isBusy       = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.IsBusy");
        isBusy.RegisterFunc(() => plugin.Controller.Running);
        goalReached  = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.GoalReached");
        goalReached.RegisterFunc(() => !plugin.Controller.Running && plugin.Controller.LastRunGoalReached);
        isRunning.RegisterFunc(() => plugin.Controller.Running);
        start.RegisterFunc(() =>
        {
            plugin.StartFromCommand();
            return plugin.Controller.Running;
        });
        stopWhenSafe.RegisterAction(() => plugin.Controller.StopWhenSafe());
        stop.RegisterAction(() => plugin.Controller.Stop());
        phase.RegisterFunc(() => plugin.Controller.Phase.ToString());

        this.plugin = plugin;
        startLevelling = Svc.PluginInterface.GetIpcProvider<string, bool>($"{Prefix}.StartLevelling");
        startLevelling.RegisterFunc(json =>
        {
            if (plugin.Controller.Running) return false;
            Core.Fork.RunOverride.Request? request;
            try { request = Newtonsoft.Json.JsonConvert.DeserializeObject<Core.Fork.RunOverride.Request>(json); }
            catch { return false; }
            if (request is null || !Core.Fork.RunOverride.Apply(Plugin.Cfg, request)) return false;
            plugin.StartFromCommand();
            if (plugin.Controller.Running) return true;
            Core.Fork.RunOverride.Restore(Plugin.Cfg);
            return false;
        });
        isLevelling = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.IsLevelling");
        isLevelling.RegisterFunc(() => plugin.Controller.Running && Core.Fork.RunOverride.Active);
        Svc.Framework.Update += OnUpdate;
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework _) => Core.Fork.RunOverride.Tick(plugin);

    public void Dispose()
    {
        isBusy.UnregisterFunc();
        goalReached.UnregisterFunc();
        isRunning.UnregisterFunc();
        start.UnregisterFunc();
        stopWhenSafe.UnregisterAction();
        stop.UnregisterAction();
        phase.UnregisterFunc();
        startLevelling.UnregisterFunc();
        isLevelling.UnregisterFunc();
        Svc.Framework.Update -= OnUpdate;
        Core.Fork.RunOverride.Restore(Plugin.Cfg); // an unload never leaves the run's settings in place
    }
}
