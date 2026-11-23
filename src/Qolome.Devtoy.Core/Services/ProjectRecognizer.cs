namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

/// <summary>识别指定工程根目录类型与版本。</summary>
public static class ProjectRecognizer
{
    public static ProjectRecognition Recognize(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return ProjectRecognition.NotFound(projectRoot);
        }

        var types = ProjectTypes.Infer(projectRoot);
        var auto = types.Java || types.Web || types.Flutter;
        var version = auto ? ModuleVersionReader.Read(projectRoot, types) : "";
        var hints = new List<string>();
        if (types.Java)
        {
            if (File.Exists(Path.Combine(projectRoot, "pom.xml")))
            {
                hints.Add("Maven");
            }
            else if (File.Exists(Path.Combine(projectRoot, "build.gradle"))
                || File.Exists(Path.Combine(projectRoot, "build.gradle.kts")))
            {
                hints.Add("Gradle");
            }
            else
            {
                hints.Add("Java");
            }
        }

        if (types.Web)
        {
            hints.Add("Web (package.json)");
        }

        if (types.Flutter)
        {
            hints.Add("Flutter");
        }

        return new ProjectRecognition
        {
            ProjectRoot = projectRoot,
            AutoRecognized = auto,
            Types = types,
            Version = version,
            KindHint = hints.Count > 0 ? string.Join(" + ", hints) : "未识别",
        };
    }
}

public sealed class ProjectRecognition
{
    public required string ProjectRoot { get; init; }

    public bool AutoRecognized { get; init; }

    public ProjectTypes Types { get; init; } = new();

    public string Version { get; init; } = "";

    public string KindHint { get; init; } = "";

    public static ProjectRecognition NotFound(string path) => new()
    {
        ProjectRoot = path,
        AutoRecognized = false,
        KindHint = "目录不存在",
    };
}