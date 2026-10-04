using AutoFateGrind.Core.Multibox;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections.Config;

// Fork (README-FORK item 16): the Multibox tab, like AutoDuty's multibox list: this client's role, the leader, and every
// client of this PC with its zone, instance, FATE and whether it is following or blocked (and why). Literal English like
// the other fork rows.
internal static class MultiboxSettings
{
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Amber = new(1f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.55f, 0.55f, 0.55f, 1f);

    public static void Draw(Configuration cfg)
    {
        using (SettingsGroup.Begin("This client"))
        {
            SettingsRow.Draw("Role",
                "Leader: publishes the zone and FATE it grinds for the other clients of this PC. Follower: goes to the leader's zone and takes the leader's FATE when it is in the same world and instance (otherwise picks as usual). The slaves are set to Follower by the XIVProfiles override; leave the main on Leader.",
                SettingsControls.RowComboWidth,
                () =>
                {
                    var role = (int)cfg.MultiboxRole;
                    if (SettingsControls.DrawPlainCombo("##mb_role", ref role, ["Leader", "Follower"], SettingsControls.RowComboWidth))
                    {
                        cfg.MultiboxRole = (MultiboxRole)role;
                        cfg.Save();
                    }
                });
        }

        var clients = MultiboxLink.Clients();
        using (SettingsGroup.Begin("Leader"))
        {
            var leaders = clients.Where(c => c.Role == MultiboxRole.Leader && DateTime.UtcNow - c.UpdatedUtc <= MultiboxLink.ClientFresh).ToList();
            if (leaders.Count == 0)
            {
                ImGui.TextColored(Amber, "No leader connected. Followers pick their FATEs on their own until one runs.");
            }
            else
            {
                foreach (var leader in leaders)
                {
                    ImGui.TextColored(leader.Running ? Green : Grey, $"{leader.Name} @ {leader.World}");
                    ImGui.SameLine();
                    ImGui.TextDisabled(leader.Running ? $"{leader.Zone} (instance {leader.Instance}) · {(leader.FateId == 0 ? "between FATEs" : leader.Fate)}" : "connected, not running");
                }
                if (leaders.Count > 1) ImGui.TextColored(Red, "More than one leader is connected: followers read whichever wrote last. Set the others to Follower.");
            }
        }

        using (SettingsGroup.Begin("Clients"))
        {
            if (clients.Count == 0)
            {
                ImGui.TextDisabled("No client has reported yet (AFG writes a card every 2 s while logged in).");
                return;
            }

            var scale = ImGuiHelpers.GlobalScale;
            using var table = ImRaii.Table("##mb_clients", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
            if (!table) return;
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("FATE", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 60f * scale);
            ImGui.TableHeadersRow();

            foreach (var client in clients)
            {
                var age = DateTime.UtcNow - client.UpdatedUtc;
                var connected = age <= MultiboxLink.ClientFresh;
                var status = !connected ? "disconnected" : client.Status;
                var color = !connected ? Grey
                          : status.StartsWith("blocked", StringComparison.Ordinal) ? Red
                          : status.StartsWith("following", StringComparison.Ordinal) || status == "leading" ? Green
                          : Amber;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(connected ? new Vector4(1, 1, 1, 1) : Grey, $"{client.Name} @ {client.World}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(client.Role.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{client.Zone} · {client.Instance}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(client.FateId == 0 ? "-" : client.Fate);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, status);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(age.TotalSeconds < 60 ? $"{age.TotalSeconds:F0}s" : $"{age.TotalMinutes:F0}m");
            }
        }
    }
}
