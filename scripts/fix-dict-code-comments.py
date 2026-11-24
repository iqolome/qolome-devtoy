#!/usr/bin/env python3
"""将 qolome_epsims_dev 中字段注释 <dict-code> 内的下划线改为连字符。"""

from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path

import pymysql

DICT_CODE_RE = re.compile(r"(<dict-code>)([^<]*)(</dict-code>)", re.IGNORECASE)
SETTINGS = Path(os.environ.get("LOCALAPPDATA", "")) / "Qolome" / "Devtoy" / "settings.json"
NUMERIC_TYPES = {
    "tinyint",
    "smallint",
    "mediumint",
    "int",
    "integer",
    "bigint",
    "decimal",
    "numeric",
    "float",
    "double",
    "bit",
}
NO_DEFAULT_NULL_TYPES = {"text", "blob", "json", "geometry"}


def load_ds() -> dict:
    raw = json.loads(SETTINGS.read_text(encoding="utf-8"))
    ds = raw.get("dataSource") or raw.get("DataSource") or {}
    return {
        "host": ds.get("host") or ds.get("Host"),
        "port": int(ds.get("port") or ds.get("Port") or 3306),
        "user": ds.get("username") or ds.get("Username") or "root",
        "password": ds.get("password") or ds.get("Password") or "",
        "database": "qolome_epsims_dev",
        "charset": "utf8mb4",
    }


def fix_comment(comment: str) -> str:
    def repl(m: re.Match[str]) -> str:
        return m.group(1) + m.group(2).replace("_", "-") + m.group(3)

    return DICT_CODE_RE.sub(repl, comment)


def quote_default(value: str | None, data_type: str) -> str | None:
    if value is None:
        return None
    dt = (data_type or "").lower()
    upper = value.upper()
    if upper in {"CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP()", "NOW()"}:
        return value
    if dt in NUMERIC_TYPES:
        return value
    escaped = value.replace("\\", "\\\\").replace("'", "''")
    return f"'{escaped}'"


def append_charset(parts: list[str], row: dict) -> None:
    data_type = (row["DATA_TYPE"] or "").lower()
    if not any(x in data_type for x in ("char", "text", "enum", "set")):
        return
    charset = row.get("CHARACTER_SET_NAME")
    collation = row.get("COLLATION_NAME")
    if charset and collation:
        parts.append(f"CHARACTER SET {charset} COLLATE {collation}")


def append_nullability(parts: list[str], row: dict) -> None:
    parts.append("NOT NULL" if row["IS_NULLABLE"] == "NO" else "NULL")


def append_default(parts: list[str], row: dict, extra: str) -> None:
    if "auto_increment" in extra.lower():
        return
    default = quote_default(row["COLUMN_DEFAULT"], row["DATA_TYPE"])
    if default is not None:
        parts.append(f"DEFAULT {default}")
        return
    if row["IS_NULLABLE"] != "YES":
        return
    if (row["DATA_TYPE"] or "").lower() in NO_DEFAULT_NULL_TYPES:
        return
    parts.append("DEFAULT NULL")


def append_extra(parts: list[str], extra: str) -> None:
    if not extra:
        return
    cleaned = re.sub(r"DEFAULT_GENERATED", "", extra, flags=re.I).strip()
    if cleaned:
        parts.append(cleaned)


def build_modify_sql(row: dict, new_comment: str) -> str:
    parts = [
        f"ALTER TABLE `{row['TABLE_NAME']}` MODIFY COLUMN `{row['COLUMN_NAME']}` {row['COLUMN_TYPE']}"
    ]
    extra = (row["EXTRA"] or "").strip()
    append_charset(parts, row)
    append_nullability(parts, row)
    append_default(parts, row, extra)
    append_extra(parts, extra)
    comment_escaped = new_comment.replace("\\", "\\\\").replace("'", "''")
    parts.append(f"COMMENT '{comment_escaped}'")
    return " ".join(parts)


def collect_changes(cur, database: str) -> list[tuple[dict, str, str, str]]:
    cur.execute(
        """
        SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, COLUMN_TYPE,
               IS_NULLABLE, COLUMN_DEFAULT, EXTRA, COLUMN_COMMENT,
               CHARACTER_SET_NAME, COLLATION_NAME, COLUMN_KEY
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = %s
          AND COLUMN_COMMENT LIKE %s
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """,
        (database, "%<dict-code>%"),
    )
    changes: list[tuple[dict, str, str, str]] = []
    for row in cur.fetchall():
        old = row["COLUMN_COMMENT"] or ""
        new = fix_comment(old)
        if new != old:
            changes.append((row, old, new, build_modify_sql(row, new)))
    return changes


def print_preview(rows_count: int, changes: list[tuple[dict, str, str, str]], ds: dict) -> None:
    print(f"database={ds['database']} host={ds['host']}")
    print(f"dict-code columns={rows_count} need_update={len(changes)}")
    print("--- preview ---")
    for row, old, new, _sql in changes:
        print(f"{row['TABLE_NAME']}.{row['COLUMN_NAME']}")
        print(f"  OLD: {old}")
        print(f"  NEW: {new}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="实际执行 ALTER；默认仅预览")
    args = parser.parse_args()
    ds = load_ds()

    with pymysql.connect(
        host=ds["host"],
        port=ds["port"],
        user=ds["user"],
        password=ds["password"],
        database=ds["database"],
        charset=ds["charset"],
        cursorclass=pymysql.cursors.DictCursor,
        autocommit=False,
    ) as conn:
        with conn.cursor() as cur:
            # 先统计含 dict-code 的列数（含无需修改）
            cur.execute(
                """
                SELECT COUNT(*) AS cnt
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = %s AND COLUMN_COMMENT LIKE %s
                """,
                (ds["database"], "%<dict-code>%"),
            )
            rows_count = int(cur.fetchone()["cnt"])
            changes = collect_changes(cur, ds["database"])
            print_preview(rows_count, changes, ds)

            if not changes:
                print("nothing to update")
                return 0
            if not args.apply:
                print("dry-run only; re-run with --apply to execute")
                return 0

            print("--- apply ---")
            for row, _old, _new, sql in changes:
                cur.execute(sql)
                print(f"OK {row['TABLE_NAME']}.{row['COLUMN_NAME']}")
            conn.commit()
            print(f"committed {len(changes)} column comments")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
