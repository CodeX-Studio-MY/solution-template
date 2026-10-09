import type { PurchaseRequestSchemas } from "@acme/api-clients";

type Schemas = PurchaseRequestSchemas["schemas"];

export type RequestDto = Schemas["RequestDto"];
export type RequestStatus = Schemas["RequestStatus"];
export type SaveRequestInput = Schemas["SaveRequestInput"];
export type LineInput = Schemas["LineInput"];
