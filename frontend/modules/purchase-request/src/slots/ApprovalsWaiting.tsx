import Link from "@mui/material/Link";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { atLeast, toNumber, unwrap, useModuleRole, useResource, type SlotProps } from "@acme/web-core";
import { DashboardCard, MoneyText, ProblemAlert } from "@acme/ui-kit";
import { basePath, moduleKey } from "../basePath";
import { requests } from "../client";

/**
 * The home card for managers: purchase requests waiting for a decision. It fills the shell's `home.dashboard` slot, so the
 * shell knows nothing about purchase requests. The list API decides what the person may see.
 */
export default function ApprovalsWaiting(_props: SlotProps["home.dashboard"]) {
  const { t } = useTranslation(moduleKey);
  const canDecide = atLeast(useModuleRole(moduleKey), "manager");
  const list = useResource(
    () =>
      canDecide
        ? unwrap(requests.GET("/api/purchase-request/v1/requests", { params: { query: { Page: 1, PageSize: 5, status: "submitted" } } }))
        : Promise.resolve(undefined),
    [canDecide],
  );

  if (!canDecide) return null;
  const items = list.data?.items ?? [];
  const total = list.data ? toNumber(list.data.totalCount) : 0;

  return (
    <DashboardCard title={t("widgets.approvals.title")}>
      <ProblemAlert error={list.error} />
      {list.loading && !list.data ? (
        <Skeleton variant="rounded" height={96} />
      ) : items.length === 0 ? (
        <Typography color="text.secondary">{t("widgets.approvals.none")}</Typography>
      ) : (
        <>
          <Typography variant="h2" component="p">
            {total}
          </Typography>
          <List dense disablePadding>
            {items.map((row) => (
              <ListItem key={row.id} disableGutters sx={{ justifyContent: "space-between", gap: 2 }}>
                <Stack sx={{ minWidth: 0 }}>
                  <Link component={RouterLink} to={`${basePath}/${row.id}`} noWrap>
                    {row.number}
                  </Link>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {row.title}
                  </Typography>
                </Stack>
                <MoneyText amount={row.total} currency={row.currency} />
              </ListItem>
            ))}
          </List>
        </>
      )}
    </DashboardCard>
  );
}
