using System;
using Autodesk.AutoCAD.Windows;

namespace AutocadAI.Ui;

public static class PaletteHost
{
    private static PaletteSet? _paletteSet;
    private static AiPaletteViewModel? _vm;

    public static void Show(string workspaceRoot, string projectConfigPath, ProjectSettings settings)
    {
        if (_paletteSet == null)
        {
            _vm = new AiPaletteViewModel(_ => { });

            var view = new AiPalette
            {
                DataContext = _vm
            };

            _paletteSet = new PaletteSet("AutoCAD AI")
            {
                Style =
                    PaletteSetStyles.NameEditable |
                    PaletteSetStyles.ShowAutoHideButton |
                    PaletteSetStyles.ShowCloseButton |
                    PaletteSetStyles.ShowPropertiesMenu
            };

            _paletteSet.AddVisual("Chat", view);
            _paletteSet.MinimumSize = new System.Drawing.Size(360, 260);
            _paletteSet.Size = new System.Drawing.Size(420, 620);
        }

        if (_vm != null)
        {
            _vm.WorkspaceRoot = workspaceRoot;
            _vm.ProjectConfigPath = projectConfigPath;
            _vm.PolicyLevel = settings.PolicyLevel;
            _vm.PreferredEngine = settings.PreferredEngine;
            _vm.LocalEndpoint = settings.LocalEndpoint;
            _vm.LocalModel = settings.LocalModel;
            _vm.CloudProvider = settings.CloudProvider;
            _vm.EnableAiLogs = settings.EnableAiLogs;
            _vm.EnableWebSearch = settings.EnableWebSearch;
            _vm.InteractionMode = settings.InteractionMode;
        }

        _paletteSet.Visible = true;
    }

    public static void AppendFromCommand(string line)
    {
        _vm?.AppendLine(line);
    }
}

