import { useState, type FormEvent } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useResource } from "@acme/web-core";
import { EmptyState, MoneyText, PageHeader, ProblemAlert } from "@acme/ui-kit";
import { budgets } from "../client";
import type { BudgetDto } from "../types";

/** Finance sets a budget per cost center and sees how much each has used. */
export function Component() {
  const { t } = useTranslation("cost-center-budget");
  const list = useResource(() => unwrap(budgets.GET("/api/cost-center-budget/v1/budgets")), []);
  const [editing, setEditing] = useState<{ code: string; name: string; amount: string; eTag?: string }>({ code: "", name: "", amount: "" });
  const [error, setError] = useState<Error | undefined>(undefined);
  const [saving, setSaving] = useState(false);
  const apiError = error instanceof ApiError ? error : undefined;

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(undefined);
    try {
      await unwrap(
        budgets.PUT("/api/cost-center-budget/v1/budgets/{code}", {
          params: { path: { code: editing.code }, header: editing.eTag ? { "If-Match": editing.eTag } : {} },
          body: { name: editing.name, amount: Number(editing.amount) },
        }),
      );
      setEditing({ code: "", name: "", amount: "" });
      await list.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setSaving(false);
    }
  }

  const rows: BudgetDto[] = list.data ?? [];
  return (
    <>
      <PageHeader title={t("list.title")} subtitle={t("list.subtitle")} />
      <ProblemAlert error={list.error ?? (apiError && apiError.fieldErrors.length === 0 ? apiError : undefined)} />
      {rows.length === 0 && !list.loading ? (
        <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ mb: 3 }}>
          <TableContainer>
            <Table size="small" aria-label={t("list.title")}>
              <TableHead>
                <TableRow>
                  <TableCell>{t("fields.code")}</TableCell>
                  <TableCell>{t("fields.name")}</TableCell>
                  <TableCell align="right">{t("fields.amount")}</TableCell>
                  <TableCell align="right">{t("fields.used")}</TableCell>
                  <TableCell align="right">{t("fields.remaining")}</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.code}>
                    <TableCell>{row.code}</TableCell>
                    <TableCell>{row.name}</TableCell>
                    <TableCell align="right">
                      <MoneyText amount={row.amount} currency={row.currency} />
                    </TableCell>
                    <TableCell align="right">
                      <MoneyText amount={row.used} currency={row.currency} />
                    </TableCell>
                    <TableCell align="right">
                      <MoneyText amount={row.remaining} currency={row.currency} />
                    </TableCell>
                    <TableCell align="right">
                      <Button
                        size="small"
                        onClick={() => setEditing({ code: row.code, name: row.name, amount: String(row.amount), eTag: row.eTag })}
                      >
                        {t("list.edit")}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Paper>
      )}

      <form onSubmit={save} noValidate>
        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
          {editing.eTag ? t("form.editTitle", { code: editing.code }) : t("form.newTitle")}
        </Typography>
        <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "flex-start" }, maxWidth: 800 }}>
          <TextField
            label={t("fields.code")}
            required
            value={editing.code}
            disabled={editing.eTag !== undefined}
            onChange={(e) => setEditing({ ...editing, code: e.target.value })}
            error={apiError?.forField("code") !== undefined}
            helperText={apiError?.forField("code")?.message}
          />
          <TextField
            label={t("fields.name")}
            required
            value={editing.name}
            onChange={(e) => setEditing({ ...editing, name: e.target.value })}
            error={apiError?.forField("name") !== undefined}
            helperText={apiError?.forField("name")?.message}
          />
          <TextField
            label={t("fields.amount")}
            required
            type="number"
            value={editing.amount}
            onChange={(e) => setEditing({ ...editing, amount: e.target.value })}
            error={apiError?.forField("amount") !== undefined}
            helperText={apiError?.forField("amount")?.message}
          />
          <Button type="submit" variant="contained" disabled={saving}>
            {t("form.save")}
          </Button>
          {editing.eTag ? (
            <Button onClick={() => setEditing({ code: "", name: "", amount: "" })} disabled={saving}>
              {t("form.cancel")}
            </Button>
          ) : null}
        </Stack>
      </form>
    </>
  );
}
