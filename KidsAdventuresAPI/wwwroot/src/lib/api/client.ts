const TOKEN_KEY = "adventurepacks_token";

export function getApiBaseUrl(): string {
  const base = import.meta.env.VITE_API_BASE_URL;
  if (!base) {
    throw new Error("VITE_API_BASE_URL is not set. Add it to wwwroot/.env");
  }
  return base.replace(/\/$/, "");
}

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (typeof window === "undefined") return;
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export async function apiRequest<T>(
  path: string,
  options: RequestInit & { auth?: boolean } = {},
): Promise<T> {
  const { auth = true, ...fetchOptions } = options;
  const headers = new Headers(fetchOptions.headers);

  if (auth) {
    const token = getToken();
    if (token) headers.set("Authorization", `Bearer ${token}`);
  }

  if (fetchOptions.body && !(fetchOptions.body instanceof FormData)) {
    if (!headers.has("Content-Type")) {
      headers.set("Content-Type", "application/json");
    }
  }

  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    ...fetchOptions,
    headers,
  });

  if (!response.ok) {
    let message = response.statusText;
    try {
      const body = (await response.json()) as { message?: string };
      if (body.message) message = body.message;
    } catch {
      /* ignore */
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}
