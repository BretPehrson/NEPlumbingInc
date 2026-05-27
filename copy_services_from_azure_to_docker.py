#!/usr/bin/env python3
"""Copy Services and SubServices from Azure SQL to local Docker SQL Server.

This variant uses pymssql to avoid ODBC driver setup on macOS.

Required environment variables:
    AZURE_SQL_CONNECTION_STRING
    LOCAL_SQL_CONNECTION_STRING

Example:
    export AZURE_SQL_CONNECTION_STRING='Server=tcp:<azure-server>.database.windows.net,1433;Initial Catalog=<db>;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
    export LOCAL_SQL_CONNECTION_STRING='Server=localhost,1433;Database=NEPlumbingIncDB;User ID=sa;Password=<password>;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;'

Usage:
    python3 copy_services_from_azure_to_docker.py --dry-run
    python3 copy_services_from_azure_to_docker.py

Optional args:
    --include-inactive   Copy all services, not only active ones.
    --dry-run            Show row counts and stop before writing local DB.
"""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass
from typing import Any

import pymssql


@dataclass
class TableData:
    name: str
    columns: list[str]
    rows: list[tuple[Any, ...]]


@dataclass
class SqlConnectionSettings:
    server: str
    port: int
    database: str
    user: str
    password: str
    encrypt: bool


def get_connection_string(env_key: str, fallback: str | None = None) -> str:
    value = os.getenv(env_key, "").strip()
    if value:
        return value
    if fallback:
        return fallback
    raise ValueError(f"Missing required environment variable: {env_key}")


def parse_connection_string(conn_str: str) -> SqlConnectionSettings:
    parts: dict[str, str] = {}
    for segment in conn_str.split(";"):
        if not segment.strip() or "=" not in segment:
            continue
        key, value = segment.split("=", 1)
        parts[key.strip().lower()] = value.strip()

    server_raw = parts.get("server") or parts.get("data source") or ""
    if not server_raw:
        raise ValueError("Connection string is missing Server")

    server_raw = server_raw.removeprefix("tcp:")
    if "," in server_raw:
        host, port_str = server_raw.rsplit(",", 1)
    elif ":" in server_raw and server_raw.count(":") == 1:
        host, port_str = server_raw.rsplit(":", 1)
    else:
        host, port_str = server_raw, "1433"

    database = parts.get("database") or parts.get("initial catalog")
    if not database:
        raise ValueError("Connection string is missing Database/Initial Catalog")

    user = parts.get("uid") or parts.get("user id") or parts.get("user")
    password = parts.get("pwd") or parts.get("password")
    if not user or not password:
        raise ValueError("Connection string is missing User ID/Password")

    encrypt_raw = parts.get("encrypt", "false").lower()
    encrypt = encrypt_raw in {"true", "yes", "1", "mandatory"}

    try:
        port = int(port_str)
    except ValueError as ex:
        raise ValueError(f"Invalid SQL Server port: {port_str}") from ex

    return SqlConnectionSettings(
        server=host,
        port=port,
        database=database,
        user=user,
        password=password,
        encrypt=encrypt,
    )


def connect(conn_str: str) -> pymssql.Connection:
    cfg = parse_connection_string(conn_str)
    return pymssql.connect(
        server=cfg.server,
        port=cfg.port,
        user=cfg.user,
        password=cfg.password,
        database=cfg.database,
        login_timeout=30,
        timeout=30,
        encryption="require" if cfg.encrypt else "off",
    )


def get_non_computed_columns(cursor: Any, table: str) -> list[str]:
    sql = """
    SELECT c.name
    FROM sys.columns c
    INNER JOIN sys.objects o ON c.object_id = o.object_id
    WHERE o.type = 'U'
      AND o.name = %s
      AND c.is_computed = 0
    ORDER BY c.column_id
    """
    cursor.execute(sql, (table,))
    rows = cursor.fetchall()
    if not rows:
        raise RuntimeError(f"Table not found or has no columns: {table}")
    return [r[0] for r in rows]


def fetch_table_data(
    cursor: Any,
    table: str,
    where_clause: str | None = None,
    order_clause: str | None = None,
) -> TableData:
    columns = get_non_computed_columns(cursor, table)
    select_cols = ", ".join(f"[{c}]" for c in columns)

    sql = f"SELECT {select_cols} FROM [{table}]"
    if where_clause:
        sql += f" WHERE {where_clause}"
    if order_clause:
        sql += f" ORDER BY {order_clause}"

    cursor.execute(sql)
    rows = cursor.fetchall()
    return TableData(name=table, columns=columns, rows=[tuple(r) for r in rows])


def execute_non_query(cursor: Any, sql: str) -> None:
    cursor.execute(sql)


def clear_target_tables(cursor: Any) -> None:
    # Child first for FK safety.
    execute_non_query(cursor, "DELETE FROM [SubServices]")
    execute_non_query(cursor, "DELETE FROM [Services]")


def has_identity_column(columns: list[str]) -> bool:
    return "Id" in columns


def bulk_insert(cursor: Any, data: TableData) -> int:
    if not data.rows:
        return 0

    col_list = ", ".join(f"[{c}]" for c in data.columns)
    placeholders = ", ".join("%s" for _ in data.columns)
    insert_sql = f"INSERT INTO [{data.name}] ({col_list}) VALUES ({placeholders})"

    identity_on = has_identity_column(data.columns)
    if identity_on:
        execute_non_query(cursor, f"SET IDENTITY_INSERT [{data.name}] ON")

    cursor.executemany(insert_sql, data.rows)

    if identity_on:
        execute_non_query(cursor, f"SET IDENTITY_INSERT [{data.name}] OFF")

    return len(data.rows)


def main() -> int:
    parser = argparse.ArgumentParser(description="Copy Services/SubServices from Azure SQL to Docker SQL")
    parser.add_argument(
        "--include-inactive",
        action="store_true",
        help="Copy all services. Default copies only active services and related subservices.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Read and print counts but do not write local DB.",
    )
    args = parser.parse_args()

    try:
        azure_conn_str = get_connection_string("AZURE_SQL_CONNECTION_STRING")
        local_conn_str = get_connection_string("LOCAL_SQL_CONNECTION_STRING")
    except ValueError as ex:
        print(str(ex))
        return 1

    print("Connecting to Azure SQL source...")
    try:
        source = connect(azure_conn_str)
    except pymssql.OperationalError as ex:
        error_text = str(ex)
        if "40615" in error_text or "not allowed to access the server" in error_text.lower():
            print("Azure SQL firewall blocked this connection.")
            print("Add your current public IP address to the Azure SQL Server firewall allowlist, then retry.")
            print("Tip: In Azure Portal, open the SQL server, then Networking, then add a firewall rule for your client IP.")
            print("Azure can take a few minutes to apply firewall updates.")
            return 1
        raise

    print("Connecting to local Docker SQL target...")
    target = connect(local_conn_str)

    try:
        source_cursor = source.cursor()
        target_cursor = target.cursor()

        service_where = None if args.include_inactive else "[IsActive] = 1"
        services = fetch_table_data(source_cursor, "Services", where_clause=service_where, order_clause="[Id]")

        service_ids = [r[services.columns.index("Id")] for r in services.rows] if services.rows else []
        if service_ids:
            in_values = ", ".join(str(int(i)) for i in service_ids)
            subservices = fetch_table_data(
                source_cursor,
                "SubServices",
                where_clause=f"[ServiceId] IN ({in_values})",
                order_clause="[Id]",
            )
        else:
            subservices = TableData(
                name="SubServices",
                columns=get_non_computed_columns(source_cursor, "SubServices"),
                rows=[],
            )

        print(f"Source Services rows: {len(services.rows)}")
        print(f"Source SubServices rows: {len(subservices.rows)}")

        if args.dry_run:
            print("Dry run complete. No local changes were made.")
            return 0

        print("Clearing local Services/SubServices...")
        clear_target_tables(target_cursor)

        print("Inserting Services...")
        inserted_services = bulk_insert(target_cursor, services)

        print("Inserting SubServices...")
        inserted_subservices = bulk_insert(target_cursor, subservices)

        target.commit()

        print("Copy complete.")
        print(f"Inserted Services: {inserted_services}")
        print(f"Inserted SubServices: {inserted_subservices}")
        return 0
    except Exception as ex:
        try:
            target.rollback()
        except Exception:
            pass
        print(f"Copy failed: {ex}")
        return 1
    finally:
        source.close()
        target.close()


if __name__ == "__main__":
    sys.exit(main())
