import { useState, type FormEvent } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useResource } from "@farm360/web-core";
import { EmptyState, PageHeader, ProblemAlert } from "@farm360/ui-kit";
import { client } from "../client";

/** The module's first page: a list and a form. Replace it with the module's real screens. */
export function Component() {
  const { t } = useTranslation("master-data");
  const list = useResource(() => unwrap(client.GET("/api/master-data/v1/items", { params: { query: { PageSize: 50 } } })), []);
  const [name, setName] = useState("");
  const [error, setError] = useState<Error | undefined>(undefined);
  const [saving, setSaving] = useState(false);
  const apiError = error instanceof ApiError ? error : undefined;

  async function add(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(undefined);
    try {
      await unwrap(client.POST("/api/master-data/v1/items", { body: { name } }));
      setName("");
      await list.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setSaving(false);
    }
  }

  const items = list.data?.items ?? [];
  return (
    <>
      <PageHeader title={t("list.title")} />
      <ProblemAlert error={list.error ?? (apiError && apiError.fieldErrors.length === 0 ? apiError : undefined)} />
      {items.length === 0 && !list.loading ? (
        <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ mb: 3 }}>
          <Table size="small" aria-label={t("list.title")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("fields.name")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{item.name}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
      <form onSubmit={add} noValidate>
        <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ maxWidth: 640, alignItems: { sm: "flex-start" } }}>
          <TextField
            label={t("fields.name")}
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={apiError?.forField("name") !== undefined}
            helperText={apiError?.forField("name")?.message}
          />
          <Button type="submit" variant="contained" disabled={saving}>
            {t("form.add")}
          </Button>
        </Stack>
      </form>
    </>
  );
}
