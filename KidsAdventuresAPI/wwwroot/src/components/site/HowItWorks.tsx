import { UserRound, Palette, Download } from "lucide-react";

const steps = [
  {
    icon: UserRound,
    title: "Enter child details",
    desc: "Tell us your child's name and age so every page feels made just for them.",
  },
  {
    icon: Palette,
    title: "Choose a theme",
    desc: "Airplanes, dinosaurs, space, pirates or animals — pick what they love most.",
  },
  {
    icon: Download,
    title: "Download & print",
    desc: "Your personalized adventure pack is ready to print in under a minute.",
  },
];

export function HowItWorks() {
  return (
    <section id="how" className="relative py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">How it works</p>
          <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            Three steps to an unforgettable afternoon.
          </h2>
        </div>

        <div className="mt-14 grid md:grid-cols-3 gap-6">
          {steps.map((s, i) => (
            <div
              key={s.title}
              className="relative rounded-3xl bg-card border border-border p-8 shadow-soft hover:shadow-card transition group"
            >
              <div className="absolute -top-4 left-8 inline-flex items-center justify-center h-8 w-8 rounded-full bg-foreground text-background text-xs font-bold font-display">
                {i + 1}
              </div>
              <div className="h-12 w-12 rounded-2xl bg-secondary grid place-items-center text-primary group-hover:bg-primary group-hover:text-primary-foreground transition">
                <s.icon className="h-6 w-6" />
              </div>
              <h3 className="mt-5 font-display text-2xl font-semibold">{s.title}</h3>
              <p className="mt-2 text-muted-foreground">{s.desc}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
