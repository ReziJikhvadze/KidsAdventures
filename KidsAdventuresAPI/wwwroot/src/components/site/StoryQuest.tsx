import { Brain, CheckCircle2, Compass, Heart, Sparkles } from "lucide-react";

const questMoments = [
  {
    icon: Heart,
    title: "Kindness choices",
    desc: "Small decisions help children notice feelings, friendship, and gentle courage.",
  },
  {
    icon: Brain,
    title: "Reading comprehension",
    desc: "Each stop asks them to remember what happened and choose what the hero should do next.",
  },
  {
    icon: Compass,
    title: "A reason to reread",
    desc: "The map, treasures, and story stops make the book feel alive without turning bedtime into noisy screen time.",
  },
];

export function StoryQuest() {
  return (
    <section className="relative overflow-hidden py-20 md:py-28">
      <div className="mx-auto grid max-w-7xl items-center gap-10 px-6 lg:grid-cols-[0.95fr_1.05fr] lg:gap-16">
        <div className="min-w-0">
          <p className="inline-flex items-center gap-2 rounded-full border border-primary/20 bg-primary/5 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-primary">
            <Sparkles className="h-3.5 w-3.5" />
            Story Quest
          </p>
          <h2 className="mt-4 font-display text-3xl font-bold leading-tight text-balance sm:text-4xl md:text-5xl">
            Not another game. A story your child gets to think through.
          </h2>
          <p className="mt-4 max-w-xl text-base text-muted-foreground text-pretty sm:text-lg">
            After the book begins, your child can explore a gentle quest map with choices that build
            confidence, memory, and problem-solving. Parents still get the keepsake book. Kids get a
            reason to care.
          </p>
          <div className="mt-7 grid gap-3">
            {questMoments.map((item) => {
              const Icon = item.icon;
              return (
                <div key={item.title} className="flex gap-3">
                  <span className="mt-0.5 grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
                    <Icon className="h-5 w-5" />
                  </span>
                  <div>
                    <h3 className="font-display text-base font-semibold">{item.title}</h3>
                    <p className="mt-0.5 text-sm leading-relaxed text-muted-foreground">
                      {item.desc}
                    </p>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        <div className="relative min-w-0">
          <div className="absolute -inset-3 rounded-[2rem] bg-[color:var(--sky-soft)]/45" />
          <div className="relative overflow-hidden rounded-3xl border border-border bg-card shadow-card">
            <div className="bg-foreground px-5 py-4 text-background">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-background/60">
                    Adventure map
                  </p>
                  <p className="font-display text-xl font-semibold">The Courage Trail</p>
                </div>
                <span className="inline-flex items-center gap-1 rounded-full bg-background/12 px-3 py-1 text-xs font-semibold">
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Parent-friendly
                </span>
              </div>
            </div>
            <div className="relative aspect-[4/3] overflow-hidden bg-[linear-gradient(135deg,#e7f6ff_0%,#fff7d8_46%,#e8f8ee_100%)] p-6">
              <svg viewBox="0 0 520 360" className="h-full w-full" aria-hidden>
                <path
                  d="M62 284 C138 210, 120 124, 212 132 S332 250, 424 170"
                  fill="none"
                  stroke="rgba(41,52,89,.22)"
                  strokeLinecap="round"
                  strokeDasharray="2 24"
                  strokeWidth="18"
                />
                <path
                  d="M62 284 C138 210, 120 124, 212 132 S332 250, 424 170"
                  fill="none"
                  stroke="rgba(222,94,62,.88)"
                  strokeLinecap="round"
                  strokeDasharray="2 22"
                  strokeWidth="7"
                />
                {[
                  { x: 62, y: 284, label: "1" },
                  { x: 210, y: 132, label: "2" },
                  { x: 326, y: 232, label: "3" },
                  { x: 424, y: 170, label: "4" },
                ].map((node) => (
                  <g key={node.label}>
                    <circle cx={node.x} cy={node.y} r="28" fill="white" opacity="0.92" />
                    <circle cx={node.x} cy={node.y} r="20" fill="#de5e3e" />
                    <text
                      x={node.x}
                      y={node.y + 6}
                      fill="white"
                      fontSize="18"
                      fontWeight="800"
                      textAnchor="middle"
                    >
                      {node.label}
                    </text>
                  </g>
                ))}
              </svg>
              <div className="absolute bottom-5 left-5 right-5 rounded-2xl border border-white/70 bg-white/78 p-4 shadow-soft backdrop-blur">
                <p className="text-xs font-semibold uppercase tracking-wide text-primary">
                  Tiny choice
                </p>
                <p className="mt-1 font-display text-lg font-semibold text-foreground">
                  Should the hero share the glowing compass?
                </p>
                <div className="mt-3 grid grid-cols-2 gap-2 text-xs font-semibold">
                  <span className="rounded-full bg-primary px-3 py-2 text-center text-primary-foreground">
                    Help a friend
                  </span>
                  <span className="rounded-full border border-border bg-card px-3 py-2 text-center text-muted-foreground">
                    Keep exploring
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
