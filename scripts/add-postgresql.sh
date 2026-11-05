#!/usr/bin/env sh
# PostgreSQL is not supported in this release (see docs/adr/0011-sql-server-and-postgresql.md). This script only says so.
echo "PostgreSQL is not supported in this release: its migrations are not part of it (see docs/adr/0011-sql-server-and-postgresql.md). SQL Server is the supported database." >&2
exit 2
