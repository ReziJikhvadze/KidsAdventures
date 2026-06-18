import { apiRequest, setToken } from "./client";
import type { AuthConfigResponse, AuthResponse, SessionInfoResponse } from "./types";

export async function getAuthConfig(): Promise<AuthConfigResponse> {
  return apiRequest<AuthConfigResponse>("/api/auth/config", { auth: false });
}

export async function getSession(): Promise<SessionInfoResponse> {
  return apiRequest<SessionInfoResponse>("/api/auth/me");
}

export async function register(
  email: string,
  password: string,
  recaptchaToken?: string,
): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/register", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, password, recaptchaToken }),
  });
  setToken(result.token);
  return result;
}

/** One-step auth: signs in if the email exists, otherwise creates the account (reCAPTCHA only on new accounts). */
export async function continueAuth(
  email: string,
  password: string,
  recaptchaToken?: string,
): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/continue", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, password, recaptchaToken }),
  });
  setToken(result.token);
  return result;
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
  setToken(result.token);
  return result;
}

export async function loginWithGoogle(idToken: string): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/google", {
    method: "POST",
    body: JSON.stringify({ idToken }),
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
