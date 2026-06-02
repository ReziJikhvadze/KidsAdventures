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
import { getToken } from "@/lib/api/client";
import type { AuthResponse, SubscriptionType } from "@/lib/api/types";

type AuthUser = {
  email: string;
  subscriptionType: SubscriptionType;
};

type AuthContextValue = {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => void;
  applySession: (session: AuthResponse) => void;
};

const USER_KEY = "adventurepacks_user";

const AuthContext = createContext<AuthContextValue | null>(null);

function loadStoredUser(): AuthUser | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as AuthUser;
  } catch {
    return null;
  }
}

function persistUser(user: AuthUser | null) {
  if (typeof window === "undefined") return;
  if (user) localStorage.setItem(USER_KEY, JSON.stringify(user));
  else localStorage.removeItem(USER_KEY);
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const token = getToken();
    const stored = loadStoredUser();
    if (token && stored) setUser(stored);
    setIsLoading(false);
  }, []);

  const applySession = useCallback((session: AuthResponse) => {
    const next: AuthUser = {
      email: session.email,
      subscriptionType: session.subscriptionType,
    };
    setUser(next);
    persistUser(next);
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await authApi.login(email, password);
      applySession(session);
    },
    [applySession],
  );

  const register = useCallback(
    async (email: string, password: string) => {
      const session = await authApi.register(email, password);
      applySession(session);
    },
    [applySession],
  );

  const logout = useCallback(() => {
    authApi.logout();
    setUser(null);
    persistUser(null);
  }, []);

  const value = useMemo(
    () => ({
      user,
      isAuthenticated: !!user && !!getToken(),
      isLoading,
      login,
      register,
      logout,
      applySession,
    }),
    [user, isLoading, login, register, logout, applySession],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
