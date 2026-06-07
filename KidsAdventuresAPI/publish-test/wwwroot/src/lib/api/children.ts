import { ApiError, apiRequest, getApiBaseUrl, getToken } from "./client";
import type { ChildResponse } from "./types";

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

export async function createChild(
  name: string,
  age: number,
  photoFile?: File,
): Promise<ChildResponse> {
  const form = new FormData();
  form.append("name", name);
  form.append("age", String(age));
  if (photoFile) {
    form.append("photo", photoFile);
  }

  return apiRequest<ChildResponse>("/api/children", {
    method: "POST",
    body: form,
  });
}
