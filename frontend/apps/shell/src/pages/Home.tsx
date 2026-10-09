import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { canAccess, useSession, type ModuleManifest } from "@acme/web-core";
import { EmptyState, PageHeader } from "@acme/ui-kit";

export function Home({ manifests }: { manifests: readonly ModuleManifest[] }) {
  const { t } = useTranslation();
  const roles = useSession()?.moduleRoles ?? {};
  const available = manifests.filter((m) => m.menu.some((item) => canAccess(roles, m.id, item.requiredRole)));
  return (
    <>
      <PageHeader title={t("common:home.welcome")} subtitle={t("common:home.intro")} />
      {available.length === 0 ? (
        <EmptyState title={t("common:errors.noModules")} />
      ) : (
        <Stack spacing={1} component="section" aria-labelledby="apps">
          <Typography id="apps" variant="h2" component="h2">
            {t("common:home.yourApps")}
          </Typography>
          {available.map((m) => (
            <Link key={m.id} component={RouterLink} to={m.basePath}>
              {t(m.menu[0]?.label ?? m.id, { ns: m.i18n.namespace })}
            </Link>
          ))}
        </Stack>
      )}
    </>
  );
}
