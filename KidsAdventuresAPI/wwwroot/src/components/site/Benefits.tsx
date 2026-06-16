import { Tv, GraduationCap, Heart, Timer } from "lucide-react";

const items = [
  { icon: Tv, title: "Screen-free parenting", desc: "Adventure books away from tablets — real pages kids hold and re-read." },
  {
    icon: GraduationCap,
    title: "Child education & literacy",
    desc: "Vocabulary, comprehension, and learning through stories they care about.",
  },
  {
    icon: Heart,
    title: "Personalized for every child",
    desc: "Their name, age, and photo as the hero of every adventure book.",
  },
  { icon: Timer, title: "Ready in minutes", desc: "Generate, read online, print a PDF — perfect for busy parents." },
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
            Personalized kids books for learning, play & parenting wins.
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
