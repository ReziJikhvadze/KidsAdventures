import { apiRequest } from "./client";
import type { AdventureMapResponse, WorldResponse } from "./types";

export async function listWorldCatalogue(): Promise<WorldResponse[]> {
  return apiRequest<WorldResponse[]>("/api/worlds", { auth: false });
}

export async function listAdventureMaps(): Promise<AdventureMapResponse[]> {
  return apiRequest<AdventureMapResponse[]>("/api/worlds/maps");
}

export async function getAdventureMap(characterId: string): Promise<AdventureMapResponse> {
  return apiRequest<AdventureMapResponse>(`/api/worlds/maps/${characterId}`);
}
