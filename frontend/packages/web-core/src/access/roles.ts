import { roles as roleOrder, moduleKeys, type ModuleKey, type Role } from "../generated/platform";

/** A person's highest role in each module they have one in. */
export type ModuleRoles = Partial<Record<string, Role>>;

/** `true` if `held` is at least `required` (roles are ordered lowest to highest in platform.yaml). */
export function atLeast(held: Role | null | undefined, required: Role): boolean {
  if (!held) return false;
  return roleOrder.indexOf(held) >= roleOrder.indexOf(required);
}

/** Reads `{module-key}:{role}` claims into the highest role per module. Unknown entries are ignored. */
export function parseRoles(entries: readonly string[]): ModuleRoles {
  const result: Record<string, Role> = {};
  for (const entry of entries) {
    const separator = entry.lastIndexOf(":");
    if (separator <= 0) continue;
    const module = entry.slice(0, separator);
    const role = entry.slice(separator + 1) as Role;
    if (!roleOrder.includes(role)) continue;
    const current = result[module];
    if (!current || roleOrder.indexOf(role) > roleOrder.indexOf(current)) result[module] = role;
  }
  return result;
}

/** Whether the roles give at least `required` in `module`. Used to show or hide menus and routes; the API decides access. */
export function canAccess(moduleRoles: ModuleRoles, module: ModuleKey | string, required: Role): boolean {
  return atLeast(moduleRoles[module], required);
}

export function isModuleKey(value: string): value is ModuleKey {
  return (moduleKeys as readonly string[]).includes(value);
}
