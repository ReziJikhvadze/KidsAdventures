import { useEffect, useRef } from "react";

type GrecaptchaApi = {
  render: (
    container: HTMLElement,
    params: { sitekey: string; callback: (token: string) => void; "expired-callback": () => void },
  ) => number;
  reset: (id?: number) => void;
};

declare global {
  interface Window {
    grecaptcha?: GrecaptchaApi;
    onRecaptchaLoad?: () => void;
  }
}

const SCRIPT_ID = "google-recaptcha-script";

function ensureScript(): Promise<void> {
  return new Promise((resolve) => {
    if (typeof document === "undefined") return;
    if (window.grecaptcha?.render) {
      resolve();
      return;
    }
    if (document.getElementById(SCRIPT_ID)) {
      const check = window.setInterval(() => {
        if (window.grecaptcha?.render) {
          window.clearInterval(check);
          resolve();
        }
      }, 100);
      return;
    }
    const script = document.createElement("script");
    script.id = SCRIPT_ID;
    script.src = "https://www.google.com/recaptcha/api.js?render=explicit";
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    document.head.appendChild(script);
  });
}

/**
 * Renders a Google reCAPTCHA v2 checkbox. Only mounted when reCAPTCHA is enabled server-side.
 */
export function RecaptchaWidget({
  siteKey,
  onToken,
}: {
  siteKey: string;
  onToken: (token: string | null) => void;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const widgetIdRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    void ensureScript().then(() => {
      if (cancelled || !containerRef.current || !window.grecaptcha?.render) return;
      if (widgetIdRef.current !== null) return;
      widgetIdRef.current = window.grecaptcha.render(containerRef.current, {
        sitekey: siteKey,
        callback: (token: string) => onToken(token),
        "expired-callback": () => onToken(null),
      });
    });
    return () => {
      cancelled = true;
    };
  }, [siteKey, onToken]);

  return <div ref={containerRef} className="flex justify-center" />;
}
