import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { useState } from "react";

import { confirmEmail } from "@/lib/api/auth";
import { BrandLogo } from "@/components/brand/BrandLogo";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/confirm-email")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Confirm email — ${BRAND_NAME}`,
      description: "Confirm your Beki account email address.",
      path: "/confirm-email",
      noindex: true,
    });
    return { meta, links };
  },
  validateSearch: (search: Record<string, unknown>) => ({
    success: search.success === "1" || search.success === 1 || search.success === true,
    token: typeof search.token === "string" ? search.token : undefined,
  }),
  component: ConfirmEmailPage,
});

function ConfirmEmailPage() {
  const { success, token } = Route.useSearch();
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleConfirm = async () => {
    if (!token) return;
    setConfirming(true);
    setError(null);
    try {
      const result = await confirmEmail(token);
      if (result.success) {
        navigate({
          to: "/confirm-email",
          search: { success: true, token: undefined },
          replace: true,
        });
      } else {
        setError(result.message);
      }
    } catch {
      setError("Could not confirm your email. Check that the API is running and try again.");
    } finally {
      setConfirming(false);
    }
  };

  const showConfirmButton = Boolean(token) && !success;

  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col items-center justify-center px-6">
      <BrandLogo className="mb-10" />
      <div className="max-w-md w-full rounded-3xl border border-border bg-card p-8 text-center shadow-card">
        <h1 className="font-display text-2xl font-bold">
          {success
            ? "Email confirmed!"
            : showConfirmButton
              ? "Confirm your email"
              : "Confirmation link invalid"}
        </h1>
        <p className="mt-3 text-muted-foreground text-sm">
          {success
            ? "Your account is active. Sign in to start creating personalized storybooks."
            : showConfirmButton
              ? `Click the button below to activate your ${BRAND_NAME} account.`
              : "This link may have expired. If you already clicked it once, try signing in — your email may already be confirmed."}
        </p>
        {error ? <p className="mt-3 text-destructive text-sm">{error}</p> : null}
        {showConfirmButton ? (
          <button
            type="button"
            onClick={handleConfirm}
            disabled={confirming}
            className="inline-flex mt-6 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition disabled:opacity-60"
          >
            {confirming ? "Confirming…" : "Confirm my email"}
          </button>
        ) : (
          <Link
            to="/"
            className="inline-flex mt-6 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
          >
            {success ? "Sign in" : "Back to home"}
          </Link>
        )}
      </div>
    </div>
  );
}
