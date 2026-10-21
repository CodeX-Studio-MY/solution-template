import { toNumber } from "@farm360/web-core";
import type { FarmDto, FarmType, SaveFarmInput } from "../types";

/** The farm form's state: what the inputs hold, as text (numbers included) so a half-typed value is never lost. */
export interface FarmForm {
  name: string;
  code: string;
  farmType: FarmType;
  globalGapNumber: string;
  description: string;
  latitude: string;
  longitude: string;
  areaHectares: string;
  contactPerson: string;
  contactPhone: string;
  address: string;
  isActive: boolean;
  isExternal: boolean;
  orderSequence: string;
}

export const emptyFarmForm: FarmForm = {
  name: "",
  code: "",
  farmType: "field",
  globalGapNumber: "",
  description: "",
  latitude: "",
  longitude: "",
  areaHectares: "",
  contactPerson: "",
  contactPhone: "",
  address: "",
  isActive: true,
  isExternal: true,
  orderSequence: "",
};

const text = (value: string | null | undefined) => value ?? "";
const numberText = (value: number | string | null | undefined) => (value === null || value === undefined ? "" : String(value));

/** The form for an existing farm. */
export function toForm(farm: FarmDto): FarmForm {
  return {
    name: farm.name,
    code: text(farm.code),
    farmType: farm.farmType,
    globalGapNumber: text(farm.globalGapNumber),
    description: text(farm.description),
    latitude: numberText(farm.latitude),
    longitude: numberText(farm.longitude),
    areaHectares: numberText(farm.areaHectares),
    contactPerson: text(farm.contactPerson),
    contactPhone: text(farm.contactPhone),
    address: text(farm.address),
    isActive: farm.isActive,
    isExternal: farm.isExternal,
    orderSequence: numberText(farm.orderSequence),
  };
}

const optionalText = (value: string) => (value.trim() === "" ? null : value.trim());

/** A number from a text input: blank is "not set"; anything unparsable goes to the server as typed, which explains the problem. */
const optionalNumber = (value: string): number | string | null => {
  const trimmed = value.trim();
  if (trimmed === "") return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : trimmed;
};

/**
 * The request body for the form. PUT replaces the whole farm, so every field is always sent. The order is sent only when given
 * (the server ignores it on create and keeps the farm's place when it is left out on update).
 */
export function toBody(form: FarmForm): SaveFarmInput {
  const order = optionalNumber(form.orderSequence);
  return {
    name: form.name.trim(),
    code: optionalText(form.code),
    farmType: form.farmType,
    globalGapNumber: optionalText(form.globalGapNumber),
    description: optionalText(form.description),
    latitude: optionalNumber(form.latitude),
    longitude: optionalNumber(form.longitude),
    areaHectares: optionalNumber(form.areaHectares),
    contactPerson: optionalText(form.contactPerson),
    contactPhone: optionalText(form.contactPhone),
    address: optionalText(form.address),
    isActive: form.isActive,
    isExternal: form.isExternal,
    ...(order === null ? {} : { orderSequence: order }),
  };
}

/** The full body of a farm as it is, with some fields changed: for actions such as "move up" that change one thing through PUT. */
export function bodyFromDto(farm: FarmDto, changes: Partial<SaveFarmInput> = {}): SaveFarmInput {
  return {
    name: farm.name,
    code: farm.code,
    farmType: farm.farmType,
    globalGapNumber: farm.globalGapNumber,
    description: farm.description,
    latitude: farm.latitude,
    longitude: farm.longitude,
    areaHectares: farm.areaHectares,
    contactPerson: farm.contactPerson,
    contactPhone: farm.contactPhone,
    address: farm.address,
    isActive: farm.isActive,
    isExternal: farm.isExternal,
    orderSequence: toNumber(farm.orderSequence),
    ...changes,
  };
}
