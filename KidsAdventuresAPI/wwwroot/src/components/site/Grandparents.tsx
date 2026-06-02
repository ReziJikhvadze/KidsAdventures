import { Gift, Heart, Sparkles, ArrowRight } from "lucide-react";

export function Grandparents() {
  return (
    <section id="grandparents" className="relative py-24 md:py-32 scroll-mt-20">
      <div className="mx-auto max-w-7xl px-6">
        <div className="grid lg:grid-cols-[1.1fr_1fr] gap-10 items-center">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full bg-primary/10 text-primary px-3 py-1 text-xs font-semibold">
              <Gift className="h-3.5 w-3.5" />
              For grandparents
            </div>
            <h2 className="mt-4 font-display text-4xl md:text-5xl font-bold text-balance">
              The perfect gift for a grandchild who has everything.
            </h2>
            <p className="mt-4 text-muted-foreground text-lg max-w-xl">
              Grandparents are always looking for something meaningful. A personalized adventure
              book starring their grandchild beats another toy — and lasts forever.
            </p>

            <ul className="mt-6 space-y-3 text-sm max-w-md">
              {[
                "Personalized with their grandchild's name, age and theme",
                "Add grandma or grandpa into the story as a character",
                "Premium 20–30 page Family Keepsake option",
                "Print at home or ship-ready PDF — no waiting",
              ].map((f) => (
                <li key={f} className="flex items-start gap-3">
                  <span className="mt-0.5 grid place-items-center h-5 w-5 rounded-full bg-secondary text-primary">
                    <Heart className="h-3 w-3" />
                  </span>
                  <span>{f}</span>
                </li>
              ))}
            </ul>

            <div className="mt-8 flex flex-wrap gap-3">
              <a
                href="#generator"
                className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-5 py-3 text-sm font-semibold hover:opacity-90 transition"
              >
                <Sparkles className="h-4 w-4" />
                Create a gift pack
              </a>
              <a
                href="#pricing"
                className="inline-flex items-center gap-2 rounded-full bg-card border border-border px-5 py-3 text-sm font-semibold hover:bg-secondary transition"
              >
                See Family Keepsake
                <ArrowRight className="h-4 w-4" />
              </a>
            </div>
          </div>

          {/* Visual card */}
          <div className="relative">
            <div
              className="absolute inset-0 -z-10 rounded-[2.5rem] opacity-60 blur-3xl"
              style={{ background: "var(--gradient-primary, var(--accent))" }}
            />
            <div className="relative rounded-3xl bg-card border border-border shadow-card p-8 rotate-1">
              <div className="rounded-2xl bg-secondary/60 p-6 -rotate-1">
                <div className="text-xs font-semibold uppercase tracking-wide text-foreground/60">
                  A gift from Grandma
                </div>
                <div className="mt-1 font-display text-2xl font-bold leading-tight">
                  Leo's Pirate Quest
                </div>
                <p className="mt-3 text-sm text-muted-foreground">
                  "Captain Leo and First Mate Grandma set sail on a stormy night to find the
                  treasure of Blue Coral Bay…"
                </p>
                <div className="mt-5 flex items-center gap-2 text-xs">
                  <span className="rounded-full bg-primary text-primary-foreground px-2.5 py-1 font-semibold">
                    20 pages
                  </span>
                  <span className="rounded-full bg-card border border-border px-2.5 py-1">
                    Premium paper
                  </span>
                  <span className="rounded-full bg-card border border-border px-2.5 py-1">
                    Family illustrations
                  </span>
                </div>
              </div>
              <div className="mt-5 flex items-center justify-between text-sm">
                <span className="text-muted-foreground">Family Keepsake</span>
                <span className="font-display text-2xl font-bold">$19.99</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
