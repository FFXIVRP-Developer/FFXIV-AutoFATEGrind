using AutoFateGrind.Core;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Trading;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoFateGrind.Windows.Components;

internal static class WalletChip
{
    private const float IconGap = 5f;

    public static bool TryRead(out int count)
    {
        if (Svc.Objects.LocalPlayer is null)
        {
            count = 0;
            return false;
        }

        return GemstoneCatalog.TryCurrentWalletCount(out count);
    }

    public static float Width(int count)
    {
        using (Fonts.PushCaption())
        {
            return TextDraw.IconSize(FontAwesomeIcon.Gem).X + IconGap * ImGuiHelpers.GlobalScale + TextDraw.Measure(Label(count)).X;
        }
    }

    public static void Draw(Configuration cfg, int count, float rightX, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var label = Label(count);
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            var iconSize = TextDraw.IconSize(FontAwesomeIcon.Gem);
            var labelX = rightX - labelSize.X;
            var iconX = labelX - IconGap * scale - iconSize.X;
            TextDraw.Icon(FontAwesomeIcon.Gem, new Vector2(iconX, midY - iconSize.Y * 0.5f), Styling.AccentAmber);
            TextDraw.At(label, new Vector2(labelX, midY - labelSize.Y * 0.5f), Tint(cfg, count));

            var halfHeight = MathF.Max(labelSize.Y, iconSize.Y) * 0.5f;
            if (Hit.HoveringRect(new Vector2(iconX, midY - halfHeight), new Vector2(rightX, midY + halfHeight)))
            {
                ShowTooltip(cfg, count);
            }
        }
    }

    private static string Label(int count) => count.ToString("N0", Loc.Culture);

    private static bool Capped(int count) => count >= AfgConstants.BicolorCap;

    private static bool TradeReady(Configuration cfg, int count) => cfg.TradeOnCap && count >= cfg.TradeThreshold;

    private static Vector4 Tint(Configuration cfg, int count)
    {
        if (Capped(count)) return Styling.AccentRoseSoft;
        if (TradeReady(cfg, count)) return Styling.AccentAmberSoft;
        return Styling.TextSecondary;
    }

    private static void ShowTooltip(Configuration cfg, int count)
    {
        using (Tooltip.Begin())
        {
            Tooltip.Text(Loc.T(L.Shell.Wallet, Label(count), AfgConstants.BicolorCap.ToString("N0", Loc.Culture)), Styling.TextStrong);
            if (Capped(count))
            {
                Tooltip.Text(Loc.T(L.Shell.WalletCapped), Styling.AccentRoseSoft);
                return;
            }

            if (TradeReady(cfg, count))
            {
                Tooltip.Text(Loc.T(L.Shell.WalletTradeReady, cfg.TradeThreshold.ToString("N0", Loc.Culture)), Styling.AccentAmberSoft);
            }
        }
    }
}
