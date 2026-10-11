import { Component, lazy, Suspense, type ComponentType, type ErrorInfo, type LazyExoticComponent, type ReactNode } from "react";
import { create } from "zustand";
import type { ModuleKey } from "../generated/platform";
import { useSessionStore } from "../session/sessionStore";
import { canAccess } from "../access/roles";
import type { ExtensionDefinition } from "../module/contract";
import type { SlotName, SlotProps } from "./slotProps";

interface Registered {
  moduleId: string;
  definition: ExtensionDefinition;
  component: LazyExoticComponent<ComponentType<unknown>>;
}

interface SlotRegistry {
  extensions: Registered[];
  register: (moduleId: string, definitions: readonly ExtensionDefinition[]) => void;
  unregister: (moduleId: string) => void;
}

const useSlotRegistry = create<SlotRegistry>((set) => ({
  extensions: [],
  register: (moduleId, definitions) =>
    set((state) => ({
      extensions: [
        ...state.extensions.filter((e) => e.moduleId !== moduleId),
        ...definitions.map((definition) => ({
          moduleId,
          definition,
          component: lazy(definition.component as () => Promise<{ default: ComponentType<unknown> }>),
        })),
      ],
    })),
  unregister: (moduleId) => set((state) => ({ extensions: state.extensions.filter((e) => e.moduleId !== moduleId) })),
}));

/** The shell registers each loaded module's extensions here. */
export function registerExtensions(moduleId: ModuleKey | string, definitions: readonly ExtensionDefinition[]): void {
  useSlotRegistry.getState().register(moduleId, definitions);
}

export function unregisterExtensions(moduleId: ModuleKey | string): void {
  useSlotRegistry.getState().unregister(moduleId);
}

/** A failure in an extension is contained: the host page keeps working. */
class ExtensionBoundary extends Component<{ children: ReactNode; label: string }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`Extension "${this.props.label}" failed`, error, info.componentStack);
  }

  override render() {
    return this.state.failed ? null : this.props.children;
  }
}

/** Whether anything the person may see fills the slot, so a host can leave out a heading that would sit over nothing. */
export function useSlotHasExtensions(name: SlotName): boolean {
  const extensions = useSlotRegistry((state) => state.extensions);
  const moduleRoles = useSessionStore((state) => state.session?.moduleRoles ?? {});
  return extensions.some((e) => e.definition.slot === name && canAccess(moduleRoles, e.moduleId, e.definition.requiredRole));
}

interface SlotComponentProps<S extends SlotName> {
  name: S;
  props: SlotProps[S];
}

/**
 * A named place in a module's page that other modules fill. With nothing registered (the tenant doesn't have the module that
 * fills it) it renders nothing. Each extension loads lazily and runs inside its own error boundary.
 */
export function Slot<S extends SlotName>({ name, props }: SlotComponentProps<S>) {
  const extensions = useSlotRegistry((state) => state.extensions);
  const moduleRoles = useSessionStore((state) => state.session?.moduleRoles ?? {});
  const matching = extensions.filter((e) => e.definition.slot === name && canAccess(moduleRoles, e.moduleId, e.definition.requiredRole));
  if (matching.length === 0) return null;
  return (
    <>
      {matching.map((extension) => {
        const Extension = extension.component as unknown as ComponentType<SlotProps[S]>;
        return (
          <ExtensionBoundary key={`${extension.moduleId}:${name}`} label={`${extension.moduleId}:${name}`}>
            <Suspense fallback={null}>
              <Extension {...props} />
            </Suspense>
          </ExtensionBoundary>
        );
      })}
    </>
  );
}
