import airplanes from "@/assets/theme-airplanes.jpg";
import dinosaurs from "@/assets/theme-dinosaurs.jpg";
import space from "@/assets/theme-space.jpg";
import pirates from "@/assets/theme-pirates.jpg";
import animals from "@/assets/theme-animals.jpg";

const themes = [
  { name: "Airplanes", desc: "Take to the skies", img: airplanes, tint: "var(--sky-soft)" },
  { name: "Dinosaurs", desc: "Roar into the past", img: dinosaurs, tint: "var(--mint)" },
  { name: "Space", desc: "Explore the stars", img: space, tint: "var(--accent)" },
  { name: "Pirates", desc: "Hunt the treasure", img: pirates, tint: "var(--sun)" },
  { name: "Animals", desc: "Meet the wild", img: animals, tint: "var(--sun)" },
];

export function Themes() {
  return (
    <section id="themes" className="relative py-24 md:py-32 bg-secondary/40">
      <div className="mx-auto max-w-7xl px-6">
        <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">Themes</p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              Pick the world they'll fall in love with.
            </h2>
          </div>
          <p className="text-muted-foreground max-w-md">
            Each theme blends story, puzzles and printable activities — and is personalized with
            your child's name on every page.
          </p>
        </div>

        <div className="mt-12 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-5">
          {themes.map((t) => (
            <div
              key={t.name}
              className="group relative rounded-3xl bg-card border border-border overflow-hidden shadow-soft hover:shadow-card hover:-translate-y-1 transition"
            >
              <div
                className="aspect-square p-4"
                style={{ background: `color-mix(in oklab, ${t.tint} 35%, var(--card))` }}
              >
                <img
                  src={t.img}
                  alt={`${t.name} theme illustration`}
                  loading="lazy"
                  width={768}
                  height={768}
                  className="w-full h-full object-contain group-hover:scale-105 transition duration-500"
                />
              </div>
              <div className="p-4">
                <div className="font-display text-lg font-semibold">{t.name}</div>
                <div className="text-sm text-muted-foreground">{t.desc}</div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
