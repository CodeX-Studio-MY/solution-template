import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { useFormatters } from "@saaskit/web-core";
import { EmptyState } from "./EmptyState";

/** One line of an audit log, whichever service wrote it. */
export interface ActivityRow {
  id: number | string;
  occurredOn: string;
  kind: string;
  /** Who did it: a name when known, else an ID, else "System". */
  who: string | null;
  onBehalfOf?: string | null;
  /** What it was done to, when that is a named thing. */
  subject?: string | null;
  /** The record's JSON details. */
  details: string | null;
}

interface ActivityTableProps {
  label: string;
  rows: ActivityRow[] | undefined;
  totalCount: number;
  page: number;
  pageSize: number;
  onPage: (page: number, pageSize: number) => void;
  /** Kinds to filter by; omit for no filter. "" is everything. */
  kinds?: readonly string[];
  kind?: string;
  onKind?: (kind: string) => void;
}

/** `{"module":"billing","role":"admin"}` → `module: billing, role: admin`. Anything that isn't a JSON object is shown as it is. */
function describe(details: string | null): string {
  if (!details) return "";
  try {
    const value = JSON.parse(details) as unknown;
    if (value && typeof value === "object" && !Array.isArray(value))
      return Object.entries(value as Record<string, unknown>)
        .map(([k, v]) => `${k}: ${typeof v === "object" ? JSON.stringify(v) : String(v)}`)
        .join(", ");
  } catch {
    // not JSON
  }
  return details;
}

/** An audit log, newest first, a page at a time. Known kinds read as words in the person's language. */
export function ActivityTable({ label, rows, totalCount, page, pageSize, onPage, kinds, kind = "", onKind }: ActivityTableProps) {
  const { t } = useTranslation();
  const format = useFormatters();
  const kindLabel = (value: string) => t(`common:activity.kinds.${value}`, { defaultValue: value });
  return (
    <>
      {kinds && onKind ? (
        <TextField
          select
          size="small"
          label={t("common:activity.kind")}
          value={kind}
          onChange={(e) => onKind(e.target.value)}
          sx={{ maxWidth: 320, mb: 2 }}
        >
          <MenuItem value="">{t("common:activity.all")}</MenuItem>
          {kinds.map((value) => (
            <MenuItem key={value} value={value}>
              {kindLabel(value)}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {rows && rows.length === 0 ? (
        <EmptyState title={t("common:activity.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={label}>
            <TableHead>
              <TableRow>
                <TableCell>{t("common:activity.when")}</TableCell>
                <TableCell>{t("common:activity.what")}</TableCell>
                <TableCell>{t("common:activity.who")}</TableCell>
                <TableCell>{t("common:activity.details")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(rows ?? []).map((row) => (
                <TableRow key={row.id}>
                  <TableCell sx={{ whiteSpace: "nowrap" }}>{format.dateTime(row.occurredOn)}</TableCell>
                  <TableCell>
                    {kindLabel(row.kind)}
                    {row.subject ? (
                      <Typography variant="caption" color="text.secondary" component="div">
                        {row.subject}
                      </Typography>
                    ) : null}
                  </TableCell>
                  <TableCell>
                    {row.who ?? t("common:activity.system")}
                    {row.onBehalfOf ? (
                      <Typography variant="caption" color="text.secondary" component="div">
                        {t("common:activity.onBehalfOf", { name: row.onBehalfOf })}
                      </Typography>
                    ) : null}
                  </TableCell>
                  <TableCell sx={{ wordBreak: "break-word" }}>{describe(row.details)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <TablePagination
            component="div"
            count={totalCount}
            page={page - 1}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[25, 50, 100]}
            onPageChange={(_, next) => onPage(next + 1, pageSize)}
            onRowsPerPageChange={(e) => onPage(1, Number(e.target.value))}
          />
        </Paper>
      )}
    </>
  );
}
