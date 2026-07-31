import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";

import { ApiError } from "@/lib/api/client";
import { useAuth } from "@/lib/auth/AuthContext";
import { BRAND_NAME } from "@/lib/brand";
import { t } from "@/lib/i18n";
import { buildPageMeta } from "@/lib/seo";

const copy = t.journey.auth.landing;

export const Route = createFileRoute("/auth/magic")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შესვლა — ${BRAND_NAME}`,
      description: "Adventrya-ს ერთჯერადი ბმულით შესვლა.",
      path: "/auth/magic",
      noindex: true,
    });
    return { meta, links };
  },
  validateSearch: (search: Record<string, unknown>) => ({
    token: typeof search.token === "string" ? search.token : undefined,
    next: typeof search.next === "string" ? search.next : undefined,
  }),
  component: MagicLinkLanding,
});

function MagicLinkLanding() {
  const { token, next } = Route.useSearch();
  const { signInWithMagicLink } = useAuth();
  const navigate = useNavigate();

  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  // React 18 mounts effects twice in development, and a magic link is single-use:
  // the second call would consume nothing and report the link as invalid.
  const attempted = useRef(false);

  useEffect(() => {
    if (attempted.current) return;
    attempted.current = true;

    if (!token) {
      setError(copy.missingToken);
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        await signInWithMagicLink(token);
        if (cancelled) return;
        setDone(true);
        const target = safeNext(next);
        navigate({ to: target.to, hash: target.hash, replace: true });
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.message : copy.failedTitle);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [token, next, signInWithMagicLink, navigate]);

  return (
    <main className="ux-auth-landing">
      <section className="ux-auth-landing-card" role="status" aria-live="polite">
        <h1 className="ux-auth-landing-title">
          {error ? copy.failedTitle : done ? copy.successTitle : copy.verifying}
        </h1>
        <p className="ux-auth-landing-lead">
          {error ?? (done ? copy.successLead : copy.verifyingLead)}
        </p>
        {error ? (
          <div className="ux-auth-landing-actions">
            <Link to="/create" hash="auth" className="ux-auth-landing-primary">
              {copy.retry}
            </Link>
            <Link to="/" className="ux-auth-landing-secondary">
              {copy.goHome}
            </Link>
          </div>
        ) : null}
      </section>
    </main>
  );
}

/** Mirrors the server-side guard: only same-origin relative paths are followed. */
function safeNext(next: string | undefined): { to: string; hash?: string } {
  if (!next || !next.startsWith("/") || next.startsWith("//")) {
    return { to: "/create", hash: "checkout" };
  }

  const hashIndex = next.indexOf("#");
  if (hashIndex === -1) {
    return { to: next };
  }

  const path = next.slice(0, hashIndex) || "/create";
  const hash = next.slice(hashIndex + 1) || undefined;
  return { to: path, hash };
}
