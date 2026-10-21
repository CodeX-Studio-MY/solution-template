import Box from "@mui/material/Box";
import ButtonBase from "@mui/material/ButtonBase";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { canAccess, Slot, useSession, useSlotHasExtensions, useTenant, type ModuleManifest } from "@farm360/web-core";
import { EmptyState, Icon, PageHeader } from "@farm360/ui-kit";
import { settingsEntries } from "../layout/settingsEntries";

const cardGrid = { display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(min(100%, 320px), 1fr))", gap: 2 } as const;

/**
 * The home page: what needs attention (cards that modules contribute through the `home.dashboard` slot, so the shell never
 * imports a module) and a way into each application the person can use.
 */
export function Home({ manifests }: { manifests: readonly ModuleManifest[] }) {
  const { t } = useTranslation();
  const tenant = useTenant();
  const roles = useSession()?.moduleRoles ?? {};
  const hasWidgets = useSlotHasExtensions("home.dashboard");
  const available = manifests.filter((m) => m.menu.some((item) => canAccess(roles, m.id, item.requiredRole)));
  const hasSettings = settingsEntries(manifests, roles, t).length > 0;

  return (
    <>
      <PageHeader
        title={t("common:home.welcome")}
        subtitle={tenant.name ? t("common:home.subtitle", { name: tenant.name }) : t("common:home.intro")}
      />
      {available.length === 0 ? (
        hasSettings ? (
          <EmptyState title={t("common:home.settingsOnly")} hint={t("common:home.settingsOnlyHint")} />
        ) : (
          <EmptyState title={t("common:errors.noModules")} />
        )
      ) : (
        <Stack spacing={4}>
          {hasWidgets ? (
            <Box component="section" aria-labelledby="today">
              <Typography id="today" variant="h2" component="h2" sx={{ mb: 1.5 }}>
                {t("common:home.today")}
              </Typography>
              <Box sx={cardGrid}>
                <Slot name="home.dashboard" props={{}} />
              </Box>
            </Box>
          ) : null}
          <Box component="section" aria-labelledby="apps">
            <Typography id="apps" variant="h2" component="h2" sx={{ mb: 1.5 }}>
              {t("common:home.yourApps")}
            </Typography>
            <Box sx={cardGrid}>
              {available.map((m) => {
                const item = m.menu.find((entry) => canAccess(roles, m.id, entry.requiredRole)) ?? m.menu[0];
                const label = t(item?.label ?? m.id, { ns: m.i18n.namespace });
                return (
                  <Paper key={m.id} variant="outlined" sx={{ overflow: "hidden" }}>
                    <ButtonBase
                      component={RouterLink}
                      to={`${m.basePath}${item?.path ? `/${item.path}` : ""}`}
                      sx={{ display: "flex", justifyContent: "flex-start", gap: 2, width: "100%", p: 2, textAlign: "left" }}
                    >
                      <Icon name={item?.icon ?? m.id} color="primary" />
                      <Typography variant="h5" component="span">
                        {label}
                      </Typography>
                    </ButtonBase>
                  </Paper>
                );
              })}
            </Box>
          </Box>
        </Stack>
      )}
    </>
  );
}
