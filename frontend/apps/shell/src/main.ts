// Module Federation needs an async boundary before the first import of a shared package (react, MUI, ...), so the shell
// starts from a dynamic import. Everything real is in bootstrap.tsx.
void import("./bootstrap");
