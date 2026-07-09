import { Sparkles } from "lucide-react";
import type { StoryPathAchievement } from "@/lib/api/story-path";
import { cn } from "@/lib/utils";

type WorldCompleteCelebrationProps = {
  achievement: StoryPathAchievement;
  themeLabel: string;
  className?: string;
};

export function WorldCompleteCelebration({
  achievement,
  themeLabel,
  className,
}: WorldCompleteCelebrationProps) {
  return (
    <div
      className={cn(
        "rounded-3xl border border-primary/25 px-6 py-8 text-center shadow-card motion-safe:animate-rise motion-reduce:animate-none",
        className,
      )}
      style={{
        background:
          "linear-gradient(160deg, color-mix(in oklab, var(--mint) 25%, var(--card)) 0%, var(--card) 70%)",
      }}
      role="status"
    >
      <div className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-full bg-primary/15 text-primary">
        <Sparkles className="h-7 w-7" />
      </div>
      <p className="text-xs font-semibold uppercase tracking-wide text-primary">World complete</p>
      <h2 className="mt-1 font-display text-2xl font-semibold">{themeLabel} conquered!</h2>
      <p className="mt-2 text-sm text-muted-foreground">
        New badge: <span className="font-semibold text-foreground">{achievement.label}</span>
      </p>
    </div>
  );
}
