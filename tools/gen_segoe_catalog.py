import re
import pathlib

root = pathlib.Path(__file__).resolve().parents[1]
md = (root / "docs" / "segoe-fluent-icons.md").read_text(encoding="utf-8")
pat = re.compile(r"^\| ([A-Za-z0-9_]+) \| U\+([0-9A-Fa-f]+) \|")
items = []
for line in md.splitlines():
    m = pat.match(line)
    if m:
        items.append((m.group(1), int(m.group(2), 16)))

out = root / "src" / "Qolome.Devtoy.App" / "Helpers" / "SegoeFluentIconCatalog.cs"
lines = [
    "namespace Qolome_Devtoy_App.Helpers;",
    "",
    "/// <summary>Segoe Fluent Icons 公开列表（与 docs/segoe-fluent-icons.md 同步）。</summary>",
    "public static class SegoeFluentIconCatalog",
    "{",
    "    public sealed record Entry(string Name, int CodePoint)",
    "    {",
    "        public string Glyph => char.ConvertFromUtf32(CodePoint);",
    '        public string Unicode => $"U+{CodePoint:X4}";',
    '        public string CsharpEscape => $"\\\\u{CodePoint:X4}";',
    '        public string XamlEntity => $"&#x{CodePoint:X4};";',
    "    }",
    "",
    "    public static IReadOnlyList<Entry> All { get; } =",
    "    [",
]
for name, cp in items:
    lines.append('        new("%s", 0x%04X),' % (name, cp))
lines.extend(["    ];", "}"])
out.write_text("\n".join(lines) + "\n", encoding="utf-8")
print("wrote %s count=%d" % (out, len(items)))
