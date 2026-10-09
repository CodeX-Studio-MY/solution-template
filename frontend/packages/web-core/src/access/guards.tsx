import type { ReactNode } from "react";
import type { ModuleKey, Role } from "../generated/platform";
import { useSessionStore } from "../session/sessionStore";
import { canAccess } from "./roles";

/** Whether the signed-in person has at least `role` in `module`. For showing or hiding routes and menus. */
export function useRouteAccess(module: ModuleKey | string, role: Role): boolean {
  return useSessionStore((state) => (state.session ? canAccess(state.session.moduleRoles, module, role) : false));
}

interface RequireModuleRoleProps {
  module: ModuleKey | string;
  role: Role;
  children: ReactNode;
  /** Shown instead when the person lacks the role. Default: nothing. */
  fallback?: ReactNode;
}

/** Renders its children only for people with at least `role` in `module` (the same rule the API applies). */
export function RequireModuleRole({ module, role, children, fallback = null }: RequireModuleRoleProps) {
  return useRouteAccess(module, role) ? <>{children}</> : <>{fallback}</>;
}
