using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Ipc;

// Fork: lets another local plugin (the boat orchestrator) drive a run like /afg start, /afg stop soft and /afg stop.
//   AutoFateGrind.IsRunning    () -> bool    a run is going (paused counts)
//   AutoFateGrind.Start        () -> bool    starts the selected zones like /afg start; true when a run is going after
//   AutoFateGrind.StopWhenSafe ()            ends the run once the current FATE is over and combat is clear
//   AutoFateGrind.Stop         ()            ends the run now
//   AutoFateGrind.Phase        () -> string  Idle, Grinding, Repairing, Trading, Humanizing, Finishing, Paused
internal sealed class AfgIpcProvider : IDisposable
{
    private const string Prefix = "AutoFateGrind";

    private readonly ICallGateProvider<bool> isRunning;
    private readonly ICallGateProvider<bool> start;
    private readonly ICallGateProvider<object> stopWhenSafe;
    private readonly ICallGateProvider<object> stop;
    private readonly ICallGateProvider<string> phase;

    public AfgIpcProvider(Plugin plugin)
    {
        isRunning    = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.IsRunning");
        start        = Svc.PluginInterface.GetIpcProvider<bool>($"{Prefix}.Start");
        stopWhenSafe = Svc.PluginInterface.GetIpcProvider<object>($"{Prefix}.StopWhenSafe");
        stop         = Svc.PluginInterface.GetIpcProvider<object>($"{Prefix}.Stop");
        phase        = Svc.PluginInterface.GetIpcProvider<string>($"{Prefix}.Phase");

        isRunning.RegisterFunc(() => plugin.Controller.Running);
        start.RegisterFunc(() =>
        {
            plugin.StartFromCommand();
            return plugin.Controller.Running;
        });
        stopWhenSafe.RegisterAction(() => plugin.Controller.StopWhenSafe());
        stop.RegisterAction(() => plugin.Controller.Stop());
        phase.RegisterFunc(() => plugin.Controller.Phase.ToString());
    }

    public void Dispose()
    {
        isRunning.UnregisterFunc();
        start.UnregisterFunc();
        stopWhenSafe.UnregisterAction();
        stop.UnregisterAction();
        phase.UnregisterFunc();
    }
}
