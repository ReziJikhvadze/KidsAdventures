import { Award } from "lucide-react";
import type { StoryPathAchievement } from "@/lib/api/story-path";
import type { ThemeType } from "@/lib/api/types";
import { STORY_THEMES } from "@/lib/themes";
import { cn } from "@/lib/utils";

type AchievementsShelfProps = {
  achievements: StoryPathAchievement[];
  className?: string;
};

/** Aspirational hint shown on badges the child hasn't earned yet (warmer than a flat "Not yet"). */
const LOCKED_HINT: Record<ThemeType, string> = {
  Airplanes: "Finish the sky saga",
  Dinosaurs: "Finish the dino saga",
  Space: "Finish the space saga",
  Pirates: "Finish the pirate saga",
  Animals: "Finish the safari saga",
};

export function AchievementsShelf({ achievements, className }: AchievementsShelfProps) {
  const earnedThemes = new Set(achievements.map((a) => a.theme));
  const earnedCount = earnedThemes.size;
  const total = STORY_THEMES.length;

  return (
    <section className={cn("rounded-3xl border border-border bg-card p-5 shadow-soft", className)}>
      <div className="mb-4 flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Award className="h-5 w-5 text-primary" />
          <h2 className="font-display text-lg font-semibold">Adventure badges</h2>
        </div>
        <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-bold text-primary">
          {earnedCount} of {total} earned
        </span>
      </div>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {STORY_THEMES.map((theme) => {
          const earned = achievements.find((a) => a.theme === theme.apiTheme);
          const locked = !earnedThemes.has(theme.apiTheme);
          return (
            <div
              key={theme.id}
              className={cn(
                "flex flex-col items-center gap-2 rounded-2xl border px-3 py-4 text-center transition",
                earned
                  ? "border-primary/30 bg-primary/5"
                  : "border-dashed border-border bg-muted/20",
              )}
              style={
                earned
                  ? { background: `color-mix(in oklab, ${theme.tint} 18%, var(--card))` }
                  : undefined
              }
            >
              <div
                className={cn(
                  "flex h-12 w-12 items-center justify-center rounded-full text-lg font-bold",
                  earned ? "bg-card shadow-soft" : "bg-muted text-muted-foreground/70",
                )}
              >
                {earned ? "★" : "☆"}
              </div>
              <p
                className={cn(
                  "text-xs font-semibold",
                  earned ? "text-foreground" : "text-muted-foreground",
                )}
              >
                {earned?.label ?? theme.name}
              </p>
              <p className="text-[10px] text-muted-foreground">
                {locked ? LOCKED_HINT[theme.apiTheme] : "Earned!"}
              </p>
            </div>
          );
        })}
      </div>
    </section>
  );
}
