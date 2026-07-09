import { useState } from "react";
import { Check, BookOpen, Sparkles } from "lucide-react";
import { notify } from "@/lib/ui/notify";

import { useAuth } from "@/lib/auth/AuthContext";
import { createCheckoutSession } from "@/lib/api/subscriptions";
import type { BookPackPlan, PaymentProvider } from "@/lib/api/types";
import { AuthDialog } from "@/components/auth/AuthDialog";

type Action = "free" | BookPackPlan;

// Dodo is temporarily hidden — re-add { id: "dodo", label: "Dodo Payments" } to restore the toggle.
const paymentMethods: { id: PaymentProvider; label: string }[] = [
  { id: "stripe", label: "Card (Stripe)" },
];

const plans: {
  name: string;
  price: string;
  period: string;
  desc: string;
  features: string[];
  cta: string;
  highlighted: boolean;
  action: Action;
  icon: typeof Sparkles;
  badge?: string;
}[] = [
  {
    name: "First book free",
    price: "$0",
    period: "no card needed",
    desc: "Your first complete storybook — fully illustrated, on us.",
    features: [
      "Full 6-page illustrated story",
      "Photo-personalized hero",
      "All 5 core themes",
      "Printable PDF download (free)",
    ],
    cta: "Create my free book",
    highlighted: false,
    action: "free",
    icon: Sparkles,
  },
  {
    name: "Every book after",
    price: "$4.99",
    period: "one-time, per book",
    desc: "Loved the first one? Each new illustrated storybook is a single payment.",
    features: [
      "Another full 6-page illustrated story",
      "Photo-personalized hero",
      "Printable PDF download (free)",
      "Your extra wishes woven into the story",
    ],
    cta: "Buy the next book — $4.99",
    highlighted: true,
    action: "Book1",
    icon: BookOpen,
    badge: "Most popular",
  },
];

export function Pricing() {
  const { isAuthenticated, refreshAccountBalance } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [pendingPlan, setPendingPlan] = useState<BookPackPlan | null>(null);
  const [checkingOut, setCheckingOut] = useState<BookPackPlan | null>(null);
  const [provider, setProvider] = useState<PaymentProvider>("stripe");

  const startCheckout = async (plan: BookPackPlan) => {
    setCheckingOut(plan);
    try {
      const session = await createCheckoutSession(plan, provider);
      if (session.checkoutUrl) {
        window.location.href = session.checkoutUrl;
        return;
      }
      notify.error("Checkout could not start", {
        description: "No payment link was returned. Please try again.",
      });
    } catch (err) {
      notify.fromError(err, "Could not start checkout.");
    } finally {
      setCheckingOut(null);
    }
  };

  const handleCta = (action: Action) => {
    if (action === "free") {
      document.getElementById("generator")?.scrollIntoView({ behavior: "smooth" });
      return;
    }

    if (!isAuthenticated) {
      setPendingPlan(action);
      setAuthOpen(true);
      return;
    }

    void startCheckout(action);
  };

  return (
    <>
      <AuthDialog
        open={authOpen}
        onOpenChange={setAuthOpen}
        defaultMode="login"
        onSuccess={() => {
          void refreshAccountBalance();
          if (pendingPlan) {
            const plan = pendingPlan;
            setPendingPlan(null);
            void startCheckout(plan);
          }
        }}
      />
      <section id="pricing" className="relative py-24 md:py-32">
        <div className="mx-auto max-w-7xl px-6">
          <div className="max-w-2xl mx-auto text-center">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">Pricing</p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              Your first book is free. Every book after is $4.99.
            </h2>
            <p className="mt-4 text-muted-foreground">
              Create a complete, fully illustrated storybook at no cost. When you&apos;re ready for more
              adventures, one simple $4.99 payment unlocks each new book — yours to keep and print.
            </p>
          </div>

          {paymentMethods.length > 1 && (
            <div className="mt-8 flex flex-col items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Payment method
              </span>
              <div className="inline-flex rounded-full border border-border bg-card p-1 shadow-soft">
                {paymentMethods.map((m) => (
                  <button
                    key={m.id}
                    type="button"
                    onClick={() => setProvider(m.id)}
                    aria-pressed={provider === m.id}
                    className={`rounded-full px-4 py-1.5 text-sm font-semibold transition ${
                      provider === m.id
                        ? "bg-foreground text-background"
                        : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    {m.label}
                  </button>
                ))}
              </div>
            </div>
          )}

          <div className="mt-10 grid sm:grid-cols-2 gap-6 max-w-3xl mx-auto">
            {plans.map((p) => {
              const Icon = p.icon;
              const isLoading = p.action !== "free" && checkingOut === p.action;
              return (
                <div
                  key={p.name}
                  className={`relative rounded-3xl p-8 border flex flex-col ${
                    p.highlighted
                      ? "bg-foreground text-background border-foreground shadow-card"
                      : "bg-card border-border shadow-soft"
                  }`}
                >
                  {p.badge && (
                    <div
                      className={`absolute -top-3 left-8 inline-flex items-center rounded-full px-3 py-1 text-xs font-semibold ${
                        p.highlighted
                          ? "bg-primary text-primary-foreground"
                          : "bg-foreground text-background"
                      }`}
                    >
                      {p.badge}
                    </div>
                  )}

                  <div className="flex items-center gap-2">
                    <span
                      className={`grid place-items-center h-8 w-8 rounded-lg ${
                        p.highlighted ? "bg-background/15" : "bg-primary/10 text-primary"
                      }`}
                    >
                      <Icon className="h-4 w-4" />
                    </span>
                    <div className="font-display text-xl font-semibold">{p.name}</div>
                  </div>

                  <div className="mt-4 flex items-baseline gap-1">
                    <span className="font-display text-5xl font-bold">{p.price}</span>
                    <span
                      className={p.highlighted ? "text-background/70" : "text-muted-foreground"}
                    >
                      {p.period}
                    </span>
                  </div>
                  <p
                    className={`mt-2 ${p.highlighted ? "text-background/70" : "text-muted-foreground"}`}
                  >
                    {p.desc}
                  </p>

                  <ul className="mt-6 space-y-3 flex-1">
                    {p.features.map((f) => (
                      <li key={f} className="flex items-start gap-3 text-sm">
                        <span
                          className={`mt-0.5 grid place-items-center h-5 w-5 rounded-full ${
                            p.highlighted
                              ? "bg-background/15 text-background"
                              : "bg-secondary text-primary"
                          }`}
                        >
                          <Check className="h-3 w-3" />
                        </span>
                        <span>{f}</span>
                      </li>
                    ))}
                  </ul>

                  <button
                    onClick={() => handleCta(p.action)}
                    disabled={isLoading}
                    className={`mt-8 w-full rounded-full py-3 font-semibold transition disabled:opacity-60 ${
                      p.highlighted
                        ? "bg-primary text-primary-foreground hover:opacity-90"
                        : "bg-foreground text-background hover:opacity-90"
                    }`}
                  >
                    {isLoading ? "Redirecting…" : p.cta}
                  </button>
                </div>
              );
            })}
          </div>

          <p className="mt-8 text-center text-xs text-muted-foreground max-w-2xl mx-auto">
            Your first illustrated book is free. After that, one $4.99 payment unlocks each new book —
            no subscription, no hidden fees, and the printable PDF download is always free.
          </p>
        </div>
      </section>
    </>
  );
}
