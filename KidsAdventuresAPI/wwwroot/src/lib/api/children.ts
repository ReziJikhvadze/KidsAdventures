import { ApiError, apiRequest, getApiBaseUrl, getToken } from "./client";
import type { ChildResponse } from "./types";
import type { AvatarConfig, PersonalizationType } from "@/lib/avatar/config";

export async function listChildren(): Promise<ChildResponse[]> {
  return apiRequest<ChildResponse[]>("/api/children");
}

export async function fetchChildPhotoObjectUrl(childId: string): Promise<string> {
  const token = getToken();
  const response = await fetch(`${getApiBaseUrl()}/api/children/${childId}/photo`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError("Could not load child photo.", response.status);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export async function fetchHeroPortraitObjectUrl(childId: string): Promise<string> {
  const token = getToken();
  const response = await fetch(`${getApiBaseUrl()}/api/children/${childId}/hero-portrait`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError("Could not load hero portrait.", response.status);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export type CreateChildOptions = {
  photoFile?: File;
  personalizationType?: PersonalizationType;
  avatarConfig?: AvatarConfig;
};

export async function createChild(
  name: string,
  age: number,
  options?: CreateChildOptions | File,
): Promise<ChildResponse> {
  const resolved: CreateChildOptions =
    options instanceof File ? { photoFile: options } : (options ?? {});

  const form = new FormData();
  form.append("name", name);
  form.append("age", String(age));
  if (resolved.photoFile) {
    form.append("photo", resolved.photoFile);
  }
  if (resolved.personalizationType) {
    form.append("personalizationType", resolved.personalizationType);
  }
  if (resolved.avatarConfig) {
    form.append("avatarConfigJson", JSON.stringify(resolved.avatarConfig));
  }

  return apiRequest<ChildResponse>("/api/children", {
    method: "POST",
    body: form,
  });
}
