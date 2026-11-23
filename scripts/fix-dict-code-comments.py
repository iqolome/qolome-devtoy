#!/usr/bin/env python3
"""将 qolome_epsims_dev 中字段注释 <dict-code> 内的下划线改为连字符。"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

import pymysql

DICT_CODE_RE = re.compile(r"(<dict-code>)(.*?)(</dict-code>)", re.IGNORECASE | re.DOTALL)
SETTINGS = Path(os.environ.get("LOCALAPPDATA", "")) / "Qolome" / "Devtoy" / "settings.json"


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


def quote_default(value: str | None, data_type: str, column_type: str) -> str | None:
    if value is None:
        return None
    dt = (data_type or "").lower()
    if value.upper() in {"CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP()", "NOW()"}:
        return value
    if dt in {"tinyint", "smallint", "mediumint", "int", "integer", "bigint", "decimal", "numeric", "float", "double", "bit"}:
        return value
    if dt in {"timestamp", "datetime", "date", "time", "year"} and value.upper() in {"CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP()"}:
        return value
    escaped = value.replace("\\", "\\\\").replace("'", "''")
    return f"'{escaped}'"


def build_modify_sql(row: dict, new_comment: str) -> str:
    col = row["COLUMN_NAME"]
    table = row["TABLE_NAME"]
    parts = [f"ALTER TABLE `{table}` MODIFY COLUMN `{col}` {row['COLUMN_TYPE']}"]

    if row.get("CHARACTER_SET_NAME") and row.get("COLLATION_NAME"):
        # 字符串类型：显式保留字符集，避免被会话默认值改掉
        if any(x in (row["DATA_TYPE"] or "").lower() for x in ("char", "text", "enum", "set")):
            parts.append(f"CHARACTER SET {row['CHARACTER_SET_NAME']} COLLATE {row['COLLATION_NAME']}")

    if row["IS_NULLABLE"] == "NO":
        parts.append("NOT NULL")
    else:
        parts.append("NULL")

    default = quote_default(row["COLUMN_DEFAULT"], row["DATA_TYPE"], row["COLUMN_TYPE"])
    extra = (row["EXTRA"] or "").strip()
    # auto_increment / on update 在 EXTRA 中
    if default is not None and "auto_increment" not in extra.lower():
        parts.append(f"DEFAULT {default}")
    elif row["IS_NULLABLE"] == "YES" and row["COLUMN_DEFAULT"] is None and "auto_increment" not in extra.lower():
        # 显式 DEFAULT NULL，避免部分类型隐式变化
        if (row["DATA_TYPE"] or "").lower() not in {"text", "blob", "json", "geometry"}:
            parts.append("DEFAULT NULL")

    if extra:
        # EXTRA 可能含 auto_increment / DEFAULT_GENERATED / on update CURRENT_TIMESTAMP
        # DEFAULT_GENERATED 时 COLUMN_DEFAULT 已处理表达式，跳过重复
        cleaned = extra
        if "DEFAULT_GENERATED" in cleaned.upper():
            cleaned = re.sub(r"DEFAULT_GENERATED", "", cleaned, flags=re.I).strip()
        if cleaned:
            parts.append(cleaned)

    comment_escaped = new_comment.replace("\\", "\\\\").replace("'", "''")
    parts.append(f"COMMENT '{comment_escaped}'")
    return " ".join(parts)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="实际执行 ALTER；默认仅预览")
    args = parser.parse_args()

    ds = load_ds()
    conn = pymysql.connect(
        host=ds["host"],
        port=ds["port"],
        user=ds["user"],
        password=ds["password"],
        database=ds["database"],
        charset=ds["charset"],
        cursorclass=pymysql.cursors.DictCursor,
        autocommit=False,
    )

    try:
        with conn.cursor() as cur:
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
                (ds["database"], "%<dict-code>%"),
            )
            rows = cur.fetchall()

            changes: list[tuple[dict, str, str, str]] = []
            for row in rows:
                old = row["COLUMN_COMMENT"] or ""
                new = fix_comment(old)
                if new != old:
                    sql = build_modify_sql(row, new)
                    changes.append((row, old, new, sql))

            print(f"database={ds['database']} host={ds['host']}")
            print(f"dict-code columns={len(rows)} need_update={len(changes)}")
            print("--- preview ---")
            for row, old, new, _sql in changes:
                print(f"{row['TABLE_NAME']}.{row['COLUMN_NAME']}")
                print(f"  OLD: {old}")
                print(f"  NEW: {new}")

            if not changes:
                print("nothing to update")
                return 0

            if not args.apply:
                print("dry-run only; re-run with --apply to execute")
                return 0

            print("--- apply ---")
            for row, old, new, sql in changes:
                cur.execute(sql)
                print(f"OK {row['TABLE_NAME']}.{row['COLUMN_NAME']}")
            conn.commit()
            print(f"committed {len(changes)} column comments")
            return 0
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


if __name__ == "__main__":
    raise SystemExit(main())
