import { describe, expect, it } from "vitest";
import type { FarmDto } from "../types";
import { bodyFromDto, emptyFarmForm, toBody, toForm } from "./farmForm";

const farm: FarmDto = {
  id: "f1",
  name: "Ladang Hijau",
  code: "FRM-001",
  farmType: "greenhouse",
  globalGapNumber: null,
  description: null,
  address: "Cameron Highlands",
  contactPerson: null,
  contactPhone: null,
  areaHectares: 12.5,
  latitude: 4.4721,
  longitude: 101.3801,
  orderSequence: 3,
  isExternal: false,
  isActive: true,
  createdOn: "2026-10-21T01:00:00Z",
  eTag: '"v1"',
};

describe("farmForm", () => {
  it("sends blank text and numbers as not set, and trims text", () => {
    const body = toBody({ ...emptyFarmForm, name: "  Ladang  ", code: "   ", latitude: " ", areaHectares: "" });
    expect(body).toMatchObject({ name: "Ladang", code: null, latitude: null, longitude: null, areaHectares: null, farmType: "field" });
    expect(body).not.toHaveProperty("orderSequence");
  });

  it("parses numbers, and sends what it can't parse as typed so the server explains it", () => {
    const body = toBody({ ...emptyFarmForm, name: "F", latitude: "3.139", longitude: "abc", areaHectares: "12.50", orderSequence: "2" });
    expect(body).toMatchObject({ latitude: 3.139, longitude: "abc", areaHectares: 12.5, orderSequence: 2 });
  });

  it("round-trips a farm through the form", () => {
    expect(toBody(toForm(farm))).toEqual(bodyFromDto(farm));
  });

  it("keeps every field when one is changed", () => {
    const body = bodyFromDto(farm, { orderSequence: 4 });
    expect(body).toMatchObject({
      name: "Ladang Hijau",
      code: "FRM-001",
      latitude: 4.4721,
      address: "Cameron Highlands",
      isExternal: false,
      orderSequence: 4,
    });
  });
});
