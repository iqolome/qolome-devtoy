using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NVelocity;
using NVelocity.App;
using NVelocity.Runtime;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>从用户模板目录渲染 Velocity（NVelocity + foreach.hasNext 适配）。</summary>
public sealed class VelocityFiles
{
    private static readonly Regex ForeachPattern = new(
        @"#foreach\s*\(\s*\$(\w+)\s+in\s+(\$\{?[\w.]+\}?)\s*\)",
        RegexOptions.Compiled);

    private readonly object _gate = new();
    private VelocityEngine? _engine;
    private string? _templateRoot;

    public void WriteBean(string templateName, string outputFile, object data)
    {
        var text = MergeBean(templateName, data);
        var parent = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(outputFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void WriteMap(string templateName, string outputFile, IDictionary data)
    {
        var text = MergeMap(templateName, data);
        var parent = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(outputFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string MergeBean(string templateName, object data) =>
        MergeMap(templateName, ToDictionary(data));

    public string MergeMap(string templateName, IDictionary data)
    {
        EnsureSeeded();
        var engine = GetEngine();
        var fileName = NormalizeName(templateName);
        var path = Path.Combine(DevtoyPaths.TemplatesRoot, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("找不到模板: " + path);
        }

        var adapted = AdaptForNVelocity(File.ReadAllText(path));
        var context = new VelocityContext();
        context.Put("null", new VmNullTool());
        foreach (DictionaryEntry entry in data)
        {
            context.Put(Convert.ToString(entry.Key), entry.Value);
        }

        using var writer = new StringWriter();
        try
        {
            engine.Evaluate(context, writer, fileName, adapted);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("渲染模板失败: " + fileName, ex);
        }

        return writer.ToString();
    }

    private VelocityEngine GetEngine()
    {
        lock (_gate)
        {
            var root = DevtoyPaths.TemplatesRoot;
            if (_engine is not null && string.Equals(_templateRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return _engine;
            }

            var engine = new VelocityEngine();
            engine.SetProperty(RuntimeConstants.RESOURCE_LOADER, "file");
            engine.SetProperty(RuntimeConstants.FILE_RESOURCE_LOADER_PATH, root);
            engine.SetProperty(RuntimeConstants.INPUT_ENCODING, "UTF-8");
            engine.SetProperty(RuntimeConstants.OUTPUT_ENCODING, "UTF-8");
            engine.SetProperty("runtime.log.logsystem.class", "NVelocity.Runtime.Log.NullLogSystem");
            engine.Init();
            _engine = engine;
            _templateRoot = root;
            return engine;
        }
    }

    private static void EnsureSeeded() => new TemplateStore().EnsureSeeded();

    private static string NormalizeName(string templateName)
    {
        var name = templateName.Replace('\\', '/');
        if (name.StartsWith("templates/", StringComparison.OrdinalIgnoreCase))
        {
            name = name["templates/".Length..];
        }

        if (name.StartsWith('/'))
        {
            name = name[1..];
        }

        return name.EndsWith(".vm", StringComparison.OrdinalIgnoreCase) ? name : name + ".vm";
    }

    /// <summary>
    /// NVelocity 1.x 无 $foreach.hasNext；注入 loopSize 并用 velocityCount 比较。
    /// </summary>
    internal static string AdaptForNVelocity(string template)
    {
        var withLoopSize = ForeachPattern.Replace(
            template,
            m =>
            {
                var collection = m.Groups[2].Value;
                var sizeExpr = collection.StartsWith("${", StringComparison.Ordinal)
                    ? collection[..^1] + ".size()}"
                    : collection + ".size()";
                return "#set($__loopSize=" + sizeExpr + ")" + m.Value;
            });

        return withLoopSize
            .Replace("${foreach.hasNext}", "($velocityCount < $__loopSize)", StringComparison.Ordinal)
            .Replace("$foreach.hasNext", "($velocityCount < $__loopSize)", StringComparison.Ordinal);
    }

    private static Hashtable ToDictionary(object data)
    {
        if (data is Hashtable table)
        {
            return table;
        }

        if (data is IDictionary dict)
        {
            var copy = new Hashtable();
            foreach (DictionaryEntry entry in dict)
            {
                copy[entry.Key!] = entry.Value;
            }

            return copy;
        }

        var result = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in data.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            result[prop.Name] = prop.GetValue(data);
        }

        return result;
    }
}
