using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadAI.Ui;

namespace AutocadAI;

public sealed class Commands : IExtensionApplication
{
    public void Initialize()
    {
        // Nothing heavy here: AutoCAD loads fast.
    }

    public void Terminate()
    {
    }

    [CommandMethod("AIWORKSPACE")]
    public void EnsureWorkspace()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null || ed == null)
            return;

        var paths = ProjectPaths.FromDocument(doc);
        paths.EnsureFolders();

        ed.WriteMessage("\n[AutoCAD AI] Workspace racine: " + paths.WorkspaceRoot);
        ed.WriteMessage("\n[AutoCAD AI] Config projet: " + paths.ProjectConfigPath);
        ed.WriteMessage("\n[AutoCAD AI] LISP généré: " + paths.GeneratedLispDir);
        ed.WriteMessage("\n[AutoCAD AI] Logs: " + paths.LogsDir);
    }

    [CommandMethod("AIOPENWS")]
    public void OpenWorkspace()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null || ed == null)
            return;

        var paths = ProjectPaths.FromDocument(doc);
        paths.EnsureFolders();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = paths.WorkspaceRoot,
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[AutoCAD AI] Impossible d’ouvrir le dossier: " + ex.Message);
        }
    }

    [CommandMethod("AICHAT")]
    public void OpenChat()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null || ed == null)
            return;

        var paths = ProjectPaths.FromDocument(doc);
        paths.EnsureFolders();

        var settings = ProjectSettings.LoadOrCreate(paths.ProjectConfigPath);
        // Create the config file if missing, so the user sees where it lives.
        settings.Save(paths.ProjectConfigPath);

        PaletteHost.Show(paths.WorkspaceRoot, paths.ProjectConfigPath, settings);
        ed.WriteMessage("\n[AutoCAD AI] Palette ouverte. Workspace: " + paths.WorkspaceRoot);
    }

    [CommandMethod("AICONFIG")]
    public void ShowOrInitConfig()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null || ed == null)
            return;

        var paths = ProjectPaths.FromDocument(doc);
        paths.EnsureFolders();

        var settings = ProjectSettings.LoadOrCreate(paths.ProjectConfigPath);
        settings.Save(paths.ProjectConfigPath);

        ed.WriteMessage("\n[AutoCAD AI] Config: " + paths.ProjectConfigPath);
        ed.WriteMessage("\n" + settings.ToPrettyJson());
    }
}

