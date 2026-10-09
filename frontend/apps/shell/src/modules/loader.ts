import { createInstance, getInstance } from "@module-federation/enhanced/runtime";
import { addNamespace, apiJson, registerExtensions, type ModuleManifest } from "@acme/web-core";

/** One entry of `GET /api/tenancy/v1/me/modules`. */
interface ManifestEntry {
  id: string;
  entry: string;
}

export interface LoadedModule {
  manifest: ModuleManifest;
}

export interface ModuleFailure {
  id: string;
  error: Error;
}

export interface LoadResult {
  modules: LoadedModule[];
  failures: ModuleFailure[];
}

type Loader = (id: string) => Promise<{ default: ModuleManifest } | undefined | null>;

/**
 * Asks the tenancy service which modules this tenant has, registers them as Module Federation remotes at runtime, and loads each
 * one's manifest. A module that fails to load is reported and skipped: it never takes the app down.
 */
export async function loadModules(load: Loader = loadRemoteManifest, entries?: ManifestEntry[]): Promise<LoadResult> {
  const list = entries ?? (await apiJson<{ modules: ManifestEntry[] }>("tenancy", "v1/me/modules")).modules;
  registerRemotes(list);

  const settled = await Promise.allSettled(list.map(async (entry) => ({ entry, module: await load(entry.id) })));
  const modules: LoadedModule[] = [];
  const failures: ModuleFailure[] = [];
  settled.forEach((result, index) => {
    const id = list[index]!.id;
    if (result.status === "rejected") {
      failures.push({ id, error: result.reason as Error });
      return;
    }
    const manifest = result.value.module?.default;
    if (!manifest || manifest.id !== id || manifest.basePath !== `/${id}`) {
      failures.push({ id, error: new Error(`Module "${id}" did not expose a valid manifest.`) });
      return;
    }
    if (manifest.i18n.resources) addNamespace(manifest.i18n.namespace, manifest.i18n.resources);
    registerExtensions(manifest.id, manifest.extensions ?? []);
    modules.push({ manifest });
  });
  return { modules, failures };
}

function registerRemotes(entries: ManifestEntry[]): void {
  const remotes = entries.map((e) => ({ name: e.id, entry: e.entry }));
  const instance = getInstance() ?? createInstance({ name: "shell", remotes: [] });
  instance.registerRemotes(remotes, { force: true });
}

async function loadRemoteManifest(id: string) {
  const instance = getInstance();
  if (!instance) throw new Error("Module Federation is not initialised.");
  return instance.loadRemote<{ default: ModuleManifest }>(`${id}/manifest`);
}
