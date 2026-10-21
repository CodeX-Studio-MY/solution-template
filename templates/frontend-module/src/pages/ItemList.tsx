import { useState, type FormEvent } from "react";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Delete from "@mui/icons-material/Delete";
import { useTranslation } from "react-i18next";
import {
  ApiError,
  masterKey,
  unwrap,
  unwrapEmpty,
  useApiMutation,
  useApiQuery,
  useDialog,
} from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { client } from "../client";
import type { ItemDto } from "../types";

/** Everything that shows items: this list and any `useMaster("__KEY__", ...)` picker elsewhere in the app. */
const itemQueries = [["__KEY__", "items"], masterKey("__KEY__")];

/**
 * The module's first page: a list, a form and a delete. It shows the three hooks every screen uses: `useApiQuery` (cached,
 * typed loading), `useApiMutation` (refresh and a snackbar afterwards) and `useDialog` (ask first). Replace it with the module's
 * real screens.
 */
export function Component() {
  const { t } = useTranslation("__KEY__");
  const dialog = useDialog();
  const [name, setName] = useState("");
  const list = useApiQuery(["__KEY__", "items"], (signal) =>
    unwrap(
      client.GET("/api/__KEY__/v1/items", {
        params: { query: { PageSize: 50 } },
        signal,
      }),
    ),
  );
  const add = useApiMutation(
    () => unwrap(client.POST("/api/__KEY__/v1/items", { body: { name } })),
    {
      invalidate: itemQueries,
      success: t("form.added"),
      onSuccess: () => setName(""),
    },
  );
  const remove = useApiMutation(
    (item: ItemDto) =>
      unwrapEmpty(
        client.DELETE("/api/__KEY__/v1/items/{id}", {
          params: { path: { id: item.id }, header: { "If-Match": item.eTag } },
        }),
      ),
    { invalidate: itemQueries, success: t("list.deleted") },
  );

  async function confirmRemove(item: ItemDto) {
    const confirmed = await dialog.confirm({
      title: t("list.deleteTitle", { name: item.name }),
      text: t("list.deleteText"),
      confirmLabel: t("common:actions.delete"),
      tone: "danger",
    });
    if (confirmed) remove.mutate(item);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    add.mutate();
  }

  // Field errors show next to the field; anything else was already shown as a snackbar.
  const nameError =
    add.error instanceof ApiError
      ? add.error.forField("name")?.message
      : undefined;
  const items = list.data?.items ?? [];
  return (
    <>
      <PageHeader title={t("list.title")} />
      <ProblemAlert error={list.error ?? undefined} />
      {items.length === 0 && !list.isLoading ? (
        <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ mb: 3 }}>
          <Table size="small" aria-label={t("list.title")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("fields.name")}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{item.name}</TableCell>
                  <TableCell align="right">
                    <IconButton
                      aria-label={t("list.delete", { name: item.name })}
                      onClick={() => void confirmRemove(item)}
                    >
                      <Delete />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
      <form onSubmit={submit} noValidate>
        <Stack
          direction={{ xs: "column", sm: "row" }}
          spacing={2}
          sx={{ maxWidth: 640, alignItems: { sm: "flex-start" } }}
        >
          <TextField
            label={t("fields.name")}
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError !== undefined}
            helperText={nameError}
          />
          <Button type="submit" variant="contained" disabled={add.isPending}>
            {t("form.add")}
          </Button>
        </Stack>
      </form>
    </>
  );
}
