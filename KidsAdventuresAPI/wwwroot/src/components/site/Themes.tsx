import { Link } from "@tanstack/react-router";
import { STORY_THEMES, isStoryThemeId, type StoryThemeId } from "@/lib/themes";

export function Themes() {
  return (
    <section id="themes" className="relative py-24 md:py-32 bg-secondary/40">
      <div className="mx-auto max-w-7xl px-6">
        <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">Themes</p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              Kids adventure book themes — dinosaurs, space & more
            </h2>
          </div>
          <p className="text-muted-foreground max-w-md">
            Each theme is a personalized children's book with your child's name on every page.{" "}
            <Link to="/themes" className="text-primary font-semibold hover:underline">
              View all themes →
            </Link>
          </p>
        </div>

        <div className="mt-12 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-5">
          {STORY_THEMES.map((t) => (
            <Link
              key={t.name}
              to="/themes/$slug"
              params={{ slug: t.slug }}
              className="group relative rounded-3xl bg-card border border-border overflow-hidden shadow-soft hover:shadow-card hover:-translate-y-1 transition"
            >
              <div
                className="aspect-square p-4"
                style={{ background: `color-mix(in oklab, ${t.tint} 35%, var(--card))` }}
              >
                <img
                  src={t.image}
                  alt={`${t.name} theme illustration`}
                  loading="lazy"
                  width={768}
                  height={768}
                  className="w-full h-full object-contain group-hover:scale-105 transition duration-500"
                />
              </div>
              <div className="p-4">
                <div className="font-display text-lg font-semibold">{t.name}</div>
                <div className="text-sm text-muted-foreground">{t.shortDesc}</div>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </section>
  );
}
