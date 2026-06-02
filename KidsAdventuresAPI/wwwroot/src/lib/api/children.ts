import { apiRequest } from "./client";
import type { ChildResponse } from "./types";

export async function createChild(name: string, age: number): Promise<ChildResponse> {
  return apiRequest<ChildResponse>("/api/children", {
    method: "POST",
    body: JSON.stringify({ name, age }),
  });
}
