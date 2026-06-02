import { useState } from "react";
import { Check, Gift, Sparkles, Heart } from "lucide-react";
import { toast } from "sonner";

import { useAuth } from "@/lib/auth/AuthContext";
import { ApiError } from "@/lib/api/client";
import { createCheckoutSession } from "@/lib/api/subscriptions";
import { AuthDialog } from "@/components/auth/AuthDialog";

type Action = "free" | "premium" | "keepsake";

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
    desc: "Try one adventure on the house.",
    features: ["1 adventure pack", "All 5 core themes", "Printable PDF", "Personalized name & age"],
    cta: "Get started",
    highlighted: false,
    action: "free",
    icon: Sparkles,
  },
  {
    name: "Premium",
    price: "$14.99",
    period: "/month",
    desc: "Unlimited adventures, all year round.",
    features: [
      "Unlimited adventure packs",
      "Birthday Mode + Family Quests",
      "Premium seasonal themes",
      "Multiple child profiles",
      "High-resolution printables",
    ],
    cta: "Start Premium",
    highlighted: true,
    action: "premium",
    icon: Heart,
    badge: "Most popular",
  },
  {
    name: "Family Keepsake",
    price: "$19.99",
    period: "one-time",
    desc: "A premium book to treasure forever.",
    features: [
      "20–30 page premium book",
      "Personalized family illustrations",
      "Premium paper-quality PDF",
      "Perfect gift from grandparents",
      "Ships-ready print file",
    ],
    cta: "Order Keepsake",
    highlighted: false,
    action: "keepsake",
    icon: Gift,
    badge: "Best gift",
  },
];

export function Pricing() {
  const { isAuthenticated } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [pendingPremium, setPendingPremium] = useState(false);

  const startPremiumCheckout = async () => {
    try {
      const session = await createCheckoutSession("Premium");
      if (session.checkoutUrl) {
        window.location.href = session.checkoutUrl;
        return;
      }
      toast.error("Checkout URL was not returned.");
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Could not start checkout.";
      toast.error(message);
    }
  };

  const handleCta = (action: Action) => {
    if (action === "free") {
      document.getElementById("generator")?.scrollIntoView({ behavior: "smooth" });
      return;
    }
    if (action === "premium") {
      if (!isAuthenticated) {
        setPendingPremium(true);
        setAuthOpen(true);
        return;
      }
      void startPremiumCheckout();
      return;
    }
    toast("Family Keepsake is coming soon ✨", {
      description: "One-time book orders will be available in a future release.",
    });
  };

  return (
    <>
      <AuthDialog
        open={authOpen}
        onOpenChange={setAuthOpen}
        defaultMode="login"
        onSuccess={() => {
          if (pendingPremium) {
            setPendingPremium(false);
            void startPremiumCheckout();
          }
        }}
      />
      <section id="pricing" className="relative py-24 md:py-32">
        <div className="mx-auto max-w-7xl px-6">
          <div className="max-w-2xl mx-auto text-center">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">Pricing</p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              One free pack. Or unlimited. Or a forever keepsake.
            </h2>
            <p className="mt-4 text-muted-foreground">
              Start free. Upgrade for unlimited adventures, or order a premium book parents and
              grandparents love to gift.
            </p>
          </div>

          <div className="mt-14 grid md:grid-cols-3 gap-6 max-w-6xl mx-auto">
            {plans.map((p) => {
              const Icon = p.icon;
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
                    className={`mt-8 w-full rounded-full py-3 font-semibold transition ${
                      p.highlighted
                        ? "bg-primary text-primary-foreground hover:opacity-90"
                        : "bg-foreground text-background hover:opacity-90"
                    }`}
                  >
                    {p.cta}
                  </button>
                </div>
              );
            })}
          </div>

          <p className="mt-8 text-center text-xs text-muted-foreground">
            Parents spend far more on personalized keepsakes than on subscriptions — both options
            are here.
          </p>
        </div>
      </section>
    </>
  );
}
