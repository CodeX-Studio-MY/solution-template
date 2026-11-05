import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import MenuItem from "@mui/material/MenuItem";
import TablePagination from "@mui/material/TablePagination";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useApiMutation } from "@saaskit/web-core";
import { FormDialog, tokens } from "@saaskit/ui-kit";
import { principalsKey, tenancy, tenantsKey, type PrincipalDetail, type PrincipalInput, type PrincipalType } from "../api";

/** A value that is copied and compared character by character: a serial number, an EUI, an address. */
export function Identifier({ children }: { children: string | null | undefined }) {
  return (
    <Box component="span" sx={{ fontFamily: tokens.operator.fontMono }}>
      {children}
    </Box>
  );
}

/** These tables aren't connected to sign-in yet, and an operator must not believe otherwise when revoking someone. */
export function DirectoryNotice() {
  const { t } = useTranslation("operator");
  return (
    <Alert severity="info" variant="outlined" sx={{ mb: 2 }}>
      {t("principals.notice")}
    </Alert>
  );
}

/** "LoRaWAN gateway", "External application", "User": the most specific name the row has. */
export function usePrincipalKind() {
  const { t } = useTranslation("operator");
  return (row: { type: PrincipalType; deviceType?: string | null; isExternal?: boolean | null }) => {
    if (row.type === "device" && row.deviceType) return t(`principals.deviceTypes.${row.deviceType}`);
    if (row.type === "application") return t(row.isExternal ? "principals.externalApplication" : "principals.internalApplication");
    return t(`principals.types.${row.type}`);
  };
}

export function Pager({
  count,
  page,
  pageSize,
  onPage,
}: {
  count: number;
  page: number;
  pageSize: number;
  onPage: (page: number, pageSize: number) => void;
}) {
  return (
    <TablePagination
      component="div"
      count={count}
      page={page - 1}
      rowsPerPage={pageSize}
      rowsPerPageOptions={[25, 50, 100]}
      onPageChange={(_, next) => onPage(next + 1, pageSize)}
      onRowsPerPageChange={(e) => onPage(1, Number(e.target.value))}
    />
  );
}

const blank = {
  displayName: "",
  email: "",
  deviceType: "lorawanGateway",
  identifier: "",
  manufacturer: "",
  model: "",
  isExternal: "true",
  ownerName: "",
  contactEmail: "",
};

/** Adds a principal, or edits one. The type comes first and decides the fields; it can't change afterwards. */
export function PrincipalFormDialog({
  existing,
  onClose,
  onCreated,
}: {
  existing?: PrincipalDetail;
  onClose: () => void;
  onCreated?: (id: string) => void;
}) {
  const { t } = useTranslation("operator");
  const [type, setType] = useState<PrincipalType>(existing?.type ?? "user");
  const [form, setForm] = useState({
    displayName: existing?.displayName ?? blank.displayName,
    email: existing?.email ?? blank.email,
    deviceType: existing?.deviceType ?? blank.deviceType,
    identifier: existing?.identifier ?? blank.identifier,
    manufacturer: existing?.manufacturer ?? blank.manufacturer,
    model: existing?.model ?? blank.model,
    isExternal: existing?.isExternal === false ? "false" : blank.isExternal,
    ownerName: existing?.ownerName ?? blank.ownerName,
    contactEmail: existing?.contactEmail ?? blank.contactEmail,
  });

  const save = useApiMutation(
    () => {
      const shared = { type, displayName: form.displayName };
      const body: PrincipalInput =
        type === "user"
          ? { ...shared, email: form.email }
          : type === "device"
            ? {
                ...shared,
                deviceType: form.deviceType as PrincipalInput["deviceType"],
                identifier: form.identifier,
                manufacturer: form.manufacturer,
                model: form.model,
              }
            : { ...shared, isExternal: form.isExternal === "true", ownerName: form.ownerName, contactEmail: form.contactEmail };
      return unwrap(
        existing
          ? tenancy.PUT("/api/tenancy/v1/operator/principals/{id}", {
              params: { path: { id: existing.principalId }, header: { "If-Match": existing.eTag } },
              body,
            })
          : tenancy.POST("/api/tenancy/v1/operator/principals", { body }),
      );
    },
    {
      invalidate: [principalsKey, tenantsKey],
      success: t(existing ? "principals.saved" : "principals.added"),
      onSuccess: (saved) => {
        onClose();
        if (!existing) onCreated?.(saved.principalId);
      },
    },
  );

  const fieldError = (name: string) => (save.error instanceof ApiError ? save.error.forField(name)?.message : undefined);
  const field = (name: keyof typeof form, required = true) => ({
    label: t(`principals.fields.${name}`),
    required,
    value: form[name],
    onChange: (e: { target: { value: string } }) => setForm((f) => ({ ...f, [name]: e.target.value })),
    error: Boolean(fieldError(name)),
    helperText: fieldError(name),
  });

  return (
    <FormDialog
      open
      title={t(existing ? "principals.editTitle" : "principals.addTitle")}
      submitLabel={t(existing ? "principals.save" : "principals.add")}
      busy={save.isPending}
      onSubmit={() => save.mutate()}
      onClose={onClose}
      maxWidth="sm"
    >
      {save.error && !(save.error instanceof ApiError && save.error.fieldErrors.length > 0) ? (
        <Alert severity="error">{save.error.message}</Alert>
      ) : null}
      <TextField
        select
        label={t("principals.fields.type")}
        value={type}
        disabled={Boolean(existing)}
        onChange={(e) => setType(e.target.value as PrincipalType)}
      >
        {(["user", "device", "application"] as const).map((value) => (
          <MenuItem key={value} value={value}>
            {t(`principals.types.${value}`)}
          </MenuItem>
        ))}
      </TextField>
      <TextField {...field("displayName")} autoFocus />
      {type === "user" ? <TextField {...field("email")} type="email" /> : null}
      {type === "device" ? (
        <>
          <TextField {...field("deviceType")} select>
            {(["iot", "lorawanGateway", "gateway"] as const).map((value) => (
              <MenuItem key={value} value={value}>
                {t(`principals.deviceTypes.${value}`)}
              </MenuItem>
            ))}
          </TextField>
          <TextField {...field("identifier")} />
          <TextField {...field("manufacturer", false)} />
          <TextField {...field("model", false)} />
        </>
      ) : null}
      {type === "application" ? (
        <>
          <TextField {...field("isExternal")} select>
            <MenuItem value="true">{t("principals.externalApplication")}</MenuItem>
            <MenuItem value="false">{t("principals.internalApplication")}</MenuItem>
          </TextField>
          <TextField {...field("ownerName")} />
          <TextField {...field("contactEmail", false)} type="email" />
        </>
      ) : null}
    </FormDialog>
  );
}
