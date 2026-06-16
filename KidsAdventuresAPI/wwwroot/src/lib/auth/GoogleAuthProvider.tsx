import { GoogleOAuthProvider } from "@react-oauth/google";
import { useEffect, useState, type ReactNode } from "react";

import { getAuthConfig } from "@/lib/api/auth";

type Props = {
  children: ReactNode;
};

export function GoogleAuthProvider({ children }: Props) {
  const [clientId, setClientId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getAuthConfig()
      .then((config) => {
        if (!cancelled && config.googleEnabled && config.googleClientId) {
          setClientId(config.googleClientId);
        }
      })
      .catch(() => {
        /* Google sign-in stays hidden when config cannot be loaded */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!clientId) {
    return children;
  }

  return <GoogleOAuthProvider clientId={clientId}>{children}</GoogleOAuthProvider>;
}
