import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import { useTranslation } from "react-i18next";
import { unwrap, useResource } from "@farm360/web-core";
import { EmptyState, PageHeader, ProblemAlert } from "@farm360/ui-kit";
import { client } from "../client";

/** The tenant's farms, read-only for now: the screens to add and edit farms come in a later step. */
export function Component() {
  const { t } = useTranslation("master-data");
  const list = useResource(() => unwrap(client.GET("/api/master-data/v1/farms", { params: { query: { PageSize: 50 } } })), []);

  const farms = list.data?.items ?? [];
  return (
    <>
      <PageHeader title={t("list.title")} />
      <ProblemAlert error={list.error} />
      {farms.length === 0 && !list.loading ? (
        <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
      ) : (
        <Paper variant="outlined">
          <Table size="small" aria-label={t("list.title")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("fields.code")}</TableCell>
                <TableCell>{t("fields.name")}</TableCell>
                <TableCell>{t("fields.farmType")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {farms.map((farm) => (
                <TableRow key={farm.id}>
                  <TableCell>{farm.code ?? "—"}</TableCell>
                  <TableCell>{farm.name}</TableCell>
                  <TableCell>{t(`farmTypes.${farm.farmType}`)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </>
  );
}
