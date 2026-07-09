import type { StoryTheme } from "@/lib/themes";

const ADVENTURE_TITLE: Record<string, string> = {
  space: "Cosmic Adventure",
  dinosaurs: "Dino Adventure",
  airplanes: "Sky Adventure",
  pirates: "Treasure Adventure",
  animals: "Wild Adventure",
};

type ThemeCoverPreviewProps = {
  theme: StoryTheme;
  childName?: string;
  className?: string;
};

/**
 * Instant “wow” book cover when a theme is picked — before name/photo.
 * Uses the theme art as a full-bleed cover with premium title treatment.
 */
export function ThemeCoverPreview({ theme, childName, className = "" }: ThemeCoverPreviewProps) {
  const adventure = ADVENTURE_TITLE[theme.id] ?? "Adventure";
  const heroLabel = childName?.trim()
    ? `${childName.trim()}'s ${adventure}`
    : `Your Child's ${adventure}`;

  return (
    <div
      className={`relative w-full max-w-[220px] overflow-hidden rounded-2xl border border-border shadow-card aspect-[3/4] animate-rise ${className}`}
      aria-label={`Preview cover: ${heroLabel}`}
    >
      <img
        src={theme.image}
        alt=""
        className="absolute inset-0 h-full w-full object-cover"
      />
      <div className="absolute inset-0 bg-gradient-to-t from-black/75 via-black/25 to-black/10" />
      <div className="absolute inset-x-0 top-0 flex justify-between p-2.5">
        <span className="rounded-full bg-white/90 px-2 py-0.5 text-[9px] font-bold uppercase tracking-wide text-foreground/80 shadow-sm">
          Preview
        </span>
        <span className="rounded-full bg-black/40 px-2 py-0.5 text-[9px] font-semibold text-white/90 backdrop-blur-sm">
          {theme.name}
        </span>
      </div>
      <div className="absolute inset-x-0 bottom-0 p-3.5 text-left">
        <p className="font-display text-[15px] font-semibold leading-snug tracking-tight text-white drop-shadow-sm sm:text-base">
          {heroLabel}
        </p>
        <p className="mt-1 text-[10px] font-medium text-white/75">
          Illustrated storybook · starring them
        </p>
      </div>
    </div>
  );
}
