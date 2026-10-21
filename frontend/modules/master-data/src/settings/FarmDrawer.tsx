import { useState, type FormEvent, type ReactNode } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
import Drawer from "@mui/material/Drawer";
import FormControlLabel from "@mui/material/FormControlLabel";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import Close from "@mui/icons-material/Close";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap } from "@farm360/web-core";
import { ProblemAlert } from "@farm360/ui-kit";
import { client } from "../client";
import { farmTypes, type FarmDto } from "../types";
import { emptyFarmForm, toBody, toForm, type FarmForm } from "./farmForm";

interface Props {
  open: boolean;
  /** The farm to edit; none to add one. */
  farm: FarmDto | undefined;
  onClose: () => void;
  onSaved: () => void;
}

/**
 * Add or edit a farm, in a drawer over the list (full screen on a phone). Changes carry the version the person opened (If-Match).
 * The form starts from `farm` when the drawer mounts: give it a new `key` each time it opens.
 */
export function FarmDrawer({ open, farm, onClose, onSaved }: Props) {
  const { t } = useTranslation("master-data");
  const [form, setForm] = useState<FarmForm>(() => (farm ? toForm(farm) : emptyFarmForm));
  const [error, setError] = useState<Error | undefined>(undefined);
  const [saving, setSaving] = useState(false);

  const apiError = error instanceof ApiError ? error : undefined;
  // A taken code is a 409 (not a field error); the server's message is already in the person's language.
  const codeInUse = apiError?.is("master-data.code-in-use") ?? false;
  const fieldError = (field: string) => (field === "code" && codeInUse ? apiError?.message : apiError?.forField(field)?.message);
  const set = <K extends keyof FarmForm>(key: K, value: FarmForm[K]) => setForm((f) => ({ ...f, [key]: value }));

  const textField = (key: keyof FarmForm, extra: { required?: boolean; multiline?: boolean; type?: string; helper?: string } = {}) => (
    <TextField
      label={t(`fields.${key}`)}
      value={form[key] as string}
      onChange={(e) => set(key, e.target.value)}
      required={extra.required}
      multiline={extra.multiline}
      minRows={extra.multiline ? 2 : undefined}
      type={extra.type}
      slotProps={extra.type === "number" ? { htmlInput: { step: "any" } } : undefined}
      error={fieldError(key) !== undefined}
      helperText={fieldError(key) ?? extra.helper}
      fullWidth
    />
  );

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(undefined);
    try {
      const body = toBody(form);
      if (farm) {
        await unwrap(
          client.PUT("/api/master-data/v1/farms/{id}", { params: { path: { id: farm.id }, header: { "If-Match": farm.eTag } }, body }),
        );
      } else {
        await unwrap(client.POST("/api/master-data/v1/farms", { body }));
      }
      onSaved();
    } catch (e) {
      setError(e as Error);
    } finally {
      setSaving(false);
    }
  }

  // Field problems show on their fields; anything else (a stale version, a lost connection) above the form.
  const generalError = apiError && (apiError.fieldErrors.length > 0 || codeInUse) ? undefined : error;
  const title = farm ? t("form.editTitle") : t("form.addTitle");

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: "100%", sm: 480 } }, component: "aside", "aria-label": title } as object }}
    >
      <Box component="form" onSubmit={save} noValidate sx={{ display: "flex", flexDirection: "column", height: "100%" }}>
        <Stack direction="row" sx={{ alignItems: "center", px: 3, py: 2 }}>
          <Typography variant="h2" component="h2" sx={{ flexGrow: 1 }}>
            {title}
          </Typography>
          <IconButton aria-label={t("form.close")} onClick={onClose} edge="end">
            <Close />
          </IconButton>
        </Stack>
        <Divider />

        <Stack spacing={3} sx={{ px: 3, py: 2, overflowY: "auto", flexGrow: 1 }}>
          <ProblemAlert error={generalError} />

          <Section title={t("form.general")}>
            {textField("name", { required: true })}
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              {textField("code")}
              <TextField
                select
                label={t("fields.farmType")}
                value={form.farmType}
                onChange={(e) => set("farmType", e.target.value as FarmForm["farmType"])}
                error={fieldError("farmType") !== undefined}
                helperText={fieldError("farmType")}
                fullWidth
              >
                {farmTypes.map((type) => (
                  <MenuItem key={type} value={type}>
                    {t(`farmTypes.${type}`)}
                  </MenuItem>
                ))}
              </TextField>
            </Stack>
            {textField("globalGapNumber", { helper: t("form.globalGapNumberHelp") })}
            {textField("description", { multiline: true })}
          </Section>

          <Section title={t("form.locationAndSize")}>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              {textField("latitude", { type: "number" })}
              {textField("longitude", { type: "number" })}
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {t("form.locationHelp")}
            </Typography>
            {textField("areaHectares", { type: "number" })}
          </Section>

          <Section title={t("form.contact")}>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              {textField("contactPerson")}
              {textField("contactPhone", { type: "tel" })}
            </Stack>
            {textField("address", { multiline: true })}
          </Section>

          <Section title={t("form.status")}>
            <FormControlLabel
              control={<Switch checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />}
              label={t("fields.isActive")}
            />
            <FormControlLabel
              control={<Switch checked={form.isExternal} onChange={(e) => set("isExternal", e.target.checked)} />}
              label={t("fields.isExternal")}
            />
            {farm ? textField("orderSequence", { type: "number", helper: t("form.orderHelp") }) : null}
          </Section>
        </Stack>

        <Divider />
        <Stack direction="row" spacing={1} sx={{ justifyContent: "flex-end", px: 3, py: 2 }}>
          <Button onClick={onClose}>{t("form.cancel")}</Button>
          <Button type="submit" variant="contained" disabled={saving}>
            {t("form.save")}
          </Button>
        </Stack>
      </Box>
    </Drawer>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Stack component="fieldset" spacing={2} sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
      <Typography component="legend" variant="overline" color="text.secondary" sx={{ p: 0, mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Stack>
  );
}
