import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import Add from "@mui/icons-material/Add";
import MoreVert from "@mui/icons-material/MoreVert";
import { useTranslation } from "react-i18next";
import { ApiError, atLeast, toNumber, unwrap, unwrapEmpty, useFormatters, useModuleRole, useResource } from "@farm360/web-core";
import { ConfirmDialog, EmptyState, PageHeader, ProblemAlert, StatusChip } from "@farm360/ui-kit";
import { moduleKey } from "../basePath";
import { client } from "../client";
import type { FarmDto } from "../types";
import { bodyFromDto } from "./farmForm";
import { FarmDrawer } from "./FarmDrawer";

const pageSizes = [25, 50, 100];

/** Read by screen readers, not shown (the column of row menus needs a header name). */
const visuallyHidden = {
  position: "absolute",
  width: 1,
  height: 1,
  overflow: "hidden",
  clip: "rect(0 0 0 0)",
  whiteSpace: "nowrap",
} as const;

/**
 * Settings → Master → Farm: the tenant's farms in their order, with add and edit in a drawer, move up and down, and delete for
 * managers. Every change carries the version the list showed (If-Match); when someone else changed the farm first, the list reloads.
 */
export function Component() {
  const { t } = useTranslation("master-data");
  const format = useFormatters();
  const role = useModuleRole(moduleKey);
  const canDelete = role !== null && atLeast(role, "manager");

  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(pageSizes[0]!);
  const list = useResource(
    () => unwrap(client.GET("/api/master-data/v1/farms", { params: { query: { Page: page + 1, PageSize: pageSize } } })),
    [page, pageSize],
  );

  // The drawer keeps its farm while it closes (so its title doesn't change mid-animation); each opening is a new session (a fresh form).
  const [drawer, setDrawer] = useState<{ open: boolean; farm: FarmDto | undefined; session: number }>({
    open: false,
    farm: undefined,
    session: 0,
  });
  const openDrawer = (farm: FarmDto | undefined) => setDrawer((d) => ({ open: true, farm, session: d.session + 1 }));
  const closeDrawer = () => setDrawer((d) => ({ ...d, open: false }));
  const [menu, setMenu] = useState<{ anchor: HTMLElement; farm: FarmDto } | undefined>(undefined);
  const [deleting, setDeleting] = useState<FarmDto | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<Error | undefined>(undefined);

  const farms = list.data?.items ?? [];
  const total = list.data ? toNumber(list.data.totalCount) : 0;
  const stale = actionError instanceof ApiError && actionError.status === 412;

  /** Runs a change, then reloads. A stale version (412) is explained and the list reloaded, so the next try uses fresh data. */
  async function change(action: () => Promise<unknown>) {
    setBusy(true);
    setActionError(undefined);
    try {
      await action();
    } catch (e) {
      setActionError(e as Error);
    } finally {
      setBusy(false);
      await list.reload();
    }
  }

  // Moving resends the whole farm (PUT replaces it) with the new position; the server shifts the farms in between.
  const move = (farm: FarmDto, by: number) =>
    change(() =>
      unwrap(
        client.PUT("/api/master-data/v1/farms/{id}", {
          params: { path: { id: farm.id }, header: { "If-Match": farm.eTag } },
          body: bodyFromDto(farm, { orderSequence: toNumber(farm.orderSequence) + by }),
        }),
      ),
    );

  const remove = (farm: FarmDto) =>
    change(() =>
      unwrapEmpty(
        client.DELETE("/api/master-data/v1/farms/{id}", { params: { path: { id: farm.id }, header: { "If-Match": farm.eTag } } }),
      ),
    );

  const position = (farm: FarmDto) => toNumber(farm.orderSequence);

  return (
    <>
      <PageHeader
        title={t("list.title")}
        subtitle={t("list.subtitle")}
        actions={
          <Button variant="contained" startIcon={<Add />} onClick={() => openDrawer(undefined)}>
            {t("actions.add")}
          </Button>
        }
      />

      <Stack spacing={2}>
        {stale ? (
          <Alert severity="warning" onClose={() => setActionError(undefined)}>
            {t("list.stale")}
          </Alert>
        ) : (
          <ProblemAlert error={actionError ?? list.error} />
        )}

        {farms.length === 0 && !list.loading ? (
          <EmptyState title={t("list.empty")} hint={t("list.emptyHint")} />
        ) : (
          <Paper variant="outlined">
            <TableContainer>
              <Table size="small" aria-label={t("list.title")}>
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ width: 56 }}>{t("fields.orderSequence")}</TableCell>
                    <TableCell>{t("fields.code")}</TableCell>
                    <TableCell>{t("fields.name")}</TableCell>
                    <TableCell>{t("fields.farmType")}</TableCell>
                    <TableCell align="right">{t("fields.areaHectares")}</TableCell>
                    <TableCell>{t("fields.status")}</TableCell>
                    <TableCell sx={{ width: 56 }}>
                      <Box component="span" sx={visuallyHidden}>
                        {t("list.actions")}
                      </Box>
                    </TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {farms.map((farm) => (
                    <TableRow key={farm.id} hover>
                      <TableCell>{position(farm)}</TableCell>
                      <TableCell>{farm.code ?? "—"}</TableCell>
                      <TableCell>{farm.name}</TableCell>
                      <TableCell>{t(`farmTypes.${farm.farmType}`)}</TableCell>
                      <TableCell align="right">{farm.areaHectares === null ? "—" : format.number(farm.areaHectares)}</TableCell>
                      <TableCell>
                        <Stack direction="row" spacing={1}>
                          <StatusChip
                            label={farm.isActive ? t("status.active") : t("status.inactive")}
                            tone={farm.isActive ? "success" : "default"}
                          />
                          {farm.isExternal ? <StatusChip label={t("status.external")} tone="info" /> : null}
                        </Stack>
                      </TableCell>
                      <TableCell>
                        <IconButton
                          size="small"
                          aria-label={t("list.actionsFor", { name: farm.name })}
                          aria-haspopup="menu"
                          disabled={busy}
                          onClick={(e) => setMenu({ anchor: e.currentTarget, farm })}
                        >
                          <MoreVert fontSize="small" />
                        </IconButton>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <TablePagination
              component="div"
              count={total}
              page={page}
              rowsPerPage={pageSize}
              rowsPerPageOptions={pageSizes}
              onPageChange={(_, next) => setPage(next)}
              onRowsPerPageChange={(e) => {
                setPageSize(Number(e.target.value));
                setPage(0);
              }}
            />
          </Paper>
        )}
      </Stack>

      <Menu anchorEl={menu?.anchor} open={Boolean(menu)} onClose={() => setMenu(undefined)} slotProps={{ list: { dense: true } }}>
        {menu
          ? [
              <MenuItem
                key="edit"
                onClick={() => {
                  openDrawer(menu.farm);
                  setMenu(undefined);
                }}
              >
                {t("actions.edit")}
              </MenuItem>,
              <MenuItem
                key="up"
                disabled={position(menu.farm) <= 1}
                onClick={() => {
                  void move(menu.farm, -1);
                  setMenu(undefined);
                }}
              >
                {t("actions.moveUp")}
              </MenuItem>,
              <MenuItem
                key="down"
                disabled={position(menu.farm) >= total}
                onClick={() => {
                  void move(menu.farm, +1);
                  setMenu(undefined);
                }}
              >
                {t("actions.moveDown")}
              </MenuItem>,
              ...(canDelete
                ? [
                    <MenuItem
                      key="delete"
                      sx={{ color: "error.main" }}
                      onClick={() => {
                        setDeleting(menu.farm);
                        setMenu(undefined);
                      }}
                    >
                      {t("actions.delete")}
                    </MenuItem>,
                  ]
                : []),
            ]
          : null}
      </Menu>

      <FarmDrawer
        key={drawer.session}
        open={drawer.open}
        farm={drawer.farm}
        onClose={closeDrawer}
        onSaved={() => {
          closeDrawer();
          setActionError(undefined);
          void list.reload();
        }}
      />

      <ConfirmDialog
        open={deleting !== undefined}
        title={t("delete.title")}
        text={t("delete.text", { name: deleting?.name ?? "" })}
        confirmLabel={t("actions.delete")}
        busy={busy}
        onCancel={() => setDeleting(undefined)}
        onConfirm={() => {
          if (!deleting) return;
          setDeleting(undefined);
          void remove(deleting);
        }}
      />
    </>
  );
}
