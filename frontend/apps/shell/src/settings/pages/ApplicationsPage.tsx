import { PrincipalListPage } from "./PrincipalListPage";

/** Applications: external systems (an ERP, a partner) that call the API. */
export function Component() {
  return <PrincipalListPage kind="service" />;
}
