import { useState } from "react";
import { Link as RouterLink, useParams } from "react-router";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation, useApiQuery, useDialog, useFormatters } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { principalsKey, tenancy, tenantsKey, type PrincipalDetail } from "../api";
import { AccessTrail } from "../components/AccessTrail";
import { DirectoryNotice, Identifier, PrincipalFormDialog, usePrincipalKind } from "../components/principals";
import { accessTone } from "./statusTone";

/** One principal: what it is, which tenants it can reach, and how that access changed. */
export function Component() {
  const { t } = useTranslation("operator");
  const { id = "" } = useParams();
  const dialog = useDialog();
  const kind = usePrincipalKind();
  const [editing, setEditing] = useState(false);
  const principal = useApiQuery([...principalsKey, id], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/principals/{id}", { params: { path: { id } }, signal })),
  );
  const data = principal.data;
  const setActive = useApiMutation(
    (active: boolean) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/principals/{id}/active", {
          params: { path: { id }, header: { "If-Match": data?.eTag } },
          body: { active },
        }),
      ),
    {
      invalidate: [principalsKey, tenantsKey],
      success: (_, active) => t(active ? "principals.activated" : "principals.deactivated", { name: data?.displayName }),
    },
  );

  async function toggleActive(detail: PrincipalDetail) {
    if (
      !detail.isActive ||
      (await dialog.confirm({
        title: t("principals.deactivateTitle", { name: detail.displayName }),
        text: t("principals.deactivateText"),
        confirmLabel: t("principals.deactivate"),
        tone: "danger",
      }))
    )
      setActive.mutate(!detail.isActive);
  }

  return (
    <>
      <Link component={RouterLink} to="/principals" underline="hover" sx={{ display: "inline-block", mb: 2 }}>
        {t("principals.back")}
      </Link>
      <ProblemAlert error={principal.error ?? setActive.error ?? undefined} />
      {data ? (
        <>
          <PageHeader
            title={data.displayName}
            subtitle={kind(data)}
            actions={
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap" }}>
                <StatusChip
                  label={t(data.isActive ? "principals.active" : "principals.inactive")}
                  tone={data.isActive ? "success" : "default"}
                />
                <Button variant="outlined" onClick={() => setEditing(true)}>
                  {t("principals.edit")}
                </Button>
                <Button
                  variant="outlined"
                  color={data.isActive ? "error" : "primary"}
                  disabled={setActive.isPending}
                  onClick={() => void toggleActive(data)}
                >
                  {t(data.isActive ? "principals.deactivate" : "principals.activate")}
                </Button>
              </Stack>
            }
          />
          <DirectoryNotice />
          <Facts principal={data} />
          <Reach principal={data} />
          {data.type === "user" ? <Providers principal={data} /> : null}
          {editing ? <PrincipalFormDialog existing={data} onClose={() => setEditing(false)} /> : null}
        </>
      ) : null}
    </>
  );
}

/** The details of this type, as a quiet line under the title. */
function Facts({ principal }: { principal: PrincipalDetail }) {
  const { t } = useTranslation("operator");
  const facts: [string, React.ReactNode][] = [];
  if (principal.email) facts.push([t("principals.fields.email"), <Identifier key="email">{principal.email}</Identifier>]);
  if (principal.type === "device") {
    facts.push([t("principals.fields.identifier"), <Identifier key="identifier">{principal.identifier}</Identifier>]);
    if (principal.manufacturer) facts.push([t("principals.fields.manufacturer"), principal.manufacturer]);
    if (principal.model) facts.push([t("principals.fields.model"), principal.model]);
  }
  if (principal.ownerName) facts.push([t("principals.fields.ownerName"), principal.ownerName]);
  if (principal.contactEmail)
    facts.push([t("principals.fields.contactEmail"), <Identifier key="contact">{principal.contactEmail}</Identifier>]);
  return (
    <Box component="dl" sx={{ display: "flex", flexWrap: "wrap", columnGap: 4, rowGap: 1, m: 0, mb: 3 }}>
      {facts.map(([label, value]) => (
        <div key={label}>
          <Typography component="dt" variant="caption" color="text.secondary">
            {label}
          </Typography>
          <Typography component="dd" variant="body2" sx={{ m: 0, overflowWrap: "anywhere" }}>
            {value}
          </Typography>
        </div>
      ))}
    </Box>
  );
}

/** Which tenants it can reach, beside the trail of the tenant picked. */
function Reach({ principal }: { principal: PrincipalDetail }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const [picked, setPicked] = useState(0);
  const tenants = principal.tenants;
  const current = tenants[picked] ?? tenants[0];

  if (!current) return <EmptyState title={t("access.noTenants")} hint={t("access.noTenantsHint")} />;
  return (
    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "minmax(0, 3fr) minmax(0, 2fr)" }, gap: 3, mb: 3 }}>
      <section aria-labelledby="tenant-access">
        <Typography id="tenant-access" variant="h3" component="h2" sx={{ mb: 1.5 }}>
          {t("access.tenantAccess")}
        </Typography>
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-labelledby="tenant-access">
            <TableHead>
              <TableRow>
                <TableCell>{t("tenants.name")}</TableCell>
                <TableCell>{t("tenants.status")}</TableCell>
                <TableCell>{t("access.granted")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {tenants.map((row, index) => (
                <TableRow key={row.tenantId} hover selected={row === current} onClick={() => setPicked(index)} sx={{ cursor: "pointer" }}>
                  <TableCell>
                    <Button
                      variant="text"
                      aria-pressed={row === current}
                      onClick={() => setPicked(index)}
                      sx={{ p: 0, minWidth: 0, textTransform: "none", textAlign: "left" }}
                    >
                      {row.tenantName}
                    </Button>
                  </TableCell>
                  <TableCell>
                    <StatusChip label={t(`access.statuses.${row.status}`)} tone={accessTone[row.status]} />
                  </TableCell>
                  <TableCell>{format.dateTime(row.grantedOn)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      </section>
      <Trail principalId={principal.principalId} tenantId={current.tenantId} tenantName={current.tenantName} />
    </Box>
  );
}

function Trail({ principalId, tenantId, tenantName }: { principalId: string; tenantId: string; tenantName: string }) {
  const { t } = useTranslation("operator");
  const audit = useApiQuery([...principalsKey, principalId, "audit", tenantId], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/principals/{id}/access-audit", {
        params: { path: { id: principalId }, query: { tenantId, PageSize: 100 } },
        signal,
      }),
    ),
  );
  return (
    <section aria-labelledby="access-trail">
      <Typography id="access-trail" variant="h3" component="h2" sx={{ mb: 1.5, overflowWrap: "anywhere" }}>
        {t("access.trail", { name: tenantName })}
      </Typography>
      <ProblemAlert error={audit.error ?? undefined} />
      {audit.data ? <AccessTrail rows={audit.data.items} /> : null}
    </section>
  );
}

function Providers({ principal }: { principal: PrincipalDetail }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  return (
    <section aria-labelledby="linked-accounts">
      <Typography id="linked-accounts" variant="h3" component="h2" sx={{ mb: 1.5 }}>
        {t("principals.linkedAccounts")}
      </Typography>
      {principal.providers.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          {t("principals.noLinkedAccounts")}
        </Typography>
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-labelledby="linked-accounts">
            <TableHead>
              <TableRow>
                <TableCell>{t("principals.provider")}</TableCell>
                <TableCell>{t("principals.fields.email")}</TableCell>
                <TableCell>{t("principals.linkedOn")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {principal.providers.map((row) => (
                <TableRow key={row.provider}>
                  <TableCell>{t(`logins.methods.${row.provider}`)}</TableCell>
                  <TableCell>
                    <Identifier>{row.providerEmail}</Identifier>
                  </TableCell>
                  <TableCell>{format.dateTime(row.linkedOn)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </section>
  );
}
