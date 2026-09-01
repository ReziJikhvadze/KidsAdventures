import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { useEffect, useState } from "react";

import { ApiError } from "@/lib/api/client";
import { useAuth } from "@/lib/auth/AuthContext";
import { takeMagicReturnPath } from "@/lib/auth/magicReturn";
import { BRAND_NAME } from "@/lib/brand";
import { useT } from "@/lib/i18n";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/auth/magic")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შესვლა — ${BRAND_NAME}`,
      description: "Beki-ს ერთჯერადი ბმულით შესვლა.",
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

/**
 * One attempt per token, for as long as the tab is open.
 *
 * This component does not get to mount only once. The page is server-rendered and then
 * hydrated, and a hydration mismatch makes React throw the server's markup away and build the
 * tree again — which unmounts and remounts this route. A `useRef` guard is per mount, so the
 * second mount spent the token a second time; the server had already burned it, and answered
 * "this link is invalid" about a link that had just worked a moment earlier.
 *
 * Keyed by the token, so a parent who gives up and asks for a second link is not handed the
 * verdict on their first one. Keeping the promise rather than a boolean is the point: whichever
 * instance is mounted when it settles lands the outcome, including one that mounted after.
 */
const attempts = new Map<string, Promise<void>>();

function MagicLinkLanding() {
  const copy = useT().journey.auth.landing;
  const { token, next } = Route.useSearch();
  const { signInWithMagicLink } = useAuth();
  const navigate = useNavigate();

  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!token) {
      setError(copy.missingToken);
      return;
    }

    let mounted = true;

    /*
      Fire once; subscribe every time.

      The teardown used to set a `cancelled` flag that the handlers checked, so a remount
      mid-flight threw away the answer to the one request the token was good for — leaving the
      screen on "signing in…" with no error and no way forward, for a parent who was by then
      signed in. The flag now only guards `setState` on a dead instance; the outcome itself
      belongs to the promise, which outlives any one mount.
    */
    let attempt = attempts.get(token);
    if (!attempt) {
      attempt = signInWithMagicLink(token);
      attempts.set(token, attempt);
      // The real handlers are attached below, and again by any later mount. This one is here so
      // a rejection is never momentarily unhandled between the two.
      attempt.catch(() => {});
    }

    void attempt.then(
      () => {
        if (!mounted) return;
        setDone(true);
        const target = safeNext(next);
        navigate({ to: target.to, hash: target.hash, replace: true });
      },
      (err: unknown) => {
        if (!mounted) return;
        setError(err instanceof ApiError ? err.message : copy.failedTitle);
      },
    );

    return () => {
      mounted = false;
    };
  }, [token, next, signInWithMagicLink, navigate, copy]);

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

/**
 * Mirrors the server-side guard: only same-origin relative paths are followed.
 *
 * With no usable `next` in the address, the parent's own request decides — the panel wrote the
 * return path down when it asked for the link — and the parent's space is what is left when
 * even that is gone. It used to be the checkout for everyone, which is the wrong end of the
 * product for somebody who pressed "my space" and asked to be let in.
 */
function safeNext(next: string | undefined): { to: string; hash?: string } {
  /*
    Spent either way.

    The remembered path belongs to the link being followed right now, so following one consumes
    it whether or not it was needed. Reading it only in the fallback left every ordinary sign-in
    — the overwhelming majority, which carry a perfectly good `next` — with a path still in
    storage, and the next link that arrived without one would follow a journey from weeks ago.
  */
  const remembered = takeMagicReturnPath();
  if (!next || !next.startsWith("/") || next.startsWith("//")) {
    return splitPath(remembered ?? "/dashboard");
  }

  return splitPath(next);
}

function splitPath(next: string): { to: string; hash?: string } {
  const hashIndex = next.indexOf("#");
  if (hashIndex === -1) {
    return { to: next };
  }

  const path = next.slice(0, hashIndex) || "/dashboard";
  const hash = next.slice(hashIndex + 1) || undefined;
  return { to: path, hash };
}
