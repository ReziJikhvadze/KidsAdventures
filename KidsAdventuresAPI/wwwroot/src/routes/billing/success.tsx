import { useEffect, useState } from "react";
import { createFileRoute, Link } from "@tanstack/react-router";
import { CheckCircle2, Loader2 } from "lucide-react";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { useAuth } from "@/lib/auth/AuthContext";
import { confirmCheckoutSession, getAccountBalance } from "@/lib/api/subscriptions";
import { BRAND_NAME } from "@/lib/brand";
import { notify } from "@/lib/ui/notify";

type BillingSuccessSearch = {
  session_id?: string;
  payment_id?: string;
  status?: string;
};

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

function isRetryableConfirmError(message: string): boolean {
  const lower = message.toLowerCase();
  return (
    lower.includes("still processing") ||
    lower.includes("not completed") ||
    lower.includes("could not be confirmed")
  );
}

export const Route = createFileRoute("/billing/success")({
  validateSearch: (search: Record<string, unknown>): BillingSuccessSearch => ({
    session_id: typeof search.session_id === "string" ? search.session_id : undefined,
    payment_id: typeof search.payment_id === "string" ? search.payment_id : undefined,
    status: typeof search.status === "string" ? search.status : undefined,
  }),
  head: () => ({
    meta: [{ title: `Payment successful — ${BRAND_NAME}` }],
  }),
  component: BillingSuccess,
});

function BillingSuccess() {
  const { session_id: sessionId, payment_id: paymentId, status } = Route.useSearch();
  const { refreshAccountBalance, user } = useAuth();
  const shouldConfirm =
    !!sessionId || (!!paymentId && (!status || status.toLowerCase() === "succeeded"));
  const [confirming, setConfirming] = useState(shouldConfirm);
  const [credits, setCredits] = useState<number | null>(null);

  useEffect(() => {
    if (!shouldConfirm) {
      void refreshAccountBalance();
      return;
    }

    let cancelled = false;
    (async () => {
      const startingCredits = user?.bookCredits ?? 0;

      try {
        let balance = null;
        for (let attempt = 0; attempt < 8; attempt++) {
          try {
            balance = await confirmCheckoutSession({
              sessionId,
              paymentId,
            });
            break;
          } catch (err) {
            const message = err instanceof Error ? err.message : "";
            if (!isRetryableConfirmError(message) || attempt === 7) {
              throw err;
            }
            await sleep(600 + attempt * 600);
          }
        }

        if (cancelled || !balance) return;
        setCredits(balance.bookCredits);
        await refreshAccountBalance();
      } catch (err) {
        if (cancelled) return;

        const refreshed = await getAccountBalance().catch(() => null);
        await refreshAccountBalance();
        const latestCredits = refreshed?.bookCredits ?? user?.bookCredits ?? startingCredits;
        if (latestCredits > startingCredits) {
          setCredits(latestCredits);
          return;
        }

        const message = err instanceof Error ? err.message : "";
        if (!isRetryableConfirmError(message)) {
          notify.fromError(err, "Could not confirm payment. Try refreshing.");
        }
      } finally {
        if (!cancelled) setConfirming(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [sessionId, paymentId, status, shouldConfirm, refreshAccountBalance, user?.bookCredits]);

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
