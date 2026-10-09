import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, eventBus, toNumber, unwrap, useResource } from "@acme/web-core";
import { CustomFieldsSection, MoneyText, PageHeader, ProblemAlert, type CustomFieldValues } from "@acme/ui-kit";
import { basePath } from "../basePath";
import { requests } from "../client";
import type { RequestDto } from "../types";

interface LineState {
  item: string;
  quantity: string;
  unitPrice: string;
}

const emptyLine: LineState = { item: "", quantity: "1", unitPrice: "" };

function toForm(request: RequestDto) {
  return {
    title: request.title,
    description: request.description ?? "",
    lines: request.lines.map((l) => ({ item: l.item, quantity: String(l.quantity), unitPrice: String(l.unitPrice) })),
  };
}

/** Create (`/new`) and edit (`/:id/edit`). The server validates everything; this shows what it says next to the field. */
export function Component() {
  const { t } = useTranslation("purchase-request");
  const { id } = useParams();
  const navigate = useNavigate();
  const existing = useResource(
    () => (id ? unwrap(requests.GET("/api/purchase-request/v1/requests/{id}", { params: { path: { id } } })) : Promise.resolve(undefined)),
    [id],
  );

  const [draft, setDraft] = useState<ReturnType<typeof toForm> | undefined>(undefined);
  const [customDraft, setCustomFields] = useState<CustomFieldValues | undefined>(undefined);
  const customFields: CustomFieldValues = customDraft ?? existing.data?.customFields ?? {};
  const [error, setError] = useState<Error | undefined>(undefined);
  const [saving, setSaving] = useState(false);

  const form = draft ?? (existing.data ? toForm(existing.data) : { title: "", description: "", lines: [{ ...emptyLine }] });
  const apiError = error instanceof ApiError ? error : undefined;
  const fieldError = (field: string) => apiError?.forField(field)?.message;

  const update = (patch: Partial<typeof form>) => setDraft({ ...form, ...patch });
  const setLine = (index: number, patch: Partial<LineState>) =>
    update({ lines: form.lines.map((l, i) => (i === index ? { ...l, ...patch } : l)) });
  const total = form.lines.reduce((sum, l) => sum + (Number(l.quantity) || 0) * (Number(l.unitPrice) || 0), 0);

  async function save(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(undefined);
    const body = {
      title: form.title,
      description: form.description === "" ? null : form.description,
      lines: form.lines.map((l) => ({ item: l.item, quantity: Number(l.quantity), unitPrice: Number(l.unitPrice) })),
      customFields: Object.fromEntries(Object.entries(customFields).filter((entry): entry is [string, string] => entry[1] !== null)),
    };
    try {
      const saved = existing.data
        ? await unwrap(
            requests.PUT("/api/purchase-request/v1/requests/{id}", {
              params: { path: { id: existing.data.id }, header: { "If-Match": existing.data.eTag } },
              body,
            }),
          )
        : await unwrap(requests.POST("/api/purchase-request/v1/requests", { body }));
      eventBus.emit("purchase-request.changed", { requestId: saved.id });
      void navigate(`${basePath}/${saved.id}`);
    } catch (e) {
      setError(e as Error);
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={save} noValidate>
      <PageHeader title={existing.data ? t("form.editTitle", { number: existing.data.number }) : t("form.newTitle")} />
      <Stack spacing={2} sx={{ maxWidth: 800 }}>
        <ProblemAlert
          error={
            existing.error ?? (apiError && apiError.fieldErrors.length === 0 ? apiError : error instanceof ApiError ? undefined : error)
          }
        />
        <TextField
          label={t("fields.title")}
          required
          value={form.title}
          onChange={(e) => update({ title: e.target.value })}
          error={fieldError("title") !== undefined}
          helperText={fieldError("title")}
        />
        <TextField
          label={t("fields.description")}
          multiline
          minRows={2}
          value={form.description}
          onChange={(e) => update({ description: e.target.value })}
          error={fieldError("description") !== undefined}
          helperText={fieldError("description")}
        />

        <Typography variant="h3" component="h2">
          {t("fields.lines")}
        </Typography>
        {fieldError("lines") ? (
          <Typography color="error" role="alert">
            {fieldError("lines")}
          </Typography>
        ) : null}
        {form.lines.map((line, index) => (
          <Paper key={index} variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "flex-start" } }}>
              <TextField
                label={t("fields.item")}
                required
                value={line.item}
                onChange={(e) => setLine(index, { item: e.target.value })}
                error={fieldError(`lines[${index}].item`) !== undefined}
                helperText={fieldError(`lines[${index}].item`)}
                sx={{ flex: 3 }}
              />
              <TextField
                label={t("fields.quantity")}
                required
                type="number"
                value={line.quantity}
                onChange={(e) => setLine(index, { quantity: e.target.value })}
                error={fieldError(`lines[${index}].quantity`) !== undefined}
                helperText={fieldError(`lines[${index}].quantity`)}
                sx={{ flex: 1 }}
              />
              <TextField
                label={t("fields.unitPrice")}
                required
                type="number"
                value={line.unitPrice}
                onChange={(e) => setLine(index, { unitPrice: e.target.value })}
                error={fieldError(`lines[${index}].unitPrice`) !== undefined}
                helperText={fieldError(`lines[${index}].unitPrice`)}
                sx={{ flex: 1 }}
              />
              <IconButton
                aria-label={t("form.removeLine", { n: index + 1 })}
                onClick={() => update({ lines: form.lines.filter((_, i) => i !== index) })}
                disabled={form.lines.length === 1}
              >
                <DeleteIcon />
              </IconButton>
            </Stack>
          </Paper>
        ))}
        <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center" }}>
          <Button startIcon={<AddIcon />} onClick={() => update({ lines: [...form.lines, { ...emptyLine }] })}>
            {t("form.addLine")}
          </Button>
          <Typography variant="h3" component="p">
            {t("fields.total")}: <MoneyText amount={toNumber(total)} />
          </Typography>
        </Stack>

        <CustomFieldsSection
          entity="purchase-request"
          values={customFields}
          onChange={setCustomFields}
          errors={apiError?.fieldErrors ?? []}
          disabled={saving}
        />

        <Stack direction="row" spacing={1}>
          <Button type="submit" variant="contained" disabled={saving}>
            {t("form.save")}
          </Button>
          <Button onClick={() => void navigate(-1)} disabled={saving}>
            {t("form.cancel")}
          </Button>
        </Stack>
      </Stack>
    </form>
  );
}
