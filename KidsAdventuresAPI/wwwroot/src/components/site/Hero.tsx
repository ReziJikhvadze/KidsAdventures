import heroImg from "@/assets/hero.jpg";
import { ArrowRight, BookOpen, Moon, Printer, Sparkles } from "lucide-react";

const VALUE_PROPS = [
  {
    icon: Sparkles,
    title: "Your child is the hero",
    desc: "Their name, age and photo become a one-of-a-kind illustrated character.",
  },
  {
    icon: Moon,
    title: "Made for bedtime",
    desc: "Gentle, screen-free stories that wind the day down instead of winding kids up.",
  },
  {
    icon: BookOpen,
    title: "Learning through play",
    desc: "Every adventure quietly weaves in courage, kindness and curiosity.",
  },
  {
    icon: Printer,
    title: "Keep it forever",
    desc: "Download a printable PDF and turn it into a real keepsake book.",
  },
] as const;

export function Hero() {
  return (
    <section className="relative border-t border-border bg-secondary/20">
      <div className="mx-auto max-w-7xl px-6 py-16 md:py-24">
        <div className="grid items-center gap-10 lg:grid-cols-2 lg:gap-16">
          {/* Showcase image */}
          <div className="min-w-0 animate-rise">
            <div className="overflow-hidden rounded-3xl border border-border bg-card shadow-card">
              <img
                src={heroImg}
                alt="Personalized children's adventure book open with airplane, dinosaur, space and pirate illustrations"
                width={1536}
                height={1152}
                className="h-auto w-full"
              />
            </div>
          </div>

          {/* Supporting copy + value props */}
          <div className="min-w-0 animate-rise [animation-delay:120ms]">
            <p className="inline-flex items-center gap-2 rounded-full border border-primary/20 bg-primary/5 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-primary">
              <Sparkles className="h-3.5 w-3.5" />
              See what you get
            </p>
            <h2 className="mt-4 font-display text-3xl font-bold leading-tight text-balance sm:text-4xl md:text-5xl">
              A real picture book, starring{" "}
              <span className="relative inline-block">
                <span className="relative z-10">your child</span>
                <span className="absolute inset-x-0 bottom-1 -z-0 h-3 rounded bg-[color:var(--sun)]/70" />
              </span>
              .
            </h2>
            <p className="mt-4 max-w-xl text-base text-muted-foreground text-pretty sm:text-lg">
              Custom illustrated storybooks for child education, bedtime reading and screen-free
              parenting — preview it free in minutes, print the PDF whenever you are ready.
            </p>

            <ul className="mt-8 grid gap-x-6 gap-y-5 sm:grid-cols-2">
              {VALUE_PROPS.map((prop) => {
                const Icon = prop.icon;
                return (
                  <li key={prop.title} className="flex items-start gap-3">
                    <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
                      <Icon className="h-5 w-5" />
                    </span>
                    <div className="min-w-0">
                      <div className="font-display font-semibold leading-tight">{prop.title}</div>
                      <p className="mt-0.5 text-sm text-muted-foreground">{prop.desc}</p>
                    </div>
                  </li>
                );
              })}
            </ul>

            <a
              href="#generator"
              className="group mt-8 inline-flex items-center gap-2 font-semibold text-primary transition hover:gap-3"
            >
              Create your book
              <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
