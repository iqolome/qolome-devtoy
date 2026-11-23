namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Guava CaseFormat 的常用转换。</summary>
public static class CaseFormats
{
    public static string LowerHyphenToUpperCamel(string input) =>
        ToUpperCamel(Split(input, '-'));

    /// <summary>字典 item_code → UpperCamel（同时支持 <c>_</c> / <c>-</c> 分隔）。</summary>
    public static string DictCodeToUpperCamel(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var parts = input.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ToUpperCamel(parts);
    }

    public static string LowerUnderscoreToLowerHyphen(string input) =>
        string.Join('-', Split(input, '_').Select(s => s.ToLowerInvariant()));

    public static string UpperUnderscoreToUpperCamel(string input) =>
        ToUpperCamel(Split(input, '_'));

    public static string LowerCamelToUpperCamel(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return char.ToUpperInvariant(input[0]) + input[1..];
    }

    public static string UpperCamelToLowerCamel(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return char.ToLowerInvariant(input[0]) + input[1..];
    }

    public static string LowerUnderscoreToUpperCamel(string input) =>
        ToUpperCamel(Split(input, '_'));

    public static string UpperCamelToLowerHyphen(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var chars = new List<char>();
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c) && i > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static string ToUpperCamel(IEnumerable<string> parts) =>
        string.Concat(parts.Where(p => p.Length > 0).Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

    private static string[] Split(string input, char sep)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<string>();
        }

        return input.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
