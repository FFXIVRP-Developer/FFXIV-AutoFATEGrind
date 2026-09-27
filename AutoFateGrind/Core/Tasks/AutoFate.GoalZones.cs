using AutoFateGrind.Core.Game.Items;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private bool GoalZoneDone() => Plugin.Cfg.ActiveMode.IsZoneDone(zone.TerritoryId);

    private bool GoalZoneDoneAt(int candidateIndex) => Plugin.Cfg.ActiveMode.IsZoneDone(zones[candidateIndex].TerritoryId);

    private async Task<ExitReason> LeaveDoneZone()
    {
        var done = zone;
        var reason = Plugin.Cfg.ActiveMode.ZoneDoneReason(done.TerritoryId);
        Status = $"{done.Name} is done; moving on";
        await HoldForCollectReward();
        await WaitOutSettle();
        await ClearBlockingCombat();
        if (CancelToken.IsCancellationRequested)
        {
            return ExitReason.Quit;
        }

        if (AdvanceZone())
        {
            Diag($"{done.Name}: {reason}; moving on to {zone.Name}");
            Svc.Chat.Print($"[AFG] {done.Name}: {reason}. Moving on to {zone.Name}.");
            return ExitReason.Continue;
        }

        Diag($"{done.Name}: {reason}, and no other reachable zone is left; ending the run");
        Svc.Chat.Print($"[AFG] {done.Name}: {reason}, and no other reachable zone is left in the plan.");
        Status = "Every reachable zone is done";
        session.CompletedByStopCondition = true;
        return ExitReason.Quit;
    }

    private void ReportGoalMet()
    {
        ReportYokaiGoalMet();
        ReportItemGoalMet();
    }

    private void ReportItemGoalMet()
    {
        if (ItemGoalCatalog.Find(Plugin.Cfg.ActiveMode.Id) is not { } goal)
        {
            return;
        }

        var summary = ItemGoalProgress.Describe(goal, Plugin.Cfg);
        Diag($"Item goal {goal.DisplayName} at completion: {summary}");
        Svc.Chat.Print(ItemGoalProgress.IsAvailable(goal)
            ? $"[AFG] {goal.DisplayName}: every item is collected ({summary})."
            : $"[AFG] {goal.DisplayName}: the items no longer drop (quest turned in or relic unequipped); stopping with {summary}.");
    }
}
