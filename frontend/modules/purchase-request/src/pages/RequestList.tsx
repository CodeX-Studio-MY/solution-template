import { useState } from "react";
import { Link as RouterLink } from "react-router";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { toNumber, unwrap, useFormatters, useResource } from "@acme/web-core";
import { EmptyState, MoneyText, PageHeader, ProblemAlert } from "@acme/ui-kit";
import { requests } from "../client";
import { StatusBadge } from "../components/StatusBadge";
import type { RequestStatus } from "../types";

const statuses: RequestStatus[] = ["draft", "submitted", "approved", "rejected"];

export function Component() {
  const { t } = useTranslation("purchase-request");
  const format = useFormatters();
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [status, setStatus] = useState<RequestStatus | "">("");
  const [mine, setMine] = useState(false);

  const list = useResource(
    () =>
      unwrap(
        requests.GET("/api/purchase-request/v1/requests", {
          params: { query: { Page: page + 1, PageSize: pageSize, ...(status ? { status } : {}), ...(mine ? { mine: true } : {}) } },
        }),
      ),
    [page, pageSize, status, mine],
  );

  return (
    <>
      <PageHeader
        title={t("list.title")}
        actions={
          <Button component={RouterLink} to="new" variant="contained">
            {t("list.new")}
          </Button>
        }
      />
      <Stack direction="row" spacing={2} sx={{ mb: 2, alignItems: "center" }}>
        <TextField
          select
          label={t("list.status")}
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as RequestStatus | "");
            setPage(0);
          }}
          sx={{ maxWidth: 220 }}
        >
          <MenuItem value="">{t("list.allStatuses")}</MenuItem>
          {statuses.map((s) => (
            <MenuItem key={s} value={s}>
              {t(`status.${s}`)}
            </MenuItem>
          ))}
        </TextField>
        <FormControlLabel
          control={
            <Switch
              checked={mine}
              onChange={(e) => {
                setMine(e.target.checked);
                setPage(0);
              }}
            />
          }
          label={t("list.mine")}
        />
      </Stack>
      <ProblemAlert error={list.error} />
      {list.data && list.data.items.length === 0 ? (
        <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
      ) : (
        <Paper variant="outlined">
          <TableContainer>
            <Table size="small" aria-label={t("list.title")}>
              <TableHead>
                <TableRow>
                  <TableCell>{t("fields.number")}</TableCell>
                  <TableCell>{t("fields.title")}</TableCell>
                  <TableCell>{t("fields.status")}</TableCell>
                  <TableCell>{t("fields.date")}</TableCell>
                  <TableCell align="right">{t("fields.total")}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {(list.data?.items ?? []).map((row) => (
                  <TableRow key={row.id} hover>
                    <TableCell>
                      <Link component={RouterLink} to={row.id}>
                        {row.number}
                      </Link>
                    </TableCell>
                    <TableCell>{row.title}</TableCell>
                    <TableCell>
                      <StatusBadge status={row.status} />
                    </TableCell>
                    <TableCell>{format.date(row.businessDate)}</TableCell>
                    <TableCell align="right">
                      <MoneyText amount={row.total} currency={row.currency} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          <TablePagination
            component="div"
            count={toNumber(list.data?.totalCount ?? 0)}
            page={page}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[10, 20, 50]}
            onPageChange={(_, next) => setPage(next)}
            onRowsPerPageChange={(e) => {
              setPageSize(Number(e.target.value));
              setPage(0);
            }}
          />
        </Paper>
      )}
    </>
  );
}
