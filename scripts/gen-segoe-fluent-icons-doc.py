#!/usr/bin/env python3
"""Parse Microsoft Learn Segoe Fluent Icons dump -> docs/segoe-fluent-icons.md"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SRC = ROOT / "docs" / "segoe-fluent-icons.source.txt"
OUT = ROOT / "docs" / "segoe-fluent-icons.md"

ROW = re.compile(r"^\|\s*\|\s*([0-9a-fA-F]{4})\s*\|\s*(.+?)\s*\|\s*$", re.M)

SHORTCUTS = [
    ("查找/查询", "Search"),
    ("筛选", "Filter"),
    ("排序", "Sort"),
    ("升序箭头", "ChevronUp"),
    ("降序箭头", "ChevronDown"),
    ("添加", "Add"),
    ("删除", "Delete"),
    ("编辑", "Edit"),
    ("保存", "Save"),
    ("设置", "Settings"),
    ("刷新", "Refresh"),
    ("返回", "Back"),
    ("接受/确定", "Accept"),
    ("取消", "Cancel"),
    ("播放/生成", "Play"),
]


def parse_entries(text: str) -> list[tuple[str, str]]:
    idx = text.find("## 图标列表")
    body = text[idx:] if idx >= 0 else text
    seen: dict[str, str] = {}
    for m in ROW.finditer(body):
        code = m.group(1).upper()
        name = m.group(2).strip()
        if name in ("说明", "---") or set(name) <= {"-"}:
            continue
        seen.setdefault(code, name)
    return sorted(seen.items(), key=lambda x: x[0])


def resolve_code(name_to_code: dict[str, str], name: str) -> tuple[str, str] | None:
    if name in name_to_code:
        return name, name_to_code[name]
    lower = name.lower()
    for n, c in name_to_code.items():
        if n.lower() == lower:
            return n, c
    return None


def build_markdown(entries: list[tuple[str, str]]) -> str:
    name_to_code = {n: c for c, n in entries}
    lines: list[str] = []
    a = lines.append
    a("# Segoe Fluent Icons 离线参阅")
    a("")
    a("> 整理自 Microsoft Learn《Segoe Fluent 图标字体》（约字体版本 1.56），供本仓库离线查阅。")
    a(">")
    a("> 在线原文：[Segoe Fluent Icons](https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font)")
    a(">")
    a(r"> 本机字体：`%WINDIR%\Fonts\SegoeIcons.ttf`（字体族名 `Segoe Fluent Icons`）")
    a("")
    a("## 在本项目中的用法")
    a("")
    a("- **Symbol 枚举内已有**：`SymbolIcon` / `DevtoyButtons.BuildContent(label, Symbol.Xxx)`")
    a('- **枚举外字形**：`FontIcon` + Unicode，或 `DevtoyButtons.BuildContentWithGlyph(label, "\\uXXXX")`')
    a("- 推荐字号：16 / 20 / 24 / 32 / 40 / 48 / 64")
    a("")
    a("```csharp")
    a("// Symbol")
    a('DevtoyButtons.BuildContent("查询", Symbol.Find);')
    a("")
    a("// FontIcon 字形（示例：ChevronDown）")
    a('DevtoyButtons.BuildContentWithGlyph("编码", "\\uE70D");')
    a("```")
    a("")
    a("```xaml")
    a('<FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE70D;" />')
    a("```")
    a("")
    a("## 常用快捷检索")
    a("")
    a("| 场景 | 名称 | Unicode | C# |")
    a("| --- | --- | --- | --- |")
    for scene, name in SHORTCUTS:
        hit = resolve_code(name_to_code, name)
        if not hit:
            continue
        resolved_name, code = hit
        a(f"| {scene} | {resolved_name} | U+{code} | `\\u{code}` |")
    a("")
    a("## 完整图标列表")
    a("")
    a(f"共 **{len(entries)}** 个（文档公开列表，码位 ≥ U+E700；不含文中注明的低码位字符）。按 Unicode 排序。")
    a("")
    a("| 名称 | Unicode | C# 转义 | XAML 实体 |")
    a("| --- | --- | --- | --- |")
    for code, name in entries:
        a(f"| {name} | U+{code} | `\\u{code}` | `&#x{code};` |")
    a("")
    a("---")
    a("")
    a("*由 Learn 文档表解析生成；字形预览请在本机用 Segoe Fluent Icons 字体查看。*")
    a("")
    return "\n".join(lines)


def main() -> int:
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SRC
    if not src.is_file():
        print(f"source not found: {src}", file=sys.stderr)
        return 1
    entries = parse_entries(src.read_text(encoding="utf-8"))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(build_markdown(entries), encoding="utf-8")
    print(f"count={len(entries)}")
    print(f"wrote={OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
