import heroImg from "@/assets/hero.jpg";
import { ArrowRight, BookOpen, Check, Printer, ShieldCheck, Sparkles } from "lucide-react";

export function Hero() {
  return (
    <section className="relative overflow-hidden border-b border-border bg-secondary/20">
      <div className="mx-auto max-w-7xl px-6 pb-8 pt-8 sm:pb-12 sm:pt-10 md:pb-16 md:pt-16">
        <div className="grid items-center gap-10 lg:grid-cols-[1.02fr_0.98fr] lg:gap-16">
          <div className="min-w-0 animate-rise">
            <p className="inline-flex items-center gap-2 rounded-full border border-primary/20 bg-primary/5 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-primary">
              <Sparkles className="h-3.5 w-3.5" />
              Free preview in minutes
            </p>
            <h1 className="mt-4 font-display text-3xl font-bold leading-[1.05] text-balance sm:text-5xl md:text-6xl">
              A bedtime adventure starring{" "}
              <span className="relative inline-block">
                <span className="relative z-10">your child</span>
                <span className="absolute inset-x-0 bottom-1 -z-0 h-3 rounded bg-[color:var(--sun)]/70" />
              </span>
              .
            </h1>
            <p className="mt-5 max-w-xl text-base leading-relaxed text-muted-foreground text-pretty sm:text-lg">
              Create a personalized illustrated storybook your child recognizes, reads, and wants to
              keep. Start with a free preview, then unlock the full printable book for one simple
              price.
            </p>

            <div className="mt-7 flex flex-col gap-3 sm:flex-row">
              <a
                href="#generator"
                className="inline-flex items-center justify-center gap-2 rounded-full bg-primary px-6 py-3.5 font-semibold text-primary-foreground transition hover:opacity-90"
              >
                Create free preview
                <ArrowRight className="h-4 w-4" />
              </a>
              <a
                href="#preview"
                className="inline-flex items-center justify-center rounded-full border border-border bg-card px-6 py-3.5 font-semibold text-foreground transition hover:bg-secondary"
              >
                See example book
              </a>
            </div>

            <div className="mt-6 flex flex-wrap gap-x-5 gap-y-2 text-sm text-muted-foreground">
              {["No card needed", "Photo optional", "$4.99 once", "PDF included"].map((item) => (
                <span key={item} className="inline-flex items-center gap-1.5">
                  <Check className="h-4 w-4 text-primary" />
                  {item}
                </span>
              ))}
            </div>
          </div>

          <div className="min-w-0 animate-rise [animation-delay:120ms]">
            <div className="overflow-hidden rounded-3xl border border-border bg-card shadow-card">
              <img
                src={heroImg}
                alt="Personalized children's adventure book open with airplane, dinosaur, space and pirate illustrations"
                width={1536}
                height={1152}
                className="h-auto w-full"
              />
              <div className="hidden gap-3 border-t border-border bg-card p-4 sm:grid sm:grid-cols-3">
                <div className="flex items-start gap-2">
                  <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                  <p className="text-xs leading-relaxed text-muted-foreground">
                    Parent-controlled preview before purchase.
                  </p>
                </div>
                <div className="flex items-start gap-2">
                  <BookOpen className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                  <p className="text-xs leading-relaxed text-muted-foreground">
                    Built for read-aloud bedtime moments.
                  </p>
                </div>
                <div className="flex items-start gap-2">
                  <Printer className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                  <p className="text-xs leading-relaxed text-muted-foreground">
                    Download and print the finished PDF.
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
