using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AutoFateGrind.Windows.Sections.Config;

// Fork: Settings → Travel → "Ocean fishing". Literal English like the other fork rows, so no localisation files change.
internal static class OceanTripSettings
{
    private const float CommandWidth = 220f;

    private static readonly int[] EveryHoursValues = [2, 4, 6, 8, 12, 24];
    private static readonly string[] EveryHoursLabels = ["Every voyage (2 h)", "Every 4 h", "Every 6 h", "Every 8 h", "Every 12 h", "Every 24 h"];

    private static readonly string[] AddLabels = ["Add a step…", "Sell (AutoRetainer)", "Discard (AutoRetainer)", "Retainers (AutoRetainer)", "Dagobert pinch", "Chat command"];

    public static void Draw(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Ocean fishing");

        SettingsRow.Draw("Go ocean fishing",
            "A few minutes before a voyage's registration opens, the run finishes its FATE, runs the before-boat steps, boards with Henchman's On A Boat (set it up there: character, Fisher gear set), runs the after-boat steps, re-equips its gear set and resumes. Needs Henchman; AutoRetainer for Sell/Discard/Retainers; the local Dagobert fork for the pinch. Leave Henchman's own sell/discard after a voyage off.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.OceanTripEnabled, v => cfg.OceanTripEnabled = v, "##ocean_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##ocean_body", cfg.OceanTripEnabled);
        if (!cfg.OceanTripEnabled)
        {
            return;
        }

        SettingsRow.Draw("How often",
            "Registration opens every 2 hours (even UTC hours). Longer intervals only take the voyages on matching hours.",
            SettingsControls.RowComboWidth,
            () =>
            {
                var selected = Math.Max(0, Array.IndexOf(EveryHoursValues, cfg.OceanTripEveryHours));
                if (SettingsControls.DrawPlainCombo("##ocean_every", ref selected, EveryHoursLabels, SettingsControls.RowComboWidth))
                {
                    cfg.OceanTripEveryHours = EveryHoursValues[selected];
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.DrawBlock("Before the boat",
            "Runs in order before boarding. Sell happens at the merchant at the Limsa voyage desk; the bell steps use the Limsa Lower Decks summoning bell.",
            () => DrawSteps(cfg, OceanTripSteps.Before(cfg), "before"));

        SettingsRow.DrawBlock("After the boat",
            "Runs in order once the character is back on land, before the run resumes.",
            () => DrawSteps(cfg, OceanTripSteps.After(cfg), "after"));
    }

    private static void DrawSteps(Configuration cfg, List<OceanTripStep> steps, string id)
    {
        var moveFrom = -1;
        var moveTo = -1;
        var remove = -1;

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var enabled = step.Enabled;
            if (ToggleSwitch.Draw($"##ocean_{id}_on_{index}", ref enabled))
            {
                step.Enabled = enabled;
                cfg.SaveDebounced();
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, step.Enabled ? Styling.TextStrong : Styling.TextDim))
                ImGui.TextUnformatted($"{index + 1}. {Label(step.Kind)}");

            if (step.Kind == OceanTripStepKind.Command)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(CommandWidth);
                var command = step.Command;
                if (ImGui.InputTextWithHint($"##ocean_{id}_cmd_{index}", "/command", ref command, 200))
                {
                    step.Command = command;
                    cfg.SaveDebounced();
                }
            }

            ImGui.SameLine();
            if (index > 0 && ImGui.SmallButton($"Up##ocean_{id}_up_{index}")) { moveFrom = index; moveTo = index - 1; }
            if (index < steps.Count - 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Down##ocean_{id}_down_{index}")) { moveFrom = index; moveTo = index + 1; }
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##ocean_{id}_rm_{index}")) remove = index;
        }

        if (moveFrom >= 0)
        {
            (steps[moveFrom], steps[moveTo]) = (steps[moveTo], steps[moveFrom]);
            cfg.SaveDebounced();
        }
        else if (remove >= 0)
        {
            steps.RemoveAt(remove);
            cfg.SaveDebounced();
        }

        var add = 0;
        if (SettingsControls.DrawPlainCombo($"##ocean_{id}_add", ref add, AddLabels, SettingsControls.RowComboWidth) && add > 0)
        {
            steps.Add(new OceanTripStep { Kind = (OceanTripStepKind)(add - 1) });
            cfg.SaveDebounced();
        }
    }

    private static string Label(OceanTripStepKind kind) => kind switch
    {
        OceanTripStepKind.Sell      => "Sell (AutoRetainer /ays itemsell)",
        OceanTripStepKind.Discard   => "Discard (AutoRetainer /ays discard)",
        OceanTripStepKind.Retainers => "Retainers (AutoRetainer at the bell)",
        OceanTripStepKind.Dagobert  => "Dagobert pinch (all retainers)",
        OceanTripStepKind.Command   => "Chat command",
        _                           => kind.ToString(),
    };
}
