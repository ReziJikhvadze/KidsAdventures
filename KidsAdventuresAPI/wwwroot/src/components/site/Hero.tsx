import heroImg from "@/assets/hero.jpg";
import { ArrowRight, PlayCircle, Star } from "lucide-react";

export function Hero() {
  return (
    <section className="relative overflow-hidden">
      <div className="absolute inset-0 bg-hero-glow opacity-90 pointer-events-none" />
      <div className="absolute inset-0 bg-grid opacity-40 [mask-image:radial-gradient(ellipse_at_center,black,transparent_70%)] pointer-events-none" />

      <div className="relative mx-auto max-w-7xl px-6 pt-16 pb-20 md:pt-24 md:pb-28">
        <div className="grid lg:grid-cols-[1.05fr_1fr] gap-12 items-center">
          <div className="animate-rise">
            <div className="inline-flex items-center gap-2 rounded-full border border-border bg-card/70 px-3 py-1 text-xs font-medium text-muted-foreground">
              <span className="inline-flex h-1.5 w-1.5 rounded-full bg-primary" />
              Personalized children's books · ages 3–12
            </div>
            <h1 className="mt-5 font-display text-5xl md:text-6xl lg:text-7xl font-bold leading-[1.02] text-balance">
              Kids adventure books starring{" "}
              <span className="relative inline-block">
                <span className="relative z-10">your child</span>
                <span className="absolute inset-x-0 bottom-1 h-3 bg-[color:var(--sun)]/70 -z-0 rounded" />
              </span>
              .
            </h1>
            <p className="mt-6 text-lg md:text-xl text-muted-foreground max-w-xl text-pretty">
              Custom illustrated storybooks for child education, bedtime reading, and screen-free
              parenting — create a free preview in minutes, print the PDF when you are ready.
            </p>

            <div id="cta" className="mt-8 flex flex-col sm:flex-row gap-3">
              <a
                href="#generator"
                className="group inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground px-6 py-3.5 font-semibold shadow-soft hover:translate-y-[-1px] transition"
              >
                Create your book
                <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
              </a>
              <a
                href="#preview"
                className="inline-flex items-center justify-center gap-2 rounded-full bg-card border border-border px-6 py-3.5 font-semibold hover:bg-secondary transition"
              >
                <PlayCircle className="h-4 w-4" />
                View Example
              </a>
            </div>

            <div className="mt-8 flex items-center gap-5 text-sm text-muted-foreground">
              <div className="flex items-center gap-1">
                {Array.from({ length: 5 }).map((_, i) => (
                  <Star
                    key={i}
                    className="h-4 w-4 fill-[color:var(--sun)] text-[color:var(--sun)]"
                  />
                ))}
              </div>
              <span>Loved by parents for screen-free learning & storytime</span>
            </div>
          </div>

          <div className="relative animate-rise [animation-delay:120ms]">
            <div className="absolute -inset-6 rounded-[2rem] bg-[color:var(--sky-soft)]/60 -rotate-2" />
            <div className="relative rounded-[2rem] bg-card shadow-card overflow-hidden border border-border">
              <img
                src={heroImg}
                alt="Personalized children's adventure book open with airplane, dinosaur, space and pirate illustrations"
                width={1536}
                height={1152}
                className="w-full h-auto"
              />
            </div>
            <div className="absolute -bottom-5 -left-5 rounded-2xl bg-card border border-border shadow-card px-4 py-3 flex items-center gap-3 animate-float">
              <div className="h-9 w-9 rounded-full bg-[color:var(--mint)]/40 grid place-items-center font-display font-bold">
                ✓
              </div>
              <div className="text-sm">
                <div className="font-semibold">Pack ready</div>
                <div className="text-muted-foreground text-xs">42 seconds</div>
              </div>
            </div>
            <div className="absolute -top-4 -right-3 rounded-full bg-primary text-primary-foreground text-xs font-semibold px-3 py-1.5 shadow-soft rotate-6">
              For ages 3–12
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
