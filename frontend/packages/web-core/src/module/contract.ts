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
  /** The name of an icon in `@acme/ui-kit`'s icon set. */
  icon: string;
  requiredRole: Role;
}

/** A page in the shell's Settings area. */
export interface SettingsPage {
  id: string;
  label: string;
  path: string;
  requiredRole: Role;
  lazy: () => Promise<{ Component: ComponentType }>;
}

/** A component this module contributes to a slot on another module's page. Neither module imports the other. */
export interface SlotExtension<S extends SlotName> {
  slot: S;
  /** Receives exactly the props the slot declares in `@acme/web-core`. */
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
