using Dalamud.Game;
using Dalamud.Game.Chat;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Game.Watchers;

internal sealed record TeleportRefusal(uint LogMessageId, string Text, long Tick)
{
    public bool IsAlreadyUnderway => LogMessageId == TeleportRefusals.AlreadyUnderwayId;
    public bool IsPermanent => LogMessageId is TeleportRefusals.NotAttunedId or TeleportRefusals.InsufficientGilId;
    public bool IsInsufficientGil => LogMessageId == TeleportRefusals.InsufficientGilId;
}

// The game names the reason for a refused cast in a LogMessage row; condition flags only let us guess at it (issue #67).
// Rows are picked by id from the English sheet, so the match works in every client language.
internal static class TeleportRefusals
{
    public const uint NotAttunedId = 1663;
    public const uint AlreadyUnderwayId = 1665;
    public const uint InsufficientGilId = 1669;

    private static readonly string[] RefusalPrefixes =
    {
        "Unable to teleport",
        "Unable to execute",
        "Cannot execute",
    };

    private static Dictionary<uint, string> refusalTexts = new();
    private static volatile TeleportRefusal? latest;

    public static void Initialize()
    {
        refusalTexts = BuildRefusalTexts();
        Svc.Chat.LogMessage += OnLogMessage;
    }

    public static void Shutdown()
    {
        Svc.Chat.LogMessage -= OnLogMessage;
    }

    public static TeleportRefusal? Since(long tick)
    {
        var refusal = latest;
        return refusal is not null && refusal.Tick >= tick ? refusal : null;
    }

    private static void OnLogMessage(ILogMessage message)
    {
        if (!refusalTexts.TryGetValue(message.LogMessageId, out var text))
        {
            return;
        }
        latest = new TeleportRefusal(message.LogMessageId, text, Environment.TickCount64);
    }

    private static Dictionary<uint, string> BuildRefusalTexts()
    {
        var texts = new Dictionary<uint, string>();
        var sheet = Svc.Data.GetExcelSheet<LogMessage>(ClientLanguage.English);
        foreach (var row in sheet)
        {
            var text = row.Text.ExtractText();
            if (!StartsWithRefusalPrefix(text))
            {
                continue;
            }
            texts[row.RowId] = text;
        }
        return texts;
    }

    private static bool StartsWithRefusalPrefix(string text)
    {
        for (var prefixIndex = 0; prefixIndex < RefusalPrefixes.Length; prefixIndex++)
        {
            if (text.StartsWith(RefusalPrefixes[prefixIndex], StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
