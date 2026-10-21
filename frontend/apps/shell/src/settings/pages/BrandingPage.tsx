import { useRef, type ChangeEvent } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import { useTranslation } from "react-i18next";
import { modules, unwrap, useApiMutation, useApiQuery, useSnackbar, useTenantStore } from "@saaskit/web-core";
import { PageHeader, ProblemAlert, SettingsForm } from "@saaskit/ui-kit";
import { tenancy, tenancyQueries } from "../api";

const brandingModule = modules.find((m) => m.key === "tenant-settings")!.key;
/** The server accepts up to 200 000 characters; base64 adds a third. */
const maxLogoKb = 140;

/** The tenant's colour and logo. Saved values apply to the whole app at once (the theme is rebuilt from the tenant store). */
export function Component() {
  const { t } = useTranslation("settings");
  const notify = useSnackbar();
  const file = useRef<HTMLInputElement>(null);
  const view = useApiQuery(["tenancy", "settings", brandingModule], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/settings/{moduleKey}", { params: { path: { moduleKey: brandingModule } }, signal })),
  );
  const save = useApiMutation(
    (changes: Record<string, unknown>) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/settings/{moduleKey}", { params: { path: { moduleKey: brandingModule } }, body: changes as never }),
      ),
    {
      invalidate: tenancyQueries,
      success: t("branding.saved"),
      onSuccess: (saved) =>
        useTenantStore.getState().setSettings({ branding: saved.values as { primaryColor?: string; logoUrl?: string } }),
    },
  );

  function upload(event: ChangeEvent<HTMLInputElement>) {
    const chosen = event.target.files?.[0];
    event.target.value = "";
    if (!chosen) return;
    if (chosen.size > maxLogoKb * 1024) {
      notify.warning(t("branding.tooLarge", { size: maxLogoKb }));
      return;
    }
    const reader = new FileReader();
    reader.onload = () => save.mutate({ logoUrl: reader.result as string });
    reader.readAsDataURL(chosen);
  }

  const logo = (view.data?.values as { logoUrl?: string } | undefined)?.logoUrl;
  return (
    <>
      <PageHeader title={t("sections.branding")} subtitle={t("branding.subtitle")} />
      <ProblemAlert error={view.error ?? undefined} />
      <Stack spacing={3}>
        <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
          {logo ? (
            <Box component="img" src={logo} alt={t("branding.preview")} sx={{ height: 48, maxWidth: 200, objectFit: "contain" }} />
          ) : null}
          <input ref={file} type="file" accept="image/png,image/jpeg,image/svg+xml,image/webp" hidden onChange={upload} />
          <Button variant="outlined" onClick={() => file.current?.click()} disabled={save.isPending}>
            {t("branding.upload")}
          </Button>
          {logo ? (
            <Button onClick={() => save.mutate({ logoUrl: null })} disabled={save.isPending}>
              {t("branding.remove")}
            </Button>
          ) : null}
        </Stack>
        {view.data ? (
          <SettingsForm
            view={view.data}
            labels={{
              primaryColor: { label: t("branding.primaryColor"), help: t("branding.primaryColorHelp") },
              logoUrl: { label: t("branding.logoUrl"), help: t("branding.logoUrlHelp") },
            }}
            onSave={(changes) => save.mutateAsync(changes)}
            saving={save.isPending}
            error={save.error}
          />
        ) : null}
      </Stack>
    </>
  );
}
