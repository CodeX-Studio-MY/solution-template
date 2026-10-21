import { useState, type FormEvent } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import type { TenancySchemas } from "@saaskit/api-clients";
import { ApiError, platformDefaults } from "@saaskit/web-core";

export type TenantProfile = TenancySchemas["schemas"]["TenantProfile"];

const timeZones = Intl.supportedValuesOf("timeZone");
const currencies = Intl.supportedValuesOf("currency");

interface TenantProfileFormProps {
  value: TenantProfile;
  onSave: (profile: TenantProfile) => void;
  saving?: boolean;
  error?: Error | null;
  /** Read-only (someone who may see the profile but not change it). */
  disabled?: boolean;
}

/** A tenant's name, time zone, currency and language: the tenant admin's Settings and the operator console use this one form. */
export function TenantProfileForm({ value, onSave, saving, error, disabled }: TenantProfileFormProps) {
  const { t } = useTranslation();
  // What was typed, over the saved profile; a new saved profile (after a save) starts clean.
  const [edits, setEdits] = useState<{ base: TenantProfile; values: Partial<TenantProfile> }>({ base: value, values: {} });
  if (edits.base !== value) setEdits({ base: value, values: {} });
  const form = { ...value, ...(edits.base === value ? edits.values : {}) };
  const set = (name: keyof TenantProfile) => (next: string | null) =>
    setEdits((e) => ({ base: e.base, values: { ...e.values, [name]: next ?? "" } }));
  const fieldError = (name: string) => (error instanceof ApiError ? error.forField(name)?.message : undefined);

  function submit(event: FormEvent) {
    event.preventDefault();
    onSave(form);
  }

  return (
    <form onSubmit={submit} noValidate>
      <Stack spacing={2.5} sx={{ maxWidth: 560 }}>
        <TextField
          label={t("common:profile.displayName")}
          required
          value={form.displayName}
          onChange={(e) => set("displayName")(e.target.value)}
          disabled={disabled}
          error={Boolean(fieldError("displayName"))}
          helperText={fieldError("displayName")}
        />
        <Autocomplete
          options={timeZones}
          value={form.timeZone || null}
          onChange={(_, next) => set("timeZone")(next)}
          disabled={disabled}
          renderInput={(params) => (
            <TextField
              {...params}
              label={t("common:profile.timeZone")}
              required
              error={Boolean(fieldError("timeZone"))}
              helperText={fieldError("timeZone")}
            />
          )}
        />
        <Autocomplete
          options={currencies}
          value={form.currency || null}
          onChange={(_, next) => set("currency")(next)}
          disabled={disabled}
          renderInput={(params) => (
            <TextField
              {...params}
              label={t("common:profile.currency")}
              required
              error={Boolean(fieldError("currency"))}
              helperText={fieldError("currency")}
            />
          )}
        />
        <TextField
          select
          label={t("common:profile.locale")}
          value={form.locale}
          onChange={(e) => set("locale")(e.target.value)}
          disabled={disabled}
        >
          {platformDefaults.locales.map((locale) => (
            <MenuItem key={locale} value={locale}>
              {new Intl.DisplayNames([locale], { type: "language" }).of(locale) ?? locale}
            </MenuItem>
          ))}
        </TextField>
        {disabled ? null : (
          <div>
            <Button type="submit" variant="contained" disabled={saving || Object.keys(edits.values).length === 0}>
              {t("common:actions.save")}
            </Button>
          </div>
        )}
      </Stack>
    </form>
  );
}
