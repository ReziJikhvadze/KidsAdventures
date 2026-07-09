import type { AvatarConfig } from "@/lib/avatar/config";
import type { StoryTheme } from "@/lib/themes";
import { PixarAvatarPreview } from "@/components/avatar/PixarAvatarPreview";

const ADVENTURE_TITLE: Record<string, string> = {
  space: "Cosmic Adventure",
  dinosaurs: "Dino Adventure",
  airplanes: "Sky Adventure",
  pirates: "Treasure Adventure",
  animals: "Wild Adventure",
};

const SCENE_HINT: Record<string, string> = {
  space: "In the cockpit · stars ahead",
  dinosaurs: "Peeking through the jungle",
  airplanes: "Ready for takeoff",
  pirates: "On the treasure deck",
  animals: "Among the wild friends",
};

type HeroInScenePreviewProps = {
  theme: StoryTheme;
  config: AvatarConfig;
  childName?: string;
  className?: string;
};

/**
 * Kills the “sticker on white card” effect — hero composited into
 * a theme environment (cockpit / jungle / deck) for instant story context.
 */
export function HeroInScenePreview({
  theme,
  config,
  childName,
  className = "",
}: HeroInScenePreviewProps) {
  const adventure = ADVENTURE_TITLE[theme.id] ?? "Adventure";
  const title = childName?.trim()
    ? `${childName.trim()}'s ${adventure}`
    : `Your Child's ${adventure}`;

  return (
    <div
      className={`hero-in-scene ${className}`}
      data-theme={theme.id}
      aria-label={`${title} — hero in scene`}
    >
      <img src={theme.image} alt="" className="hero-in-scene-bg" />
      <div className="hero-in-scene-veil" />

      {/* Theme-specific foreground props */}
      <div className="hero-in-scene-props" aria-hidden>
        {theme.id === "space" && (
          <>
            <div className="scene-cockpit-rail" />
            <div className="scene-cockpit-glass" />
            <div className="scene-star scene-star-a" />
            <div className="scene-star scene-star-b" />
            <div className="scene-star scene-star-c" />
          </>
        )}
        {theme.id === "dinosaurs" && (
          <>
            <div className="scene-leaf scene-leaf-l" />
            <div className="scene-leaf scene-leaf-r" />
          </>
        )}
        {theme.id === "pirates" && <div className="scene-deck-rail" />}
        {theme.id === "airplanes" && <div className="scene-wing" />}
        {theme.id === "animals" && <div className="scene-meadow" />}
      </div>

      <div className="hero-in-scene-character">
        <PixarAvatarPreview
          config={config}
          childName={childName ?? ""}
          size="compact"
          className="hero-in-scene-avatar"
        />
      </div>

      <div className="hero-in-scene-chrome">
        <span className="hero-in-scene-badge">Live preview</span>
        <span className="hero-in-scene-theme">{theme.name}</span>
      </div>
      <div className="hero-in-scene-copy">
        <p className="hero-in-scene-title">{title}</p>
        <p className="hero-in-scene-hint">{SCENE_HINT[theme.id] ?? "In their adventure"}</p>
      </div>
    </div>
  );
}
