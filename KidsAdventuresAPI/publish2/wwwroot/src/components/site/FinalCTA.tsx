import { ArrowRight } from "lucide-react";

export function FinalCTA() {
  return (
    <section className="relative py-24 md:py-32">
      <div className="mx-auto max-w-6xl px-6">
        <div className="relative overflow-hidden rounded-[2.5rem] bg-foreground text-background p-10 md:p-16">
          <div className="absolute inset-0 bg-hero-glow opacity-30 pointer-events-none" />
          <div className="relative max-w-2xl">
            <h2 className="font-display text-4xl md:text-6xl font-bold leading-tight text-balance">
              Their next adventure is one click away.
            </h2>
            <p className="mt-5 text-lg text-background/70">
              Create your first personalized adventure pack free. No credit card needed.
            </p>
            <div className="mt-8 flex flex-col sm:flex-row gap-3">
              <a
                href="#generator"
                className="inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground px-6 py-3.5 font-semibold hover:opacity-90 transition"
              >
                Create your book
                <ArrowRight className="h-4 w-4" />
              </a>
              <a
                href="#preview"
                className="inline-flex items-center justify-center rounded-full bg-background/10 border border-background/20 text-background px-6 py-3.5 font-semibold hover:bg-background/15 transition"
              >
                View example
              </a>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
