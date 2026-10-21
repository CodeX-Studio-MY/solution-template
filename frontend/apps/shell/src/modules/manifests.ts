import { createContext, useContext } from "react";
import type { ModuleManifest } from "@saaskit/web-core";

/** The modules loaded for this tenant, for pages that need to know which modules have a frontend. */
export const ManifestsContext = createContext<readonly ModuleManifest[]>([]);

export function useManifests(): readonly ModuleManifest[] {
  return useContext(ManifestsContext);
}
