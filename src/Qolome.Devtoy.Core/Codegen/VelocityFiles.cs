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
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

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
            return _engine;
        }
    }

    private static void EnsureSeeded() => TemplateStore.EnsureSeeded();

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
    /// NVelocity 1.x 无 $foreach.hasNext：为每个 foreach 注入独立 loopSize，避免共用变量导致后续循环丢项。
    /// </summary>
    internal static string AdaptForNVelocity(string template)
    {
        var matches = ForeachPattern.Matches(template);
        if (matches.Count == 0)
        {
            return template;
        }

        var sb = new StringBuilder(template.Length + matches.Count * 48);
        var last = 0;
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            sb.Append(template, last, m.Index - last);

            var collection = m.Groups[2].Value;
            var sizeExpr = collection.StartsWith("${", StringComparison.Ordinal)
                ? collection[..^1] + ".size()}"
                : collection + ".size()";
            var sizeVar = "__loopSize" + i;
            sb.Append("#set($").Append(sizeVar).Append('=').Append(sizeExpr).Append(')');
            sb.Append(m.Value);

            var bodyStart = m.Index + m.Length;
            var bodyEnd = FindForeachBodyEnd(template, bodyStart);
            if (bodyEnd < 0)
            {
                last = m.Index + m.Length;
                continue;
            }

            var body = template[bodyStart..bodyEnd];
            body = body
                .Replace("${foreach.hasNext}", "($velocityCount < $" + sizeVar + ")", StringComparison.Ordinal)
                .Replace("$foreach.hasNext", "($velocityCount < $" + sizeVar + ")", StringComparison.Ordinal);
            sb.Append(body);
            last = bodyEnd;
        }

        sb.Append(template, last, template.Length - last);
        return sb.ToString();
    }

    /// <summary>从 foreach 体起点找到与之匹配的 #end 起始下标（不含 #end）。</summary>
    private static int FindForeachBodyEnd(string template, int bodyStart)
    {
        var depth = 1;
        var i = bodyStart;
        while (i < template.Length)
        {
            if (template[i] != '#')
            {
                i++;
                continue;
            }

            if (StartsWithDirective(template, i, "foreach")
                || StartsWithDirective(template, i, "if")
                || StartsWithDirective(template, i, "macro"))
            {
                depth++;
                i++;
                continue;
            }

            if (StartsWithDirective(template, i, "end"))
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }

                i++;
                continue;
            }

            i++;
        }

        return -1;
    }

    private static bool StartsWithDirective(string template, int index, string name)
    {
        if (index + 1 + name.Length > template.Length || template[index] != '#')
        {
            return false;
        }

        if (!string.Equals(template.Substring(index + 1, name.Length), name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var after = index + 1 + name.Length;
        if (after >= template.Length)
        {
            return true;
        }

        var c = template[after];
        return !char.IsLetterOrDigit(c) && c != '_';
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
                if (entry.Key is not null)
                {
                    copy[entry.Key] = entry.Value;
                }
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
