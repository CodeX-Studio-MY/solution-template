export * from "./generated/platform";
export { errorCodes, type ErrorCode } from "./generated/errors";
export { featureFlags, features, type FeatureKey } from "./generated/features";
export { wellKnownCustomFields } from "./generated/custom-fields";

export { api, apiJson, apiPath, unwrap, unwrapEmpty, getJson, platformFetch, signInUrl, setRedirect, loginPath } from "./http/client";
export { ApiError, readProblem, type FieldError, type ProblemDetails } from "./http/problem";

export { useSessionStore, useSession, useModuleRole, toSession, type Session } from "./session/sessionStore";
export {
  useTenantStore,
  useTenant,
  useTenantSettings,
  useFeature,
  type Tenant,
  type TenantSettings,
  type Branding,
} from "./tenant/tenantStore";

export { atLeast, canAccess, parseRoles, isModuleKey, type ModuleRoles } from "./access/roles";
export { RequireModuleRole, useRouteAccess } from "./access/guards";

export { eventBus, type AppEvents } from "./events/eventBus";

export { Slot, useSlotHasExtensions, registerExtensions, unregisterExtensions } from "./slots/Slot";
export type { SlotProps, SlotName } from "./slots/slotProps";

export {
  formatMoney,
  formatNumber,
  formatDate,
  formatDateTime,
  toNumber,
  useFormatters,
  type Money,
  type FormatContext,
} from "./format/formatters";

export { initI18n, getI18n, addNamespace, type NamespaceResources } from "./i18n/i18n";
export { useTranslation } from "react-i18next";

export type { ModuleManifest, ModuleRoute, MenuItem, SettingsPage, ExtensionDefinition, SlotExtension } from "./module/contract";

export { queryClient, createPlatformQueryClient, PlatformQueryProvider } from "./query/queryClient";
export { useApiQuery, useApiMutation, type ApiMutationOptions } from "./query/useApi";
export {
  useMaster,
  masterKey,
  invalidateMaster,
  type Master,
  type MasterItem,
  type MasterOption,
  type MasterOptions,
  type ListPath,
} from "./query/useMaster";
export { useSnackbar, useSnackbarStore, snackbar, type Snack, type Snackbar, type Severity } from "./feedback/snackbar";
export {
  useDialog,
  useDialogStore,
  dialog,
  type Dialog,
  type DialogRequest,
  type ConfirmOptions,
  type AlertOptions,
} from "./feedback/dialog";
