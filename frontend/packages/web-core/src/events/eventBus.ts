/**
 * Everything modules can tell each other. Modules never import each other; they share only this list, which lives here,
 * and they react to what they care about. Add an event by adding a line, named `{module-key}.{what-happened}`, for example
 * `"goods-receipt.changed": { receiptId: string };`.
 */
export interface AppEvents {
  /** Ask the shell to go somewhere (modules link to each other by URL, never by import). */
  navigate: { to: string };
  /** Show a short message (a snackbar) in the shell. */
  notify: { message: string; severity?: "success" | "info" | "warning" | "error" };
}

type Handler<K extends keyof AppEvents> = (payload: AppEvents[K]) => void;

class EventBus {
  private readonly handlers = new Map<keyof AppEvents, Set<Handler<keyof AppEvents>>>();

  on<K extends keyof AppEvents>(event: K, handler: Handler<K>): () => void {
    const set = this.handlers.get(event) ?? new Set();
    set.add(handler as Handler<keyof AppEvents>);
    this.handlers.set(event, set);
    return () => set.delete(handler as Handler<keyof AppEvents>);
  }

  emit<K extends keyof AppEvents>(event: K, payload: AppEvents[K]): void {
    for (const handler of this.handlers.get(event) ?? []) {
      try {
        handler(payload);
      } catch (error) {
        // One listener's failure never stops the others or the emitter.
        console.error(`Handler for "${event}" failed`, error);
      }
    }
  }
}

/** The one bus. Shared as a singleton through Module Federation, with web-core itself. */
export const eventBus = new EventBus();
