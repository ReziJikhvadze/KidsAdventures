import { apiRequest } from "./client";
import type { ChildResponse } from "./types";

export async function listChildren(): Promise<ChildResponse[]> {
  return apiRequest<ChildResponse[]>("/api/children");
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
