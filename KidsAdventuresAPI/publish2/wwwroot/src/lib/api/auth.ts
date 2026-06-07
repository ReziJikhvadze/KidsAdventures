import { apiRequest, setToken } from "./client";
import type { AuthResponse, RegisterResponse, SessionInfoResponse } from "./types";

export async function getSession(): Promise<SessionInfoResponse> {
  return apiRequest<SessionInfoResponse>("/api/auth/me");
}

export async function register(email: string, password: string): Promise<RegisterResponse> {
  return apiRequest<RegisterResponse>("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
  setToken(result.token);
  return result;
}

export function logout(): void {
  setToken(null);
}

export async function confirmEmail(token: string): Promise<{ success: boolean; message: string }> {
  return apiRequest<{ success: boolean; message: string }>("/api/auth/confirm-email", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ token }),
  });
}
