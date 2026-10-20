import { api } from "@farm360/web-core";

/** The typed client for the master-data service: paths, bodies and responses come from its OpenAPI document (scripts/generate). */
export const client = api("master-data");
