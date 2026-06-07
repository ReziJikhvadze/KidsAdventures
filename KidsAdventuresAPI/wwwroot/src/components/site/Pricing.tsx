import { useState } from "react";
import { Check, BookOpen, Sparkles, Users } from "lucide-react";
import { notify } from "@/lib/ui/notify";

import { useAuth } from "@/lib/auth/AuthContext";
import { createCheckoutSession } from "@/lib/api/subscriptions";
import type { BookPackPlan } from "@/lib/api/types";
import { AuthDialog } from "@/components/auth/AuthDialog";

type Action = "free" | BookPackPlan;

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
    name: "Free",
    price: "$0",
    period: "forever",
    desc: "One free 2-page welcome preview — no card needed.",
    features: [
      "1 free 2-page illustrated welcome story",
      "All 5 core themes",
      "Read the slideshow in My Books",
      "Full 6-page books with book credits",
    ],
    cta: "Create free story",
    highlighted: false,
    action: "free",
    icon: Sparkles,
  },
  {
    name: "3 Books",
    price: "$14.99",
    period: "one-time",
    desc: "Three illustrated storybook PDFs.",
    features: [
      "3 illustrated PDF credits",
      "Never expires",
      "Photo-personalized hero",
      "Print-ready downloads",
    ],
    cta: "Buy 3 books",
    highlighted: false,
    action: "Books3",
    icon: BookOpen,
  },
  {
    name: "5 Books",
    price: "$23.99",
    period: "one-time",
    desc: "Best value for siblings or repeat adventures.",
    features: [
      "5 illustrated PDF credits",
      "Never expires",
      "All themes & languages",
      "Family cast in stories",
    ],
    cta: "Buy 5 books",
    highlighted: true,
    action: "Books5",
    icon: BookOpen,
    badge: "Most popular",
  },
  {
    name: "15 Books",
    price: "$62.99",
    period: "one-time",
    desc: "For families, classrooms, or big gift seasons.",
    features: [
      "15 illustrated PDF credits",
      "Never expires",
      "Lowest price per book",
      "Perfect for grandparents",
    ],
    cta: "Buy 15 books",
    highlighted: false,
    action: "Books15",
    icon: Users,
    badge: "Best value",
  },
];

export function Pricing() {
  const { isAuthenticated, refreshAccountBalance } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [pendingPlan, setPendingPlan] = useState<BookPackPlan | null>(null);
  const [checkingOut, setCheckingOut] = useState<BookPackPlan | null>(null);

  const startCheckout = async (plan: BookPackPlan) => {
    setCheckingOut(plan);
    try {
      const session = await createCheckoutSession(plan);
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
              Free stories. Pay only for illustrated books.
            </h2>
            <p className="mt-4 text-muted-foreground">
              Creating a story is free. Each illustrated PDF uses one book credit — buy packs of 3,
              5, or 15 and use them whenever you want.
            </p>
          </div>

          <div className="mt-14 grid md:grid-cols-2 xl:grid-cols-4 gap-6 max-w-7xl mx-auto">
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
            One illustrated book uses 1 credit. Credits never expire. At roughly $4–$5 per book,
            packs cover our illustration costs with room to grow the product.
          </p>
        </div>
      </section>
    </>
  );
}
