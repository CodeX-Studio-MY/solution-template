import { useDeferredValue, useState } from "react";
import { Link as RouterLink } from "react-router";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useApiMutation, useApiQuery, useFormatters } from "@saaskit/web-core";
import { ActivityTable, EmptyState, FormDialog, ProblemAlert, StatusChip, tokens } from "@saaskit/ui-kit";
import { principalsKey, tenancy, tenantsKey, type AccessStatus, type PrincipalSummary, type TenantPrincipalRow } from "../api";
import { DirectoryNotice, Identifier, Pager, usePrincipalKind } from "../components/principals";
import { accessTone } from "./statusTone";

/** What the grant dialog needs: a row from the principal list, or the row of someone whose access was revoked. */
type Grantable = Pick<PrincipalSummary, "principalId" | "displayName" | "identifier" | "isActive">;

type Move = "suspend" | "resume" | "revoke";
const moveTo: Record<Move, AccessStatus> = { suspend: "suspended", resume: "active", revoke: "revoked" };
const movesFrom: Record<AccessStatus, Move[]> = { active: ["suspend", "revoke"], suspended: ["resume", "revoke"], revoked: [] };

/** Who has access to this tenant, the moves each one allows, and the history of those moves. */
export function TenantAccess({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const kind = usePrincipalKind();
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const [granting, setGranting] = useState<Grantable | "new" | null>(null);
  const [moving, setMoving] = useState<{ row: TenantPrincipalRow; move: Move } | null>(null);
  const list = useApiQuery([...tenantsKey, tenantId, "principals", paging], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/principals", {
        params: { path: { id: tenantId }, query: { Page: paging.page, PageSize: paging.pageSize } },
        signal,
      }),
    ),
  );
  const items = list.data?.items;

  return (
    <>
      <DirectoryNotice />
      <ProblemAlert error={list.error ?? undefined} />
      <Stack direction="row" sx={{ justifyContent: "flex-end", mb: 2 }}>
        <Button variant="contained" onClick={() => setGranting("new")}>
          {t("access.grant")}
        </Button>
      </Stack>
      {items?.length === 0 ? (
        <EmptyState title={t("access.empty")} hint={t("access.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto", mb: 4 }}>
          <Table size="small" aria-label={t("tenant.tabs.access")} sx={{ "& td, & th": { height: tokens.density.rowCompact } }}>
            <TableHead>
              <TableRow>
                <TableCell>{t("principals.fields.displayName")}</TableCell>
                <TableCell>{t("principals.fields.type")}</TableCell>
                <TableCell>{t("principals.fields.identifier")}</TableCell>
                <TableCell>{t("tenants.status")}</TableCell>
                <TableCell>{t("access.granted")}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {(items ?? []).map((row) => (
                <TableRow key={row.principalId}>
                  <TableCell>
                    <Link component={RouterLink} to={`/principals/${row.principalId}`} underline="hover">
                      {row.displayName}
                    </Link>
                    {row.isActive ? null : (
                      <Typography variant="caption" color="text.secondary" component="p">
                        {t("principals.inactive")}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell>{kind(row)}</TableCell>
                  <TableCell sx={{ whiteSpace: "nowrap" }}>
                    {row.type === "application" ? row.identifier : <Identifier>{row.identifier}</Identifier>}
                  </TableCell>
                  <TableCell>
                    <StatusChip label={t(`access.statuses.${row.status}`)} tone={accessTone[row.status]} />
                  </TableCell>
                  <TableCell>
                    {format.dateTime(row.grantedOn)}
                    {row.grantedByName ? (
                      <Typography variant="caption" color="text.secondary" component="p">
                        {row.grantedByName}
                      </Typography>
                    ) : null}
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>
                    {movesFrom[row.status].map((move) => (
                      <Button
                        key={move}
                        size="small"
                        color={move === "revoke" ? "error" : "primary"}
                        aria-label={t(`access.${move}Named`, { name: row.displayName })}
                        onClick={() => setMoving({ row, move })}
                      >
                        {t(`access.${move}`)}
                      </Button>
                    ))}
                    {row.status === "revoked" ? (
                      <Button
                        size="small"
                        aria-label={t("access.regrantNamed", { name: row.displayName })}
                        onClick={() => setGranting(row)}
                      >
                        {t("access.regrant")}
                      </Button>
                    ) : null}
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
      <AccessHistory tenantId={tenantId} />
      {granting ? (
        <GrantDialog tenantId={tenantId} preset={granting === "new" ? null : granting} onClose={() => setGranting(null)} />
      ) : null}
      {moving ? <MoveDialog tenantId={tenantId} row={moving.row} move={moving.move} onClose={() => setMoving(null)} /> : null}
    </>
  );
}

function AccessHistory({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const history = useApiQuery([...tenantsKey, tenantId, "principal-audit", paging], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/principal-access-audit", {
        params: { path: { id: tenantId }, query: { Page: paging.page, PageSize: paging.pageSize } },
        signal,
      }),
    ),
  );
  return (
    <section aria-labelledby="access-history">
      <Typography id="access-history" variant="h3" component="h2" sx={{ mb: 1.5 }}>
        {t("access.history")}
      </Typography>
      <ProblemAlert error={history.error ?? undefined} />
      <ActivityTable
        label={t("access.history")}
        rows={history.data?.items.map((row) => ({
          id: row.id,
          occurredOn: row.occurredOn,
          kind: t(`access.actions.${row.action}`),
          who: row.actorName ?? row.actorId,
          subject: row.principalName,
          details: row.reason,
        }))}
        totalCount={Number(history.data?.totalCount ?? 0)}
        page={paging.page}
        pageSize={paging.pageSize}
        onPage={(page, pageSize) => setPaging({ page, pageSize })}
      />
    </section>
  );
}

function GrantDialog({ tenantId, preset, onClose }: { tenantId: string; preset: Grantable | null; onClose: () => void }) {
  const { t } = useTranslation("operator");
  const [input, setInput] = useState("");
  const [picked, setPicked] = useState<Grantable | null>(preset);
  const [reason, setReason] = useState("");
  const term = useDeferredValue(input.trim());
  const options = useApiQuery(
    [...principalsKey, "grant", term],
    (signal) =>
      unwrap(
        tenancy.GET("/api/tenancy/v1/operator/principals", { params: { query: { search: term || undefined, PageSize: 20 } }, signal }),
      ),
    { enabled: !preset },
  );
  const grant = useApiMutation(
    () =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/principals/{principalId}", {
          params: { path: { id: tenantId, principalId: picked!.principalId } },
          body: { reason },
        }),
      ),
    {
      invalidate: [tenantsKey, principalsKey],
      success: (saved) => t("access.granted_done", { name: saved.displayName }),
      onSuccess: onClose,
    },
  );
  const reasonError = grant.error instanceof ApiError ? grant.error.forField("reason")?.message : undefined;
  const generalError = grant.error && !reasonError ? grant.error.message : undefined;
  const label = (option: Grantable) => (option.identifier ? `${option.displayName} (${option.identifier})` : option.displayName);

  return (
    <FormDialog
      open
      title={t("access.grantTitle")}
      submitLabel={t("access.grant")}
      busy={grant.isPending}
      onSubmit={() => {
        if (picked) grant.mutate();
      }}
      onClose={onClose}
      maxWidth="sm"
    >
      {generalError ? <Alert severity="error">{generalError}</Alert> : null}
      <Autocomplete<Grantable>
        options={options.data?.items ?? []}
        value={picked}
        disabled={Boolean(preset)}
        loading={options.isLoading}
        filterOptions={(all) => all}
        getOptionLabel={label}
        isOptionEqualToValue={(a, b) => a.principalId === b.principalId}
        getOptionDisabled={(option) => !option.isActive}
        onChange={(_, next) => setPicked(next)}
        onInputChange={(_, next) => setInput(next)}
        noOptionsText={t("principals.noMatches")}
        renderInput={(params) => <TextField {...params} autoFocus label={t("access.principal")} required />}
      />
      <Typography variant="body2">
        <Link component={RouterLink} to="/principals">
          {t("access.addPrincipal")}
        </Link>
      </Typography>
      <TextField
        label={t("access.reasonOptional")}
        multiline
        minRows={2}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        error={Boolean(reasonError)}
        helperText={reasonError}
      />
    </FormDialog>
  );
}

/** Suspend, resume or revoke. Each needs a reason, and the button says exactly what will happen. */
function MoveDialog({ tenantId, row, move, onClose }: { tenantId: string; row: TenantPrincipalRow; move: Move; onClose: () => void }) {
  const { t } = useTranslation("operator");
  const [reason, setReason] = useState("");
  const change = useApiMutation(
    () =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/principals/{principalId}/status", {
          params: { path: { id: tenantId, principalId: row.principalId } },
          body: { status: moveTo[move], reason },
        }),
      ),
    {
      invalidate: [tenantsKey, principalsKey],
      success: () => t(`access.${move}Done`, { name: row.displayName }),
      onSuccess: onClose,
    },
  );
  const reasonError = change.error instanceof ApiError ? change.error.forField("reason")?.message : undefined;
  const generalError = change.error && !reasonError ? change.error.message : undefined;
  return (
    <FormDialog
      open
      title={t(`access.${move}Title`, { name: row.displayName })}
      text={t("access.moveText")}
      submitLabel={t(`access.${move}Confirm`)}
      busy={change.isPending || !reason.trim()}
      tone={move === "resume" ? "default" : "danger"}
      onSubmit={() => {
        if (reason.trim()) change.mutate();
      }}
      onClose={onClose}
      maxWidth="sm"
    >
      {generalError ? <Alert severity="error">{generalError}</Alert> : null}
      <TextField
        autoFocus
        required
        multiline
        minRows={2}
        label={t("tenant.reason")}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        error={Boolean(reasonError)}
        helperText={reasonError}
      />
    </FormDialog>
  );
}

/** Every login attempt in this tenant, newest first. Empty until sign-in uses the directory. */
export function TenantLogins({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const logins = useApiQuery([...tenantsKey, tenantId, "principal-logins", paging], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/principal-logins", {
        params: { path: { id: tenantId }, query: { Page: paging.page, PageSize: paging.pageSize } },
        signal,
      }),
    ),
  );
  const items = logins.data?.items;
  return (
    <>
      <ProblemAlert error={logins.error ?? undefined} />
      {items?.length === 0 ? (
        <EmptyState title={t("logins.empty")} hint={t("logins.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("tenant.tabs.logins")} sx={{ "& td, & th": { height: tokens.density.rowCompact } }}>
            <TableHead>
              <TableRow>
                <TableCell>{t("logins.when")}</TableCell>
                <TableCell>{t("logins.who")}</TableCell>
                <TableCell>{t("logins.method")}</TableCell>
                <TableCell>{t("logins.result")}</TableCell>
                <TableCell>{t("logins.address")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(items ?? []).map((row) => (
                <TableRow key={row.id}>
                  <TableCell>{format.dateTime(row.occurredOn)}</TableCell>
                  <TableCell>{row.principalName ?? t("logins.unknown")}</TableCell>
                  <TableCell>{t(`logins.methods.${row.method}`)}</TableCell>
                  <TableCell>
                    <StatusChip
                      label={row.succeeded ? t("logins.succeeded") : t(`logins.failures.${row.failureReason ?? "wrongCredentials"}`)}
                      tone={row.succeeded ? "success" : "error"}
                    />
                  </TableCell>
                  <TableCell>
                    <Identifier>{row.remoteAddress}</Identifier>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            count={Number(logins.data?.totalCount ?? 0)}
            page={paging.page}
            pageSize={paging.pageSize}
            onPage={(page, pageSize) => setPaging({ page, pageSize })}
          />
        </Paper>
      )}
    </>
  );
}
