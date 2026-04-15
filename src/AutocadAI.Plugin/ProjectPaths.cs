using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;

namespace AutocadAI;

public sealed class ProjectPaths
{
    public string ProjectRootDir { get; }
    public string WorkspaceRoot { get; }

    public string ProjectConfigPath => Path.Combine(WorkspaceRoot, "project.json");

    public string GeneratedDir => Path.Combine(WorkspaceRoot, "generated");
    public string GeneratedLispDir => Path.Combine(GeneratedDir, "lisp");
    public string GeneratedScriptsDir => Path.Combine(GeneratedDir, "scripts");

    public string LogsDir => Path.Combine(WorkspaceRoot, "logs");
    public string CacheDir => Path.Combine(WorkspaceRoot, "cache");

    private ProjectPaths(string projectRootDir)
    {
        ProjectRootDir = projectRootDir;
        WorkspaceRoot = Path.Combine(ProjectRootDir, ".autocad-ai");
    }

    public static ProjectPaths FromDocument(Document doc)
    {
        // When the drawing is saved, we want the workspace next to the DWG.
        // doc.Name can be just "Drawing1.dwg" for unsaved drawings; Database.Filename is often more reliable.
        var dwgPath1 = doc?.Name ?? "";
        var dwgPath2 = doc?.Database?.Filename ?? "";

        var projectRoot = TryGetDirectoryFromDwgPath(dwgPath1)
                          ?? TryGetDirectoryFromDwgPath(dwgPath2)
                          ?? GetUserFallbackRoot();
        return new ProjectPaths(projectRoot);
    }

    public void EnsureFolders()
    {
        Directory.CreateDirectory(WorkspaceRoot);
        Directory.CreateDirectory(GeneratedDir);
        Directory.CreateDirectory(GeneratedLispDir);
        Directory.CreateDirectory(GeneratedScriptsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(CacheDir);
    }

    private static string? TryGetDirectoryFromDwgPath(string dwgPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dwgPath))
                return null;

            // For unsaved drawings AutoCAD can return just "Drawing1.dwg".
            if (!Path.IsPathRooted(dwgPath))
                return null;

            var dir = Path.GetDirectoryName(dwgPath);
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
        catch
        {
            return null;
        }
    }

    private static string GetUserFallbackRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(docs, "AutoCAD-AI", "workspace");
        return root;
    }
}

