import { apiRequest, getApiBaseUrl, getToken, ApiError } from "./client";
import type { CharacterResponse, SaveCharacterInput } from "./types";

function toFormData(input: SaveCharacterInput): FormData {
  const form = new FormData();
  form.append("name", input.name);
  form.append("characterType", input.characterType);
  form.append("isPrimary", String(input.isPrimary));
  if (input.birthDate) form.append("birthDate", input.birthDate);
  if (input.gender) form.append("gender", input.gender);
  if (input.eyeColor) form.append("eyeColor", input.eyeColor);
  if (input.relationship) form.append("relationship", input.relationship);
  if (input.removePhoto) form.append("removePhoto", "true");
  if (input.photo) form.append("photo", input.photo);
  if (input.portraitRunId) form.append("portraitRunId", input.portraitRunId);
  return form;
}

export async function listCharacters(): Promise<CharacterResponse[]> {
  return apiRequest<CharacterResponse[]>("/api/characters");
}

export async function getCharacter(id: string): Promise<CharacterResponse> {
  return apiRequest<CharacterResponse>(`/api/characters/${id}`);
}

export async function createCharacter(input: SaveCharacterInput): Promise<CharacterResponse> {
  return apiRequest<CharacterResponse>("/api/characters", {
    method: "POST",
    body: toFormData(input),
  });
}

export async function updateCharacter(
  id: string,
  input: SaveCharacterInput,
): Promise<CharacterResponse> {
  return apiRequest<CharacterResponse>(`/api/characters/${id}`, {
    method: "PUT",
    body: toFormData(input),
  });
}

export async function deleteCharacter(id: string): Promise<void> {
  await apiRequest<void>(`/api/characters/${id}`, { method: "DELETE" });
}

export async function fetchCharacterPhotoObjectUrl(characterId: string): Promise<string> {
  const token = getToken();
  const response = await fetch(`${getApiBaseUrl()}/api/characters/${characterId}/photo`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError("Could not load character photo.", response.status);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}
