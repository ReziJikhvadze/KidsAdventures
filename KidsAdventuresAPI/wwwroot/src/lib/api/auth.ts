import { apiRequest, setToken } from "./client";
import type { AuthResponse } from "./types";

export async function register(email: string, password: string): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/register", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, password }),
  });
  setToken(result.token);
  return result;
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/login", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, password }),
  });
  setToken(result.token);
  return result;
}

export function logout(): void {
  setToken(null);
}
