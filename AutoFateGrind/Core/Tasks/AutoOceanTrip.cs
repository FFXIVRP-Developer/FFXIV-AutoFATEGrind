using AutoFateGrind.Core.Game.Ops;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork: one step of the before-boat / after-boat lists, AutoDuty loop-action style.
public enum OceanTripStepKind { Sell, Discard, Retainers, Dagobert, Command }

// Fork: Newtonsoft fills an existing list on load instead of replacing it, so the step lists start null and are
// defaulted on first use (OceanTripSteps.Before/After), never in an initialiser.
[Serializable]
public sealed class OceanTripStep
{
    public OceanTripStepKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    // Kind == Command only: a chat command, sent as typed.
    public string Command { get; set; } = "";
}

// Fork: a fishing voyage in the middle of a FATE run. The grind stops at a safe point a few minutes before a voyage's
// registration opens; this task runs the before-boat steps, hands the voyage to Henchman ("On A Boat", IPC
// Henchman.StartOnABoat), cancels Henchman once the character is back on land (its task otherwise loops to the next
// voyage), runs the after-boat steps and re-equips the gear set the run was using. The controller then resumes the run.
// Every step is best-effort: a failed step is logged and the next one runs, so the run always comes back.
public sealed class AutoOceanTrip : AutoCommon
{
    // Ocean fishing instances (Indigo and Ruby routes).
    private static readonly uint[] BoatTerritories = [900, 1163];

    // Limsa Lominsa Lower Decks: the voyage desk, the merchant & mender Henchman sells at, and the city summoning bell.
    private const uint LimsaLowerDecks = 129;
    private static readonly Vector3 MerchantPosition = new(-399f, 3f, 80f);
    private static readonly Vector3 BellPosition = new(-123.888f, 17.990f, 21.469f);
    private const uint BellBaseId = 2000401;

    // Registration opens on even UTC hours for the first 13 minutes (Henchman's IsRegistrationOpen). The grind stops this
    // long before it, so the before-boat steps finish in time, and no later than this long after it opened.
    public const int LeadMinutes = 12;
    public const int LateMinutes = 4;

    private const int TeleportWatchdogMs = 60_000;
    private const int WalkWatchdogMs = 180_000;
    private const float WalkToleranceMeters = 3f;
    private const int InteractWaitMs = 15_000;
    private const int BoardWaitMs = 35 * 60_000;
    private const int VoyageWaitMs = 45 * 60_000;
    private const int LandSettleMs = 3_000;
    private const int ArStartWaitMs = 5_000;
    private const int ArSellWaitMs = 5 * 60_000;
    private const int ArRetainerWaitMs = 10 * 60_000;
    private const int DagobertStartWaitMs = 30_000;
    private const int DagobertWaitMs = 30 * 60_000;
    private const int GearsetWaitMs = 10_000;
    private const int PollMs = 500;

    private static readonly string[] BellAddons = ["RetainerList", "SelectYesno", "SelectString", "RetainerTaskAsk", "RetainerSellList", "RetainerSell"];

    // The voyage boundary (even UTC hour) a due check fired for; one trip per boundary.
    private static DateTime lastBoundaryUtc;

    /// <summary>The even UTC hour whose registration window the grind should stop for now, or null.</summary>
    public static DateTime? DueBoundary(Configuration cfg)
    {
        if (!cfg.OceanTripEnabled)
        {
            return null;
        }
        var now = DateTime.UtcNow;
        var ahead = now.AddMinutes(LeadMinutes);
        var boundary = new DateTime(ahead.Year, ahead.Month, ahead.Day, ahead.Hour - ahead.Hour % 2, 0, 0, DateTimeKind.Utc);
        var every = Math.Max(2, cfg.OceanTripEveryHours);
        if (boundary.Hour % every != 0 || boundary == lastBoundaryUtc)
        {
            return null;
        }
        if (now < boundary.AddMinutes(-LeadMinutes) || now > boundary.AddMinutes(LateMinutes))
        {
            return null;
        }
        return Henchman.Available ? boundary : null;
    }

    public static void MarkTaken(DateTime boundary) => lastBoundaryUtc = boundary;

    protected override async Task Execute()
    {
        var cfg = Plugin.Cfg;
        var gearset = CurrentGearset();
        Svc.Chat.Print("[AFG] Ocean fishing: leaving the FATEs for the next voyage.");
        Diag($"Ocean trip start: gear set {gearset + 1}, before-boat steps [{Describe(OceanTripSteps.Before(cfg))}], after-boat steps [{Describe(OceanTripSteps.After(cfg))}]");

        await RunSteps(OceanTripSteps.Before(cfg), "before the boat");
        if (CancelToken.IsCancellationRequested) return;

        var sailed = await Voyage();
        if (CancelToken.IsCancellationRequested) return;
        if (sailed)
        {
            await RunSteps(OceanTripSteps.After(cfg), "after the boat");
        }
        else
        {
            Diag("Ocean trip: no voyage happened; skipping the after-boat steps.");
        }

        await RestoreGearset(gearset);
        Svc.Chat.Print("[AFG] Ocean fishing trip done; back to the FATEs.");
    }

    private static string Describe(IEnumerable<OceanTripStep> steps)
        => string.Join(", ", steps.Where(s => s.Enabled).Select(s => s.Kind == OceanTripStepKind.Command ? $"Command '{s.Command}'" : s.Kind.ToString()));

    private async Task RunSteps(IEnumerable<OceanTripStep> steps, string when)
    {
        foreach (var step in steps.Where(s => s.Enabled).ToList())
        {
            if (CancelToken.IsCancellationRequested) return;
            Diag($"Ocean trip, {when}: {step.Kind}");
            try
            {
                var ok = step.Kind switch
                {
                    OceanTripStepKind.Sell      => await Sell(),
                    OceanTripStepKind.Discard   => await ArCommand("/ays discard", "Discarding", ArSellWaitMs),
                    OceanTripStepKind.Retainers => await Retainers(),
                    OceanTripStepKind.Dagobert  => await Dagobert(),
                    OceanTripStepKind.Command   => await Command(step.Command),
                    _                           => true,
                };
                if (!ok) Diag($"Ocean trip, {when}: {step.Kind} did not finish; going on with the next step.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !CancelToken.IsCancellationRequested)
            {
                Warn($"Ocean trip, {when}: {step.Kind} failed ({ex.Message}); going on with the next step.");
            }
            finally
            {
                CloseBellAddons();
            }
        }
    }

    // ---- Voyage (Henchman) -------------------------------------------------------------------------------------

    private async Task<bool> Voyage()
    {
        if (!Henchman.Available)
        {
            Warn("Ocean trip: Henchman's IPC is not available; no voyage.");
            return false;
        }
        if (Henchman.IsBusy())
        {
            Warn("Ocean trip: Henchman is already running a task; not starting On A Boat.");
            return false;
        }

        Status = "Ocean fishing: waiting for Henchman to board";
        Diag("Ocean trip: Henchman.StartOnABoat");
        Henchman.StartOnABoat();
        await DelayMs(2_000);

        var boarded = await WaitUntilTimed(() => OnBoat() || !Henchman.IsBusy(), BoardWaitMs, "ocean-board", 60);
        if (!OnBoat())
        {
            Warn(boarded
                ? "Ocean trip: Henchman stopped before boarding (see Henchman's log: unlocked? gear set? character set in On A Boat?)."
                : $"Ocean trip: not on a boat within {BoardWaitMs / 60_000} min; cancelling Henchman.");
            Henchman.CancelAllTasks();
            return false;
        }

        Status = "Ocean fishing";
        Diag($"Ocean trip: on the boat (territory {Svc.ClientState.TerritoryType}).");
        await WaitUntilTimed(() => !OnBoat(), VoyageWaitMs, "ocean-voyage", 60);

        Status = "Ocean fishing: landing";
        await WaitUntilTimed(PlayerReady, 120_000, "ocean-land");
        await DelayMs(LandSettleMs);
        Diag($"Ocean trip: back on land (territory {Svc.ClientState.TerritoryType}); cancelling Henchman before its next-voyage wait.");
        Henchman.CancelAllTasks();
        AbortLifestreamIfBusy();
        await WaitUntilTimed(PlayerReady, 60_000, "ocean-after-cancel");
        return true;
    }

    private static bool OnBoat() => BoatTerritories.Contains(Svc.ClientState.TerritoryType);

    private static bool PlayerReady()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && !Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        && !OnBoat();

    private static void AbortLifestreamIfBusy()
    {
        try
        {
            var busy = Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            if (busy.HasFunction && busy.InvokeFunc())
                Svc.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction();
        }
        catch (Exception ex)
        {
            Warn($"Lifestream abort failed: {ex.Message}");
        }
    }

    // ---- Steps -------------------------------------------------------------------------------------------------

    // AutoRetainer's item sell needs a merchant next to the character; Henchman uses the one at the voyage desk.
    private async Task<bool> Sell()
    {
        if (!await GoTo(MerchantPosition, "the merchant at the voyage desk")) return false;
        return await ArCommand("/ays itemsell", "Selling", ArSellWaitMs);
    }

    private async Task<bool> ArCommand(string command, string label, int waitMs)
    {
        if (!AutoRetainer.Available)
        {
            Warn($"Ocean trip: AutoRetainer is not loaded; skipping {command}.");
            return false;
        }
        Status = $"Ocean trip: {label}";
        Chat.ExecuteCommand(command);
        // A command with nothing to do never goes busy; that is a finished step, not a failure.
        if (!await WaitUntilTimed(AutoRetainer.IsBusy, ArStartWaitMs, $"ar-start {command}", 5))
        {
            Diag($"Ocean trip: AutoRetainer did not go busy after {command}; nothing to do.");
            return true;
        }
        return await WaitUntilTimed(() => !AutoRetainer.IsBusy(), waitMs, $"ar-done {command}");
    }

    // As AutoDuty's AutoRetainer step: open the bell, enable AutoRetainer until it goes busy, wait for it to finish.
    private async Task<bool> Retainers()
    {
        if (!AutoRetainer.Available)
        {
            Warn("Ocean trip: AutoRetainer is not loaded; skipping retainers.");
            return false;
        }
        if (!AutoRetainer.AnyRetainersReady())
        {
            Diag("Ocean trip: no retainer is ready; skipping retainers.");
            return true;
        }
        if (!await OpenBell()) return false;

        Status = "Ocean trip: retainers";
        var started = await WaitUntilTimed(() =>
        {
            if (AutoRetainer.IsBusy()) return true;
            Chat.ExecuteCommand("/autoretainer e");
            return false;
        }, 15_000, "ar-retainers-start", 60);
        if (!started)
        {
            Chat.ExecuteCommand("/autoretainer d");
            return false;
        }
        var done = await WaitUntilTimed(() => !AutoRetainer.IsBusy(), ArRetainerWaitMs, "ar-retainers-done", 60);
        if (!done) AutoRetainer.AbortAllTasks();
        Chat.ExecuteCommand("/autoretainer d");
        return done;
    }

    private async Task<bool> Dagobert()
    {
        if (!DagobertIpc.Available)
        {
            Warn("Ocean trip: Dagobert's IPC (local fork) is not available; skipping the pinch.");
            return false;
        }
        if (!await OpenBell()) return false;

        Status = "Ocean trip: Dagobert";
        if (!await WaitUntilTimed(DagobertIpc.PinchAllRetainers, DagobertStartWaitMs, "dagobert-start", 30))
        {
            return false;
        }
        var done = await WaitUntilTimed(() => !DagobertIpc.IsBusy(), DagobertWaitMs, "dagobert-done", 60);
        // Dagobert only lifts the AutoRetainer suppression on a clean finish.
        AutoRetainer.SetSuppressed(false);
        return done;
    }

    private async Task<bool> Command(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;
        Status = $"Ocean trip: {command}";
        Chat.ExecuteCommand(command.Trim());
        await DelayMs(2_000);
        return true;
    }

    // ---- Movement and the bell ---------------------------------------------------------------------------------

    private async Task<bool> GoTo(Vector3 position, string what)
    {
        await WaitUntilTimed(PlayerReady, 60_000, "ocean-ready");
        if (Svc.ClientState.TerritoryType != LimsaLowerDecks)
        {
            var reached = false;
            await RunWithStatusPinned($"Teleporting to Limsa Lominsa for {what}",
                async () => reached = await TeleportToTerritory(LimsaLowerDecks, position, "ocean-teleport", TeleportWatchdogMs));
            if (!reached)
            {
                Warn($"Ocean trip: could not reach Limsa Lominsa Lower Decks for {what}.");
                return false;
            }
        }
        if (WithinReach(position, WalkToleranceMeters)) return true;

        var arrived = false;
        await RunWithStatusPinned($"Walking to {what}", async () =>
        {
            await RideAethernetShortcut(position, "ocean-aethernet");
            arrived = await WalkWithRetries(
                () => new MoveOp(o => o.Move(LimsaLowerDecks, position,
                    MovementConfig.Everything.WithTolerance(WalkToleranceMeters), stopCondition: null)),
                WalkWatchdogMs, "ocean-walk", () => WithinReach(position, WalkToleranceMeters));
        });
        await SafeDismount("ocean-dismount");
        return arrived;
    }

    private async Task<bool> OpenBell()
    {
        if (BellAddonReady("RetainerList")) return true;
        if (!await GoTo(BellPosition, "the summoning bell")) return false;

        var bell = RepairOps.FindObjectByBaseId(BellBaseId);
        if (bell is null)
        {
            Warn($"Ocean trip: summoning bell {BellBaseId} not found near {BellPosition}.");
            return false;
        }
        Status = "Ocean trip: opening the summoning bell";
        var interact = new MoveOp(o => o.Interact(bell, waitUntil: () => BellAddonReady("RetainerList"), skip: UiSkipOptions.Talk));
        await RunCancellable(interact, InteractWaitMs, "ocean-bell");
        return await WaitUntilTimed(() => BellAddonReady("RetainerList"), 5_000, "ocean-bell-list");
    }

    private static unsafe bool BellAddonReady(string name)
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) && GenericHelpers.IsAddonReady(addon);

    // Back out of whatever bell window a step left open (one level per call, innermost first).
    private static unsafe void CloseBellAddons()
    {
        for (var pass = 0; pass < BellAddons.Length; pass++)
        {
            var closed = false;
            foreach (var name in BellAddons.Reverse())
            {
                if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(name, out var addon) && GenericHelpers.IsAddonReady(addon))
                {
                    addon->Close(true);
                    closed = true;
                    break;
                }
            }
            if (!closed) return;
        }
    }

    // ---- Gear set ----------------------------------------------------------------------------------------------

    private static unsafe int CurrentGearset()
    {
        var module = RaptureGearsetModule.Instance();
        return module is null ? -1 : module->CurrentGearsetIndex;
    }

    // Henchman switches to the Fisher gear set; the run goes back to the one it had.
    private async Task RestoreGearset(int gearset)
    {
        if (gearset < 0 || CurrentGearset() == gearset) return;
        await WaitUntilTimed(PlayerReady, 60_000, "ocean-gearset-ready");
        var equipped = await WaitUntilTimed(() => EquipGearset(gearset), GearsetWaitMs, "ocean-gearset", 30);
        Diag(equipped ? $"Ocean trip: gear set {gearset + 1} back on." : $"Ocean trip: could not re-equip gear set {gearset + 1}.");
    }

    private static unsafe bool EquipGearset(int gearset)
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null) return false;
        if (module->CurrentGearsetIndex == gearset) return true;
        module->EquipGearset(gearset, 0);
        return false;
    }

    // ---- IPC ---------------------------------------------------------------------------------------------------

    private static class Henchman
    {
        public static bool Available
            => Svc.PluginInterface.GetIpcSubscriber<object>("Henchman.StartOnABoat").HasAction
            && Svc.PluginInterface.GetIpcSubscriber<bool>("Henchman.IsBusy").HasFunction;

        public static void StartOnABoat() => Svc.PluginInterface.GetIpcSubscriber<object>("Henchman.StartOnABoat").InvokeAction();

        public static bool IsBusy()
        {
            try { return Svc.PluginInterface.GetIpcSubscriber<bool>("Henchman.IsBusy").InvokeFunc(); }
            catch { return false; }
        }

        public static void CancelAllTasks()
        {
            try { Svc.PluginInterface.GetIpcSubscriber<object>("Henchman.CancelAllTasks").InvokeAction(); }
            catch (Exception ex) { Warn($"Henchman.CancelAllTasks failed: {ex.Message}"); }
        }
    }

    private static class AutoRetainer
    {
        public static bool Available => Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").HasFunction;

        public static bool IsBusy()
        {
            try { return Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc(); }
            catch { return false; }
        }

        public static bool AnyRetainersReady()
        {
            try { return Svc.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.AreAnyRetainersAvailableForCurrentChara").InvokeFunc(); }
            catch { return false; }
        }

        public static void AbortAllTasks()
        {
            try { Svc.PluginInterface.GetIpcSubscriber<object>("AutoRetainer.PluginState.AbortAllTasks").InvokeAction(); }
            catch (Exception ex) { Warn($"AutoRetainer.AbortAllTasks failed: {ex.Message}"); }
        }

        public static void SetSuppressed(bool suppressed)
        {
            try { Svc.PluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed").InvokeAction(suppressed); }
            catch (Exception ex) { Warn($"AutoRetainer.SetSuppressed failed: {ex.Message}"); }
        }
    }

    private static class DagobertIpc
    {
        public static bool Available => Svc.PluginInterface.GetIpcSubscriber<bool>("Dagobert.PinchAllRetainers").HasFunction;

        public static bool PinchAllRetainers()
        {
            try { return Svc.PluginInterface.GetIpcSubscriber<bool>("Dagobert.PinchAllRetainers").InvokeFunc(); }
            catch { return false; }
        }

        public static bool IsBusy()
        {
            try { return Svc.PluginInterface.GetIpcSubscriber<bool>("Dagobert.IsBusy").InvokeFunc(); }
            catch { return false; }
        }
    }
}

// Fork: the saved step lists, defaulted on first use.
public static class OceanTripSteps
{
    public static List<OceanTripStep> Before(Configuration cfg)
        => cfg.OceanTripBefore ??= [new() { Kind = OceanTripStepKind.Sell }, new() { Kind = OceanTripStepKind.Discard }];

    public static List<OceanTripStep> After(Configuration cfg)
        => cfg.OceanTripAfter ??=
        [
            new() { Kind = OceanTripStepKind.Sell },
            new() { Kind = OceanTripStepKind.Discard },
            new() { Kind = OceanTripStepKind.Retainers },
            new() { Kind = OceanTripStepKind.Dagobert },
        ];
}
