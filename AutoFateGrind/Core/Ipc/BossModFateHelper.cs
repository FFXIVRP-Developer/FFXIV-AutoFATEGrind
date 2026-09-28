namespace AutoFateGrind.Core.Ipc;

internal static class BossModFateHelper
{
    public const string Module = "BossMod.Autorotation.MiscAI.FateUtils";
    public const string CollectTrack = "Collect";
    public const string DisabledOption = "Disabled";

    private const string ChocoboTrack = "Chocobo";
    private const long ChocoboRetryMs = 5_000;

    private static string? chocoboOverridePreset;
    private static long nextChocoboAttemptMs;

    public static void SyncChocobo(string preset, bool reapply)
    {
        if (Plugin.Cfg.AutoSummonChocobo)
        {
            ReleaseChocobo();
            return;
        }

        if (!reapply && chocoboOverridePreset == preset)
        {
            return;
        }

        if (!reapply && Environment.TickCount64 < nextChocoboAttemptMs)
        {
            return;
        }

        if (chocoboOverridePreset != preset)
        {
            ReleaseChocobo();
        }

        if (!BossModIPC.Instance.AddTransientStrategy(preset, Module, ChocoboTrack, DisabledOption))
        {
            nextChocoboAttemptMs = Environment.TickCount64 + ChocoboRetryMs;
            RunLog.Debug($"BossMod FATE helper Chocobo override not applied to '{preset}' (no FATE helper in the preset, or BossMod unavailable).");
            return;
        }

        if (chocoboOverridePreset != preset)
        {
            RunLog.Info($"BossMod FATE helper Chocobo strategy disabled on '{preset}'.");
        }
        chocoboOverridePreset = preset;
        nextChocoboAttemptMs = 0;
    }

    public static void ReleaseChocobo()
    {
        if (chocoboOverridePreset is null)
        {
            return;
        }

        BossModIPC.Instance.ClearTransientStrategy(chocoboOverridePreset, Module, ChocoboTrack);
        RunLog.Info($"BossMod FATE helper Chocobo override cleared from '{chocoboOverridePreset}'.");
        chocoboOverridePreset = null;
    }
}
