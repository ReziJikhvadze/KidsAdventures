import { GoogleOAuthProvider } from "@react-oauth/google";
import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import { getAuthConfig } from "@/lib/api/auth";

type GoogleAuthConfig = {
  loading: boolean;
  enabled: boolean;
  clientId: string | null;
};

const GoogleAuthConfigContext = createContext<GoogleAuthConfig>({
  loading: true,
  enabled: false,
  clientId: null,
});

export function useGoogleAuthConfig() {
  return useContext(GoogleAuthConfigContext);
}

type Props = {
  children: ReactNode;
};

export function GoogleAuthProvider({ children }: Props) {
  const [loading, setLoading] = useState(true);
  const [clientId, setClientId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getAuthConfig()
      .then((config) => {
        if (cancelled) return;
        if (config.googleEnabled && config.googleClientId) {
          setClientId(config.googleClientId);
        } else {
          setClientId(null);
        }
      })
      .catch(() => {
        if (!cancelled) setClientId(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const value = useMemo<GoogleAuthConfig>(
    () => ({
      loading,
      enabled: !!clientId,
      clientId,
    }),
    [loading, clientId],
  );

  const tree = (
    <GoogleAuthConfigContext.Provider value={value}>{children}</GoogleAuthConfigContext.Provider>
  );

  if (!clientId) {
    return tree;
  }

  return <GoogleOAuthProvider clientId={clientId}>{tree}</GoogleOAuthProvider>;
}
