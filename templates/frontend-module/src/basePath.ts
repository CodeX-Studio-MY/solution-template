/** Always `"/" + the module key` (the shell checks it). The one place the module spells its own key. */
export const moduleKey = "__KEY__" as const;
export const basePath = `/${moduleKey}` as const;
