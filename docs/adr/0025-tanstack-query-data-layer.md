# 0025. TanStack Query as the shared data layer

Status: accepted

## Context

Every screen loads lists, changes them and refreshes what changed. The template started with a small `useResource` hook: no cache, so
the same list was fetched again on every page, and nothing told one screen that another had changed its data. Master data (farms,
students, brands) is read by many screens in many modules, and the shell and every module run in one page (ADR 0022).

## Decision

`@tanstack/react-query` is the one data layer, shared as a Module Federation singleton with the shell and every module.

- `web-core` owns the single `QueryClient` (`queryClient`) and its defaults: 30 s fresh, retries only for network errors and 5xx, no
  refetch on window focus. The shell renders `PlatformQueryProvider` once; modules never create a client.
- Screens use `useApiQuery` and `useApiMutation`, thin wrappers that keep the platform's rules: errors are `ApiError`s, a mutation
  refreshes what it names and reports failures through the snackbar, and field errors stay with the form.
- `useMaster(service, path)` reads master data from any list endpoint, typed from the OpenAPI document, kept for 5 minutes under
  `["master", service, path, query]`. `masterKey`/`invalidateMaster` refresh it everywhere.
- Snackbars and dialogs are zustand stores in `web-core` (`useSnackbar`, `useDialog`), rendered by `FeedbackHost` from `ui-kit`.

## Alternative rejected

- **Keep `useResource` and add a cache to it.** Deduplication, stale times, invalidation by prefix and background refresh would be
  rewritten by hand, with fewer tests than the library has. Rejected.
- **SWR.** Similar, but mutations and invalidation by key prefix are weaker. Rejected.
- **A client per module.** Each module would cache the same master data again, and an edit in one module would not refresh another.
  Rejected.
