import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import * as authApi from "@/lib/api/auth";
import { flushMarketingConsent } from "@/lib/auth/marketingConsent";
import { ApiError } from "@/lib/api/client";
import { getToken, setUnauthorizedHandler } from "@/lib/api/client";
import type { AuthResponse, SessionInfoResponse, SubscriptionType } from "@/lib/api/types";

type AuthUser = {
  /** Empty string for parents who signed up with a phone number only. */
  email: string;
  phoneNumber: string | null;
  displayName: string | null;
  isAdmin: boolean;
  subscriptionType: SubscriptionType;
  bookCredits: number;
  storiesUsedThisMonth: number;
  storiesAllowedThisMonth: number;
  storiesRemainingThisMonth: number;
  welcomeStoryRemaining: number;
  hasUnlimitedPdf: boolean;
  /** Whether this parent has agreed to hear from us. Their own space shows it and can turn it off. */
  marketingConsent: boolean;
};

type AuthContextValue = {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  canCreatePdf: boolean;
  login: (email: string, password: string) => Promise<void>;
  loginWithGoogle: (credential: { idToken?: string; accessToken?: string }) => Promise<void>;
  register: (email: string, password: string, recaptchaToken?: string) => Promise<void>;
  continueWith: (
    email: string,
    password: string,
    intent?: authApi.AuthIntent,
    recaptchaToken?: string,
  ) => Promise<void>;
  signInWithMagicLink: (token: string) => Promise<void>;
  signInWithPhoneCode: (phoneNumber: string, code: string) => Promise<void>;
  logout: () => void;
  applySession: (session: AuthResponse) => void;
  refreshAccountBalance: () => Promise<void>;
  setBookCredits: (credits: number) => void;
  setMarketingConsent: (consent: boolean) => Promise<void>;
};

const USER_KEY = "adventurepacks_user";

const AuthContext = createContext<AuthContextValue | null>(null);

function normalizeUser(raw: Partial<AuthUser>): AuthUser {
  const subscriptionType = raw.subscriptionType ?? "Free";
  const bookCredits = typeof raw.bookCredits === "number" ? raw.bookCredits : 0;
  const storiesAllowedThisMonth =
    typeof raw.storiesAllowedThisMonth === "number" ? raw.storiesAllowedThisMonth : 1 + bookCredits;
  const storiesUsedThisMonth =
    typeof raw.storiesUsedThisMonth === "number" ? raw.storiesUsedThisMonth : 0;
  const storiesRemainingThisMonth =
    typeof raw.storiesRemainingThisMonth === "number"
      ? raw.storiesRemainingThisMonth
      : Math.max(0, storiesAllowedThisMonth - storiesUsedThisMonth);
  const welcomeStoryRemaining =
    typeof raw.welcomeStoryRemaining === "number" ? raw.welcomeStoryRemaining : 0;

  return {
    email: raw.email ?? "",
    phoneNumber: raw.phoneNumber ?? null,
    displayName: raw.displayName ?? null,
    isAdmin: raw.isAdmin ?? false,
    subscriptionType,
    bookCredits,
    storiesUsedThisMonth,
    storiesAllowedThisMonth,
    storiesRemainingThisMonth,
    welcomeStoryRemaining,
    hasUnlimitedPdf: false,
    marketingConsent: raw.marketingConsent ?? false,
  };
}

function userFromSessionInfo(session: SessionInfoResponse): AuthUser {
  return normalizeUser({
    email: session.email,
    phoneNumber: session.phoneNumber ?? null,
    displayName: session.displayName ?? null,
    isAdmin: session.isAdmin ?? false,
    subscriptionType: session.subscriptionType,
    bookCredits: session.bookCredits ?? 0,
    storiesUsedThisMonth: session.storiesUsedThisMonth,
    storiesAllowedThisMonth: session.storiesAllowedThisMonth,
    storiesRemainingThisMonth: session.storiesRemainingThisMonth,
    welcomeStoryRemaining: session.welcomeStoryRemaining ?? 0,
    hasUnlimitedPdf: false,
    marketingConsent: session.marketingConsent ?? false,
  });
}

function loadStoredUser(): AuthUser | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<AuthUser>;
    // Either contact channel identifies the account; phone-only parents have no email.
    if (!parsed.email && !parsed.phoneNumber) return null;
    return normalizeUser(parsed);
  } catch {
    return null;
  }
}

function persistUser(user: AuthUser | null) {
  if (typeof window === "undefined") return;
  if (user) localStorage.setItem(USER_KEY, JSON.stringify(user));
  else localStorage.removeItem(USER_KEY);
}

function userFromAuthResponse(session: AuthResponse): AuthUser {
  return normalizeUser({
    email: session.email,
    phoneNumber: session.phoneNumber ?? null,
    displayName: session.displayName ?? null,
    isAdmin: session.isAdmin ?? false,
    subscriptionType: session.subscriptionType,
    bookCredits: session.bookCredits ?? 0,
    storiesUsedThisMonth: session.storiesUsedThisMonth,
    storiesAllowedThisMonth: session.storiesAllowedThisMonth,
    storiesRemainingThisMonth: session.storiesRemainingThisMonth,
    welcomeStoryRemaining: session.welcomeStoryRemaining ?? 0,
    hasUnlimitedPdf: false,
  });
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const logout = useCallback(() => {
    authApi.logout();
    setUser(null);
    persistUser(null);
  }, []);

  const applySessionInfo = useCallback((session: SessionInfoResponse) => {
    const next = userFromSessionInfo(session);
    setUser(next);
    persistUser(next);
  }, []);

  const refreshAccountBalance = useCallback(async () => {
    const token = getToken();
    if (!token) {
      logout();
      return;
    }

    try {
      /*
        A yes given before this account existed, spent here.

        The tick beside the terms is on the first stage of the create journey and the sign-in is
        on the fourth, so the parent agrees several screens before there is a row to write it on.
        Held on the device until now. It runs before `/me` so the session that arrives already
        carries the answer, and it is silent about failure — see `flushMarketingConsent`.
      */
      await flushMarketingConsent();
      const session = await authApi.getSession();
      applySessionInfo(session);
    } catch (error) {
      // Keep the existing session on transient /me failures. Only clear when the
      // token itself is rejected — phone-only accounts used to 401 here falsely.
      if (error instanceof ApiError && error.status === 401) {
        logout();
        return;
      }
      console.warn("Could not refresh account session", error);
    }
  }, [applySessionInfo, logout]);

  useEffect(() => {
    setUnauthorizedHandler(logout);
    return () => setUnauthorizedHandler(null);
  }, [logout]);

  useEffect(() => {
    const token = getToken();
    if (!token) {
      setUser(null);
      persistUser(null);
      setIsLoading(false);
      return;
    }

    const stored = loadStoredUser();
    if (stored) setUser(stored);

    void refreshAccountBalance().finally(() => setIsLoading(false));
  }, [refreshAccountBalance]);

  const applySession = useCallback(
    (session: AuthResponse) => {
      const next = userFromAuthResponse(session);
      setUser(next);
      persistUser(next);
      void refreshAccountBalance();
    },
    [refreshAccountBalance],
  );

  /**
   * The parent turning being written to on or off, from their own space.
   *
   * The switch moves first and the request follows, because a checkbox that waits for a server
   * before it moves reads as a checkbox that did not work. A failure puts it back where it was
   * and is thrown on, so the caller can say so.
   */
  const setMarketingConsent = useCallback(async (consent: boolean) => {
    const revert = (prev: AuthUser | null) => prev;
    setUser((prev) => {
      if (!prev) return prev;
      const next: AuthUser = { ...prev, marketingConsent: consent };
      persistUser(next);
      return next;
    });

    try {
      const session = await authApi.updateMarketingConsent(consent);
      const next = userFromSessionInfo(session);
      setUser(next);
      persistUser(next);
    } catch (error) {
      setUser((prev) => {
        if (!prev) return revert(prev);
        const back: AuthUser = { ...prev, marketingConsent: !consent };
        persistUser(back);
        return back;
      });
      throw error;
    }
  }, []);

  const setBookCredits = useCallback((credits: number) => {
    setUser((prev) => {
      if (!prev) return prev;
      const next: AuthUser = { ...prev, bookCredits: credits };
      persistUser(next);
      return next;
    });
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await authApi.login(email, password);
      applySession(session);
    },
    [applySession],
  );

  const loginWithGoogle = useCallback(
    async (credential: { idToken?: string; accessToken?: string }) => {
      const session = await authApi.loginWithGoogle(credential);
      applySession(session);
    },
    [applySession],
  );

  const register = useCallback(
    async (email: string, password: string, recaptchaToken?: string) => {
      const session = await authApi.register(email, password, recaptchaToken);
      applySession(session);
    },
    [applySession],
  );

  const continueWith = useCallback(
    async (
      email: string,
      password: string,
      intent?: authApi.AuthIntent,
      recaptchaToken?: string,
    ) => {
      const session = await authApi.continueAuth(email, password, intent, recaptchaToken);
      applySession(session);
    },
    [applySession],
  );

  const signInWithMagicLink = useCallback(
    async (token: string) => {
      const session = await authApi.verifyMagicLink(token);
      applySession(session);
    },
    [applySession],
  );

  const signInWithPhoneCode = useCallback(
    async (phoneNumber: string, code: string) => {
      const session = await authApi.verifyPhoneCode(phoneNumber, code);
      applySession(session);
    },
    [applySession],
  );

  const canCreatePdf = !!user && !!getToken();

  const value = useMemo(
    () => ({
      user,
      isAuthenticated: !isLoading && !!user && !!getToken(),
      isLoading,
      canCreatePdf,
      login,
      loginWithGoogle,
      register,
      continueWith,
      signInWithMagicLink,
      signInWithPhoneCode,
      logout,
      applySession,
      refreshAccountBalance,
      setBookCredits,
      setMarketingConsent,
    }),
    [
      user,
      isLoading,
      canCreatePdf,
      login,
      loginWithGoogle,
      register,
      continueWith,
      signInWithMagicLink,
      signInWithPhoneCode,
      logout,
      applySession,
      refreshAccountBalance,
      setBookCredits,
      setMarketingConsent,
    ],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
