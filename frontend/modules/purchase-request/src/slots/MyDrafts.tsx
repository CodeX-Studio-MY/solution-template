import Link from "@mui/material/Link";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { unwrap, useResource, type SlotProps } from "@acme/web-core";
import { DashboardCard, MoneyText, ProblemAlert } from "@acme/ui-kit";
import { basePath, moduleKey } from "../basePath";
import { requests } from "../client";

/** The home card for everyone with the module: the person's own requests that are still drafts. */
export default function MyDrafts(_props: SlotProps["home.dashboard"]) {
  const { t } = useTranslation(moduleKey);
  const list = useResource(
    () =>
      unwrap(
        requests.GET("/api/purchase-request/v1/requests", { params: { query: { Page: 1, PageSize: 5, status: "draft", mine: true } } }),
      ),
    [],
  );
  const items = list.data?.items ?? [];

  return (
    <DashboardCard title={t("widgets.drafts.title")}>
      <ProblemAlert error={list.error} />
      {list.loading && !list.data ? (
        <Skeleton variant="rounded" height={96} />
      ) : items.length === 0 ? (
        <Typography color="text.secondary">{t("widgets.drafts.none")}</Typography>
      ) : (
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
      )}
    </DashboardCard>
  );
}
