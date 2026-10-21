import { useState, type FormEvent } from "react";
import Button from "@mui/material/Button";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import RestartAlt from "@mui/icons-material/RestartAlt";
import { useTranslation } from "react-i18next";
import type { TenancySchemas } from "@saaskit/api-clients";
import { ApiError } from "@saaskit/web-core";

export type SettingsView = TenancySchemas["schemas"]["SettingsView"];

interface SettingsFormProps {
  view: SettingsView;
  /** Labels and help for each setting, by name (camelCase as the API sends it). A missing label shows the name. */
  labels?: Record<string, { label: string; help?: string }>;
  /** `true` when the editor may change every setting (an operator); a tenant admin may change only the editable ones. */
  editAll?: boolean;
  /** Receives only what changed. A `null` value goes back to the default. */
  onSave: (changes: Record<string, unknown>) => Promise<unknown>;
  saving?: boolean;
  error?: Error | null;
}

type Values = Record<string, unknown>;

/**
 * Edits one module's settings. The form is built from what the server says about each setting (its default, allowed values and
 * whether the tenant may change it), so a new setting appears here without any frontend change.
 */
export function SettingsForm({ view, labels = {}, editAll = false, onSave, saving, error }: SettingsFormProps) {
  const { t } = useTranslation();
  const initial = view.values as Values;
  // What the person typed, over the server's values. A new view from the server (after a save) starts clean.
  const [edits, setEdits] = useState<{ view: SettingsView; values: Values }>({ view, values: {} });
  if (edits.view !== view) setEdits({ view, values: {} });
  const values = { ...initial, ...(edits.view === view ? edits.values : {}) };
  const apiError = error instanceof ApiError ? error : undefined;
  const overrides = view.overrides as Values;

  if (view.settings.length === 0) return <Typography color="text.secondary">{t("common:settingsForm.noSettings")}</Typography>;

  const changes = Object.fromEntries(
    Object.entries(values).filter(([name, value]) => JSON.stringify(value) !== JSON.stringify(initial[name])),
  );

  async function submit(event: FormEvent) {
    event.preventDefault();
    await onSave(changes);
  }

  return (
    <form onSubmit={submit} noValidate>
      <Stack spacing={2.5} sx={{ maxWidth: 560 }}>
        {view.settings.map((setting) => {
          const name = setting.name;
          const editable = editAll || setting.tenantEditable;
          const label = labels[name]?.label ?? name;
          const fieldError = apiError?.forField(name)?.message;
          const help = fieldError ?? (editable ? labels[name]?.help : t("common:settingsForm.readOnly"));
          const value = values[name];
          const set = (next: unknown) => setEdits((e) => ({ view: e.view, values: { ...e.values, [name]: next } }));
          const overridden = name in overrides;
          const reset =
            editable && overridden ? (
              <Tooltip title={t("common:settingsForm.resetToDefault")}>
                <IconButton
                  aria-label={`${t("common:settingsForm.resetToDefault")}: ${label}`}
                  onClick={() => void onSave({ [name]: null })}
                  size="small"
                >
                  <RestartAlt fontSize="small" />
                </IconButton>
              </Tooltip>
            ) : null;

          if (typeof setting.default === "boolean") {
            return (
              <Stack key={name} direction="row" sx={{ alignItems: "center" }}>
                <FormControlLabel
                  control={<Switch checked={value === true} onChange={(e) => set(e.target.checked)} disabled={!editable} />}
                  label={label}
                />
                {reset}
              </Stack>
            );
          }
          const options = setting.allowedValues;
          return (
            <Stack key={name} direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
              <TextField
                fullWidth
                select={Boolean(options)}
                type={typeof setting.default === "number" ? "number" : "text"}
                label={label}
                value={value ?? ""}
                onChange={(e) =>
                  set(typeof setting.default === "number" && e.target.value !== "" ? Number(e.target.value) : e.target.value)
                }
                disabled={!editable}
                error={Boolean(fieldError)}
                helperText={help ?? t("common:settingsForm.default", { value: String(setting.default ?? "") })}
              >
                {options?.map((option) => (
                  <MenuItem key={option} value={option}>
                    {option}
                  </MenuItem>
                ))}
              </TextField>
              {reset}
            </Stack>
          );
        })}
        <div>
          <Button type="submit" variant="contained" disabled={saving || Object.keys(changes).length === 0}>
            {t("common:actions.save")}
          </Button>
        </div>
      </Stack>
    </form>
  );
}
