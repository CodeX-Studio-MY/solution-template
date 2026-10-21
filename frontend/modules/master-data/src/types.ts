import type { MasterDataSchemas } from "@farm360/api-clients";

type Schemas = MasterDataSchemas["schemas"];

export type FarmDto = Schemas["FarmDto"];
export type SaveFarmInput = Schemas["SaveFarmInput"];
export type FarmType = Schemas["FarmType"];

/** The farm types in the order the form offers them. */
export const farmTypes: readonly FarmType[] = ["field", "greenhouse", "nursery"];
