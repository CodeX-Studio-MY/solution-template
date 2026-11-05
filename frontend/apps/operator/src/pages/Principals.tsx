import { useDeferredValue, useState } from "react";
import { useNavigate } from "react-router";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, StatusChip, tokens } from "@saaskit/ui-kit";
import { principalsKey, tenancy, type PrincipalType } from "../api";
import { DirectoryNotice, Identifier, Pager, PrincipalFormDialog, usePrincipalKind } from "../components/principals";

/** Everyone and everything that can be given access to a tenant: users, devices and applications. */
export function Component() {
  const { t } = useTranslation("operator");
  const navigate = useNavigate();
  const kind = usePrincipalKind();
  const [type, setType] = useState<PrincipalType | "all">("all");
  const [search, setSearch] = useState("");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const [adding, setAdding] = useState(false);
  const term = useDeferredValue(search.trim());
  const list = useApiQuery([...principalsKey, { type, term, ...paging }], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/principals", {
        params: {
          query: { type: type === "all" ? undefined : type, search: term || undefined, Page: paging.page, PageSize: paging.pageSize },
        },
        signal,
      }),
    ),
  );
  const items = list.data?.items;
  const filtered = type !== "all" || term.length > 0;

  return (
    <>
      <PageHeader
        title={t("principals.title")}
        subtitle={t("principals.subtitle")}
        actions={
          <Button variant="contained" onClick={() => setAdding(true)} sx={{ whiteSpace: "nowrap" }}>
            {t("principals.add")}
          </Button>
        }
      />
      <DirectoryNotice />
      <ProblemAlert error={list.error ?? undefined} />
      <Stack
        direction={{ xs: "column", md: "row" }}
        spacing={2}
        sx={{ mb: 2, justifyContent: "space-between", alignItems: { md: "center" } }}
      >
        <ToggleButtonGroup
          exclusive
          size="small"
          color="primary"
          aria-label={t("principals.fields.type")}
          value={type}
          onChange={(_, next: PrincipalType | "all" | null) => {
            if (!next) return;
            setType(next);
            setPaging((p) => ({ ...p, page: 1 }));
          }}
          sx={{
            flexWrap: "wrap",
            "& .MuiToggleButton-root": { color: "text.primary", textTransform: "none" },
            "& .MuiToggleButton-root.Mui-selected, & .MuiToggleButton-root.Mui-selected:hover": {
              bgcolor: "primary.main",
              color: "primary.contrastText",
            },
          }}
        >
          <ToggleButton value="all">{t("principals.all")}</ToggleButton>
          <ToggleButton value="user">{t("principals.users")}</ToggleButton>
          <ToggleButton value="device">{t("principals.devices")}</ToggleButton>
          <ToggleButton value="application">{t("principals.applications")}</ToggleButton>
        </ToggleButtonGroup>
        <TextField
          size="small"
          type="search"
          label={t("principals.search")}
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPaging((p) => ({ ...p, page: 1 }));
          }}
          sx={{ width: { xs: "100%", md: 320 } }}
        />
      </Stack>
      {items?.length === 0 ? (
        <EmptyState
          title={t(filtered ? "principals.noMatches" : "principals.empty")}
          hint={filtered ? t("principals.noMatchesHint") : t("principals.emptyHint")}
        />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("principals.title")} sx={{ "& td, & th": { height: tokens.density.rowCompact } }}>
            <TableHead>
              <TableRow>
                <TableCell>{t("principals.fields.displayName")}</TableCell>
                <TableCell>{t("principals.fields.type")}</TableCell>
                <TableCell>{t("principals.fields.identifier")}</TableCell>
                <TableCell align="right">{t("principals.tenants")}</TableCell>
                <TableCell>{t("tenants.status")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(items ?? []).map((row) => (
                <TableRow
                  key={row.principalId}
                  hover
                  onClick={() => void navigate(`/principals/${row.principalId}`)}
                  sx={{ cursor: "pointer" }}
                >
                  <TableCell>
                    <Button
                      variant="text"
                      onClick={() => void navigate(`/principals/${row.principalId}`)}
                      sx={{ p: 0, minWidth: 0, textTransform: "none", textAlign: "left" }}
                    >
                      {row.displayName}
                    </Button>
                  </TableCell>
                  <TableCell>{kind(row)}</TableCell>
                  <TableCell sx={{ whiteSpace: "nowrap" }}>
                    {row.type === "application" ? row.identifier : <Identifier>{row.identifier}</Identifier>}
                  </TableCell>
                  <TableCell align="right">{row.tenantCount}</TableCell>
                  <TableCell>
                    <StatusChip
                      label={t(row.isActive ? "principals.active" : "principals.inactive")}
                      tone={row.isActive ? "success" : "default"}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            count={Number(list.data?.totalCount ?? 0)}
            page={paging.page}
            pageSize={paging.pageSize}
            onPage={(page, pageSize) => setPaging({ page, pageSize })}
          />
        </Paper>
      )}
      {adding ? <PrincipalFormDialog onClose={() => setAdding(false)} onCreated={(id) => void navigate(`/principals/${id}`)} /> : null}
    </>
  );
}
