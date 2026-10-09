import { create } from "zustand";
import type { ModuleKey, Role } from "../generated/platform";
import { getJson } from "../http/client";
import { parseRoles, type ModuleRoles } from "../access/roles";

/** What `/bff/user` returns for a signed-in person. */
interface SessionResponse {
  subject: string;
  tenant: string;
  principalType: string;
  roles: string[];
}

export interface Session {
  subject: string;
  tenant: string;
  principalType: string;
  /** The raw `{module-key}:{role}` entries. The UI uses them only to show or hide menus and routes. */
  roles: string[];
  moduleRoles: ModuleRoles;
}

interface SessionState {
  status: "loading" | "ready";
  session: Session | null;
  /** Loads the session; a 401 sends the browser to sign in. */
  load: () => Promise<void>;
  set: (session: Session | null) => void;
}

export const useSessionStore = create<SessionState>((set) => ({
  status: "loading",
  session: null,
  load: async () => {
    const response = await getJson<SessionResponse>("/bff/user");
    set({ status: "ready", session: response ? toSession(response) : null });
  },
  set: (session) => set({ status: "ready", session }),
}));

export function toSession(response: SessionResponse): Session {
  return { ...response, moduleRoles: parseRoles(response.roles) };
}

/** The signed-in person, or null while loading. */
export function useSession(): Session | null {
  return useSessionStore((state) => state.session);
}

/** The person's role in a module, or null if they have none. */
export function useModuleRole(module: ModuleKey | string): Role | null {
  return useSessionStore((state) => state.session?.moduleRoles[module] ?? null);
}
