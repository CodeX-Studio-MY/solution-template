import type { ComponentType } from "react";
import type { ModuleKey, Role } from "../generated/platform";
import type { NamespaceResources } from "../i18n/i18n";
import type { SlotName, SlotProps } from "../slots/slotProps";

/** A route a module contributes. `lazy` loads the page on demand and exposes it as `Component`, like React Router's own `lazy`. */
export interface ModuleRoute {
  path?: string;
  index?: boolean;
  lazy: () => Promise<{ Component: ComponentType }>;
  children?: ModuleRoute[];
}

/** A sidebar entry. `label` is an i18n key; `path` is relative to the module's `basePath`. */
export interface MenuItem {
  label: string;
  path: string;
  /** The name of an icon in `@farm360/ui-kit`'s icon set. */
  icon: string;
  requiredRole: Role;
}

/**
 * A page in the shell's Settings area, opened from the gear in the top bar (never the sidebar): base data and configuration.
 * The shell mounts it at `/settings{basePath}/{path}`.
 */
export interface SettingsPage {
  id: string;
  /** i18n key (module namespace) of the section the page is listed under, for example "Master". Pages sharing a label share a section. */
  group: string;
  /** i18n key (module namespace) of the page's name in the menu and the breadcrumb. */
  label: string;
  /** Relative to `/settings{basePath}`. */
  path: string;
  requiredRole: Role;
  lazy: () => Promise<{ Component: ComponentType }>;
}

/** A component this module contributes to a slot on another module's page. Neither module imports the other. */
export interface SlotExtension<S extends SlotName> {
  slot: S;
  /** Receives exactly the props the slot declares in `@farm360/web-core`. */
  component: () => Promise<{ default: ComponentType<SlotProps[S]> }>;
  /** The lowest role in THIS module that sees the extension. */
  requiredRole: Role;
}

/** An extension for any one slot: its component's props must be that slot's props. */
export type ExtensionDefinition = { [S in SlotName]: SlotExtension<S> }[SlotName];

/** What each module exposes, as `./manifest`. */
export interface ModuleManifest {
  /** The module key (always the same as the federated remote's name). */
  id: ModuleKey | (string & {});
  /** Always `"/" + id`. */
  basePath: `/${string}`;
  routes: ModuleRoute[];
  menu: MenuItem[];
  settings?: SettingsPage[];
  extensions?: ExtensionDefinition[];
  i18n: { namespace: string; resources?: NamespaceResources };
}
