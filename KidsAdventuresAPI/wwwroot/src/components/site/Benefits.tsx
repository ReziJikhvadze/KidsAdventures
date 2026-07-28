import { BookHeart, GraduationCap, Heart, ShieldCheck } from "lucide-react";

const items = [
  { icon: BookHeart, title: "Bedtime that feels personal", desc: "A story where your child hears their own name and feels like the brave main character." },
  {
    icon: GraduationCap,
    title: "Learning without worksheets",
    desc: "Vocabulary, memory, and comprehension grow naturally because the story matters to them.",
  },
  {
    icon: Heart,
    title: "Confidence in small moments",
    desc: "The adventure can model courage, kindness, curiosity, and problem-solving in kid-sized choices.",
  },
  { icon: ShieldCheck, title: "Parent-first preview", desc: "Try the opening for free before paying, printing, or sharing it with your child." },
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
            More than a cute book. A reason to read together.
          </h2>
          <p className="mt-4 text-muted-foreground text-pretty">
            Parents are not buying pages. They are buying a tiny ritual: their child feeling seen,
            reading one more page, and keeping a story that belongs to them.
          </p>
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
