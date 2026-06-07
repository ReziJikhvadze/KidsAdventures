import { Tv, GraduationCap, Heart, Timer } from "lucide-react";

const items = [
  { icon: Tv, title: "Screen-free play", desc: "Hours away from tablets and TV." },
  {
    icon: GraduationCap,
    title: "Educational & fun",
    desc: "Reading, problem-solving and creativity in one.",
  },
  {
    icon: Heart,
    title: "Personal for every child",
    desc: "Their name, age and interests on every page.",
  },
  { icon: Timer, title: "Ready in under a minute", desc: "Generate, print, and you're set to go." },
];

export function Benefits() {
  return (
    <section className="relative py-24 md:py-32 bg-secondary/40">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">
            Why parents love it
          </p>
          <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            Made to delight kids — and give you a break.
          </h2>
        </div>

        <div className="mt-14 grid sm:grid-cols-2 lg:grid-cols-4 gap-5">
          {items.map((it) => (
            <div
              key={it.title}
              className="rounded-3xl bg-card border border-border p-6 shadow-soft"
            >
              <div className="h-11 w-11 rounded-xl bg-[color:var(--sky-soft)]/70 grid place-items-center">
                <it.icon className="h-5 w-5 text-foreground" />
              </div>
              <h3 className="mt-5 font-display text-xl font-semibold">{it.title}</h3>
              <p className="mt-1 text-muted-foreground text-sm">{it.desc}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
