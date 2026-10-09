/**
 * The action names the API puts in `allowedActions` (the backend's `RequestActions`). The UI shows a button when its action is
 * in the list the server returned and never works out for itself whether someone may approve.
 */
export const actions = {
  edit: "edit",
  delete: "delete",
  submit: "submit",
  approve: "approve",
  reject: "reject",
  uploadQuote: "uploadQuote",
} as const;

export type RequestAction = (typeof actions)[keyof typeof actions];
