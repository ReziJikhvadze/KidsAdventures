import { useEffect, useState } from "react";
import { createFileRoute, Link } from "@tanstack/react-router";
import { CheckCircle2, Loader2 } from "lucide-react";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { useAuth } from "@/lib/auth/AuthContext";
import { confirmCheckoutSession } from "@/lib/api/subscriptions";
import { BRAND_NAME } from "@/lib/brand";
import { notify } from "@/lib/ui/notify";

type BillingSuccessSearch = {
  session_id?: string;
};

export const Route = createFileRoute("/billing/success")({
  validateSearch: (search: Record<string, unknown>): BillingSuccessSearch => ({
    session_id: typeof search.session_id === "string" ? search.session_id : undefined,
  }),
  head: () => ({
    meta: [{ title: `Payment successful — ${BRAND_NAME}` }],
  }),
  component: BillingSuccess,
});

function BillingSuccess() {
  const { session_id: sessionId } = Route.useSearch();
  const { refreshAccountBalance, user } = useAuth();
  const [confirming, setConfirming] = useState(!!sessionId);
  const [credits, setCredits] = useState<number | null>(null);

  useEffect(() => {
    if (!sessionId) {
      void refreshAccountBalance();
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        const balance = await confirmCheckoutSession(sessionId);
        if (cancelled) return;
        setCredits(balance.bookCredits);
        await refreshAccountBalance();
      } catch (err) {
        if (cancelled) return;
        notify.fromError(err, "Could not confirm payment. Try refreshing.");
        await refreshAccountBalance();
      } finally {
        if (!cancelled) setConfirming(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [sessionId, refreshAccountBalance]);

  const displayCredits = credits ?? user?.bookCredits ?? 0;

  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="mx-auto max-w-lg px-6 py-24 text-center">
        {confirming ? (
          <Loader2 className="h-14 w-14 mx-auto text-primary mb-6 animate-spin" />
        ) : (
          <CheckCircle2 className="h-14 w-14 mx-auto text-primary mb-6" />
        )}
        <h1 className="font-display text-3xl font-bold">
          {confirming ? "Confirming payment…" : "Payment successful"}
        </h1>
        <p className="mt-4 text-muted-foreground">
          {confirming
            ? "Adding your book credits now."
            : `Your book credits have been added — you now have ${displayCredits} credit${displayCredits === 1 ? "" : "s"}. Open My Books and tap Create illustrated PDF on any story.`}
        </p>
        <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
          <Link
            to="/my-packs"
            className="inline-flex items-center justify-center rounded-full bg-primary text-primary-foreground px-6 py-3 font-semibold hover:opacity-90 transition"
          >
            My books
          </Link>
          <Link
            to="/"
            hash="pricing"
            className="inline-flex items-center justify-center rounded-full border border-border px-6 py-3 font-semibold hover:bg-secondary transition"
          >
            View pricing
          </Link>
        </div>
      </main>
      <Footer />
    </div>
  );
}
