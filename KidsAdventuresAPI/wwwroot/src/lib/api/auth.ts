import { SESSION_KEYS, clearSessionState } from "@/lib/storage/session";

import { apiRequest, setToken } from "./client";
import type {
  AuthChallengeResponse,
  AuthConfigResponse,
  AuthResponse,
  EmailStatusResponse,
  SessionInfoResponse,
} from "./types";

export async function getAuthConfig(): Promise<AuthConfigResponse> {
  return apiRequest<AuthConfigResponse>("/api/auth/config", { auth: false });
}

const GUEST_USED_KEY = SESSION_KEYS.guestPreviewUsed;
const GUEST_PREVIEW_ID_KEY = SESSION_KEYS.guestPreviewId;
const GUEST_STORY_ID_KEY = SESSION_KEYS.guestStoryId;

/** True when this browser already used the free no-login teaser (legacy, non-authoritative hint). */
export function hasUsedGuestPreview(): boolean {
  try {
    return localStorage.getItem(GUEST_USED_KEY) === "1";
  } catch {
    return false;
  }
}

/** Persist the server-side ids of a just-generated teaser so they survive the auth dialog round-trip. */
export function storeGuestPreviewIds(guestPreviewId: string, storyId: string): void {
  try {
    if (guestPreviewId) localStorage.setItem(GUEST_PREVIEW_ID_KEY, guestPreviewId);
    if (storyId) localStorage.setItem(GUEST_STORY_ID_KEY, storyId);
  } catch {
    /* ignore */
  }
}

/** Clears the teaser ids once they've been claimed (after a successful import/redeem). */
export function clearGuestPreviewIds(): void {
  try {
    localStorage.removeItem(GUEST_PREVIEW_ID_KEY);
    localStorage.removeItem(GUEST_STORY_ID_KEY);
  } catch {
    /* ignore */
  }
}

/** Reads the trustable teaser identifiers sent to the backend during sign-up. */
function readGuestPreviewIds(): { guestPreviewId?: string; storyId?: string } {
  try {
    return {
      guestPreviewId: localStorage.getItem(GUEST_PREVIEW_ID_KEY) || undefined,
      storyId: localStorage.getItem(GUEST_STORY_ID_KEY) || undefined,
    };
  } catch {
    return {};
  }
}

/** Welcome-gift signals sent on every account-creating auth call. */
function guestPreviewAuthFields() {
  const { guestPreviewId, storyId } = readGuestPreviewIds();
  return { usedGuestPreview: hasUsedGuestPreview(), guestPreviewId, storyId };
}

/** Email-first UX: tells us whether to greet a returning user or welcome a brand-new parent. */
export async function getEmailStatus(email: string): Promise<EmailStatusResponse> {
  return apiRequest<EmailStatusResponse>("/api/auth/email-status", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email }),
  });
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
    body: JSON.stringify({ email, password, recaptchaToken, ...guestPreviewAuthFields() }),
  });
  setToken(result.token);
  return result;
}

/** One-step auth: signs in if the email exists, otherwise creates the account (reCAPTCHA only on new accounts). */
/** What the caller believes it is doing. Omitted means "either", which is the old behaviour. */
export type AuthIntent = "signin" | "register";

/**
 * Sign in, or create an account, over one endpoint.
 *
 * `intent` is not decoration: the endpoint creates an account for any email it does not know, so
 * without it a mistyped address on the sign-in form is answered with a new empty account rather
 * than "no such address". The server refuses to create anything when the intent is `signin`.
 */
export async function continueAuth(
  email: string,
  password: string,
  intent?: AuthIntent,
  recaptchaToken?: string,
): Promise<AuthResponse> {
  const result = await apiRequest<AuthResponse>("/api/auth/continue", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, password, intent, recaptchaToken, ...guestPreviewAuthFields() }),
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

export async function loginWithGoogle(credential: {
  idToken?: string;
  accessToken?: string;
}): Promise<AuthResponse> {
  const idToken = credential.idToken?.trim();
  const accessToken = credential.accessToken?.trim();
  const result = await apiRequest<AuthResponse>("/api/auth/google", {
    method: "POST",
    body: JSON.stringify({
      ...(idToken ? { idToken } : {}),
      ...(accessToken ? { accessToken } : {}),
      ...guestPreviewAuthFields(),
    }),
  });
  setToken(result.token);
  return result;
}

/** Asks the server to email a one-time sign-in link. Also creates the account, on verify. */
export async function requestMagicLink(
  email: string,
  returnPath?: string,
): Promise<AuthChallengeResponse> {
  return apiRequest<AuthChallengeResponse>("/api/auth/magic-link", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ email, returnPath }),
  });
}

export async function verifyMagicLink(token: string): Promise<AuthResponse> {
  const { guestPreviewId, storyId } = readGuestPreviewIds();
  const result = await apiRequest<AuthResponse>("/api/auth/magic-link/verify", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ token, guestPreviewId, storyId }),
  });
  setToken(result.token);
  return result;
}

/** Sends a six-digit code to a Georgian mobile number. */
export async function requestPhoneCode(phoneNumber: string): Promise<AuthChallengeResponse> {
  return apiRequest<AuthChallengeResponse>("/api/auth/phone/code", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ phoneNumber }),
  });
}

export async function verifyPhoneCode(phoneNumber: string, code: string): Promise<AuthResponse> {
  const { guestPreviewId, storyId } = readGuestPreviewIds();
  const result = await apiRequest<AuthResponse>("/api/auth/phone/verify", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ phoneNumber, code, guestPreviewId, storyId }),
  });
  setToken(result.token);
  return result;
}

export function logout(): void {
  // Not just the token: the journey draft and guest-teaser ids are session data too,
  // and leaving them behind showed the next visitor the previous parent's child.
  clearSessionState();
}

export async function confirmEmail(token: string): Promise<{ success: boolean; message: string }> {
  return apiRequest<{ success: boolean; message: string }>("/api/auth/confirm-email", {
    method: "POST",
    auth: false,
    body: JSON.stringify({ token }),
  });
}
