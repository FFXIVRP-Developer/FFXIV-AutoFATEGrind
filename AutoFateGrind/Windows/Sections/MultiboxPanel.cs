using AutoFateGrind.Core.Multibox;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// Fork (README-FORK item 18): the first page's multibox parts. The Leader / Slave switch on top; a slave sees only its
// panel (no goals, zones or limits: it grinds where the leader grinds), the leader keeps its goals and gets the slave list.
internal static class MultiboxPanel
{
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Amber = new(1f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Segmented.Item[] roleItems =
    [
        new(FontAwesomeIcon.Crown, "Leader"),
        new(FontAwesomeIcon.Link, "Slave"),
    ];

    public static void DrawRoleSwitch(Configuration cfg, bool running)
    {
        var role = (int)cfg.MultiboxRole;
        if (Segmented.Draw("##afg_mb_role", roleItems, ref role, enabled: !running))
        {
            cfg.MultiboxRole = (MultiboxRole)role;
            cfg.Save();
        }
        if (running && ImGui.IsItemHovered()) ImGui.SetTooltip("Stop the run to change the role.");
    }

    /// <summary>A slave's whole idle page: who it follows and what it does next.</summary>
    public static void DrawSlave(Configuration cfg)
    {
        var leader = MultiboxLink.Leader();
        ImGui.TextColored(Styling.TextSecondary, "SLAVE");
        ImGui.TextWrapped("This client follows the leader: it starts when the leader starts, grinds in the leader's zone, instance and FATE, and when the leader stops it finishes its FATE and parks. Goals, zones and limits are the leader's.");
        Styling.VSpace(10f);

        if (leader is null)
        {
            ImGui.TextColored(Amber, "Waiting for the leader to start a run.");
        }
        else
        {
            ImGui.TextColored(Green, $"Leader {leader.Name} is grinding");
            ImGui.SameLine();
            ImGui.TextDisabled($"{ZoneName(leader.Territory)} · instance {leader.Instance}");
        }
        if (MultiboxFollowerWatch.Parked) ImGui.TextColored(Grey, "Parked: the leader stopped.");

        Styling.VSpace(6f);
        var park = cfg.HumanizerRetreat switch
        {
            HumanizerRetreat.Inn => "the inn",
            HumanizerRetreat.Apartment => "the apartment",
            HumanizerRetreat.PrivateHouse => "the private house",
            HumanizerRetreat.FreeCompanyHouse => "the FC house",
            _ => "where it is (break location City)",
        };
        ImGui.TextDisabled($"Parks at {park}: Settings → Humanizer, break location.");
    }

    /// <summary>The leader's view of its slaves (every follower card of this PC).</summary>
    public static void DrawSlaveList()
    {
        var slaves = MultiboxLink.Clients().Where(c => c.Role == MultiboxRole.Follower).ToList();
        ImGui.TextColored(Styling.TextSecondary, $"SLAVES ({slaves.Count(c => DateTime.UtcNow - c.UpdatedUtc <= MultiboxLink.ClientFresh)} connected)");
        if (slaves.Count == 0)
        {
            ImGui.TextDisabled("No slave has reported. Slaves are clients with the Slave role, opened on this PC.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        using var table = ImRaii.Table("##afg_mb_slaves", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;
        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 50f * scale);
        ImGui.TableHeadersRow();
        foreach (var slave in slaves)
        {
            var age = DateTime.UtcNow - slave.UpdatedUtc;
            var connected = age <= MultiboxLink.ClientFresh;
            var status = connected ? slave.Status : "disconnected";
            var color = !connected ? Grey
                      : status.StartsWith("blocked", StringComparison.Ordinal) ? Red
                      : status.StartsWith("following", StringComparison.Ordinal) ? Green
                      : Amber;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(connected ? new Vector4(1, 1, 1, 1) : Grey, slave.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{slave.Zone} · {slave.Instance}");
            ImGui.TableNextColumn();
            ImGui.TextColored(color, status);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(age.TotalSeconds < 60 ? $"{age.TotalSeconds:F0}s" : $"{age.TotalMinutes:F0}m");
        }
    }

    private static string ZoneName(uint territory)
        => ECommons.DalamudServices.Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() ?? territory.ToString();
}
