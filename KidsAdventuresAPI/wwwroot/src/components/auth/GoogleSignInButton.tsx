import { GoogleLogin, useGoogleLogin, type CredentialResponse } from "@react-oauth/google";
import { Loader2 } from "lucide-react";

import { useGoogleAuthConfig } from "@/lib/auth/GoogleAuthProvider";
import { useT } from "@/lib/i18n";

type GoogleCredential = { accessToken?: string; idToken?: string };

type GoogleSignInButtonProps = {
  disabled?: boolean;
  /** Called with a Google GIS id token (preferred) or OAuth access token. */
  onSuccess: (credential: GoogleCredential) => void;
  onError?: () => void;
  /** Fired when Google auth is not configured and the social chrome is clicked. */
  onUnavailable?: () => void;
  /** Beki journey chrome matching Partner Demo `button.social-auth`. */
  variant?: "default" | "social";
  label?: string;
};

/**
 * Custom-styled Google sign-in. Demo chrome is visual; an almost-invisible GIS
 * credential button sits on top so the API receives a real IdToken.
 */
export function GoogleSignInButton({
  disabled,
  onSuccess,
  onError,
  onUnavailable,
  variant = "default",
  label,
}: GoogleSignInButtonProps) {
  const t = useT();
  const { loading, enabled } = useGoogleAuthConfig();

  if (loading) {
    return variant === "social" ? (
      <button className="social-auth" type="button" disabled aria-busy="true">
        <span>G</span>
        {label ?? t.journey.auth.google}
      </button>
    ) : (
      <div className="flex h-10 w-full items-center justify-center rounded-md border border-input bg-muted/40 text-sm text-muted-foreground">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        Loading Google…
      </div>
    );
  }

  if (!enabled) {
    if (variant === "social") {
      return (
        <button
          className="social-auth"
          type="button"
          disabled={disabled}
          onClick={() => {
            if (onUnavailable) onUnavailable();
            else onError?.();
          }}
        >
          <span>G</span>
          {label ?? t.journey.auth.google}
        </button>
      );
    }
    return null;
  }

  return (
    <GoogleSignInReady
      disabled={disabled}
      onSuccess={onSuccess}
      onError={onError}
      variant={variant}
      label={label}
    />
  );
}

function GoogleSignInReady({
  disabled,
  onSuccess,
  onError,
  variant = "default",
  label,
}: GoogleSignInButtonProps) {
  const t = useT();
  const oauthFallback = useGoogleLogin({
    flow: "implicit",
    scope: "openid email profile",
    onSuccess: (tokenResponse) => {
      if (tokenResponse.access_token) {
        onSuccess({ accessToken: tokenResponse.access_token });
        return;
      }
      onError?.();
    },
    onError: () => onError?.(),
  });

  const onCredential = (response: CredentialResponse) => {
    if (response.credential) {
      onSuccess({ idToken: response.credential });
      return;
    }
    onError?.();
  };

  if (disabled) {
    if (variant === "social") {
      return (
        <button className="social-auth" type="button" disabled>
          <span>G</span>
          {label ?? t.journey.auth.google}
        </button>
      );
    }
    return (
      <div className="flex h-10 w-full items-center justify-center rounded-md border border-input bg-muted/40 text-sm text-muted-foreground">
        Accept the terms above to continue with Google
      </div>
    );
  }

  if (variant === "social") {
    return (
      <div className="ux-google-social-wrap">
        <button className="social-auth" type="button" tabIndex={-1} aria-hidden="true">
          <span>G</span>
          {label ?? t.journey.auth.google}
        </button>
        <div className="ux-google-gis-overlay">
          <GoogleLogin
            onSuccess={onCredential}
            onError={() => {
              // GIS widget failed — fall back to OAuth access token.
              oauthFallback();
            }}
            useOneTap={false}
            theme="outline"
            size="large"
            shape="rectangular"
            text="continue_with"
            width="400"
          />
        </div>
      </div>
    );
  }

  return (
    <div className="ux-google-default-wrap">
      <button
        type="button"
        className="inline-flex h-11 w-full items-center justify-center gap-2 rounded-md border border-input bg-background px-4 text-sm font-semibold shadow-sm"
        tabIndex={-1}
        aria-hidden="true"
      >
        <GoogleGlyph />
        Continue with Google
      </button>
      <div className="ux-google-gis-overlay">
        <GoogleLogin
          onSuccess={onCredential}
          onError={() => oauthFallback()}
          useOneTap={false}
          theme="outline"
          size="large"
          shape="rectangular"
          text="continue_with"
          width="400"
        />
      </div>
    </div>
  );
}

function GoogleGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden="true">
      <path
        fill="#FFC107"
        d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.3 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3 0 5.8 1.1 7.9 3l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.5-.4-3.5z"
      />
      <path
        fill="#FF3D00"
        d="M6.3 14.7l6.6 4.8C14.7 16 19 13 24 13c3 0 5.8 1.1 7.9 3l5.7-5.7C34 6.1 29.3 4 24 4 16.1 4 9.2 8.5 6.3 14.7z"
      />
      <path
        fill="#4CAF50"
        d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.3 35.7 26.8 36.8 24 36.8c-5.2 0-9.6-3.3-11.2-7.9l-6.5 5C9.1 39.5 16 44 24 44z"
      />
      <path
        fill="#1976D2"
        d="M43.6 20.5H42V20H24v8h11.3c-1.3 3.7-4.6 6.4-8.5 7.4l.1.1 6.2 5.2C35.8 42.4 44 36.5 44 24c0-1.3-.1-2.5-.4-3.5z"
      />
    </svg>
  );
}

export function GoogleSignInBusyButton({
  variant = "default",
}: {
  variant?: "default" | "social";
}) {
  const t = useT();
  if (variant === "social") {
    return (
      <button className="social-auth" type="button" disabled aria-busy="true">
        <span>G</span>
        {t.journey.auth.google}
      </button>
    );
  }

  return (
    <div className="flex h-10 w-full items-center justify-center rounded-full border border-input bg-background text-sm text-muted-foreground">
      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
      Signing in with Google…
    </div>
  );
}
