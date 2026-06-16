import { GoogleLogin, type CredentialResponse } from "@react-oauth/google";
import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";

import { getAuthConfig } from "@/lib/api/auth";

type GoogleSignInButtonProps = {
  disabled?: boolean;
  onSuccess: (idToken: string) => void;
  onError?: () => void;
};

export function GoogleSignInButton({ disabled, onSuccess, onError }: GoogleSignInButtonProps) {
  const [enabled, setEnabled] = useState(false);
  const [providerReady, setProviderReady] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void getAuthConfig()
      .then((config) => {
        if (!cancelled) {
          setEnabled(config.googleEnabled && !!config.googleClientId);
        }
      })
      .catch(() => {
        if (!cancelled) setEnabled(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!enabled) return;
    const timer = window.setTimeout(() => setProviderReady(true), 0);
    return () => window.clearTimeout(timer);
  }, [enabled]);

  if (!enabled || !providerReady) {
    return null;
  }

  if (disabled) {
    return (
      <div className="flex h-10 w-full items-center justify-center rounded-md border border-input bg-muted/40 text-sm text-muted-foreground">
        Accept the terms above to continue with Google
      </div>
    );
  }

  return (
    <div className="flex w-full justify-center [&>div]:w-full [&_iframe]:!w-full">
      <GoogleLogin
        onSuccess={(response: CredentialResponse) => {
          if (response.credential) {
            onSuccess(response.credential);
            return;
          }
          onError?.();
        }}
        onError={() => onError?.()}
        useOneTap={false}
        theme="outline"
        size="large"
        shape="pill"
        text="continue_with"
        width="360"
      />
    </div>
  );
}

export function GoogleSignInBusyButton() {
  return (
    <div className="flex h-10 w-full items-center justify-center rounded-full border border-input bg-background text-sm text-muted-foreground">
      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      Signing in with Google…
    </div>
  );
}
