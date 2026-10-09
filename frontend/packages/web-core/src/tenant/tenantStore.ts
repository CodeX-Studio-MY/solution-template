import { create } from "zustand";
import { platformDefaults } from "../generated/platform";
import { getJson } from "../http/client";

/** Branding a tenant may override. Anything missing falls back to the platform default. */
export interface Branding {
  primaryColor?: string;
  logoUrl?: string;
}

/** What `/bff/config` returns: the tenant and the platform defaults. No environment values are baked into the build. */
interface ConfigResponse {
  tenant: { slug: string; name: string; timeZone: string; currency: string; locale: string } | null;
  defaults: { timeZone: string; currency: string; locales: string[] };
  modules: { baseUrl: string };
  apiPrefix: string;
}

/** The tenant's own settings: branding, feature flags and the values the UI needs. All optional; a new tenant works with none. */
export interface TenantSettings {
  branding: Branding;
  features: Record<string, boolean>;
  values: Record<string, unknown>;
}

export interface Tenant {
  slug: string;
  name: string;
  timeZone: string;
  currency: string;
  locale: string;
}

const emptySettings: TenantSettings = { branding: {}, features: {}, values: {} };

interface TenantState {
  status: "loading" | "ready";
  tenant: Tenant;
  settings: TenantSettings;
  load: () => Promise<void>;
  setSettings: (settings: Partial<TenantSettings>) => void;
}

const fallbackTenant: Tenant = {
  slug: "",
  name: "",
  timeZone: platformDefaults.timeZone,
  currency: platformDefaults.currency,
  locale: platformDefaults.locales[0],
};

export const useTenantStore = create<TenantState>((set) => ({
  status: "loading",
  tenant: fallbackTenant,
  settings: emptySettings,
  load: async () => {
    const config = await getJson<ConfigResponse>("/bff/config", { allowUnauthenticated: true });
    const tenant = config?.tenant ? { ...config.tenant } : fallbackTenant;
    set({ tenant });
    // The tenant's settings come from the tenancy service. Until a tenant has any (or the call fails) the defaults apply.
    const settings = await loadSettings();
    set({ status: "ready", settings });
  },
  setSettings: (settings) => set((state) => ({ settings: { ...state.settings, ...settings } })),
}));

async function loadSettings(): Promise<TenantSettings> {
  try {
    const response = await fetch(`/api/tenancy/v1/me/settings`, { credentials: "same-origin", headers: { Accept: "application/json" } });
    if (!response.ok) return emptySettings;
    const body = (await response.json()) as Partial<TenantSettings>;
    return { branding: body.branding ?? {}, features: body.features ?? {}, values: body.values ?? {} };
  } catch {
    return emptySettings;
  }
}

export function useTenant(): Tenant {
  return useTenantStore((state) => state.tenant);
}

export function useTenantSettings(): TenantSettings {
  return useTenantStore((state) => state.settings);
}

/** Whether a feature flag is on for this tenant. Unknown flags are off. */
export function useFeature(key: string): boolean {
  return useTenantStore((state) => state.settings.features[key] === true);
}
