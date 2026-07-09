import { Wand2 } from "lucide-react";
import { cn } from "@/lib/utils";

type GeneratingChapterCardProps = {
  chapterIndex: number;
  progress: number;
  childName?: string | null;
  className?: string;
};

/** Warm, "operational transparency" messages that reassure a waiting parent instead of a blank spinner. */
function stagedMessage(progress: number, childName?: string | null): string {
  const hero = childName?.trim() || "your hero";
  if (progress < 30) return `Dreaming up ${hero}'s next adventure…`;
  if (progress < 55) return `Weaving ${hero}'s personality into the story…`;
  if (progress < 80) return "Painting the pictures, page by page…";
  if (progress < 96) return "Adding the finishing sparkle…";
  return "Almost ready — turning to the first page!";
}

export function GeneratingChapterCard({
  chapterIndex,
  progress,
  childName,
  className,
}: GeneratingChapterCardProps) {
  const clamped = Math.min(100, Math.max(4, progress));
  return (
    <div
      className={cn(
        "rounded-3xl border border-border bg-card p-6 text-center shadow-soft",
        className,
      )}
    >
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
        <Wand2 className="h-6 w-6 motion-safe:animate-float motion-reduce:animate-none" />
      </div>
      <h3 className="font-display text-xl font-semibold">
        Creating Chapter {chapterIndex + 1}…
      </h3>
      <p className="mt-2 min-h-[2.5rem] text-sm text-muted-foreground transition-opacity">
        {stagedMessage(clamped, childName)}
      </p>
      <div className="mx-auto mt-4 h-2 w-full max-w-xs overflow-hidden rounded-full bg-secondary">
        <div
          className="h-full rounded-full bg-primary transition-[width] duration-500"
          style={{ width: `${clamped}%` }}
        />
      </div>
      <p className="mt-3 text-xs text-muted-foreground">
        You can leave this page — we&apos;ll keep it safe in Story Path and My Books.
      </p>
    </div>
  );
}
