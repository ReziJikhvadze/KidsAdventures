import { Lock, Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";

type LockedChapterCardProps = {
  chapterIndex: number;
  childName?: string;
  onDismiss: () => void;
  className?: string;
};

/** Warm cliffhanger shown when a child taps a chapter that is still locked behind the previous one. */
export function LockedChapterCard({
  chapterIndex,
  childName,
  onDismiss,
  className,
}: LockedChapterCardProps) {
  const hero = childName?.trim() || "your hero";
  return (
    <div
      className={cn(
        "rounded-3xl border border-border bg-card p-6 text-center shadow-soft",
        className,
      )}
    >
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <Lock className="h-6 w-6" />
      </div>
      <h3 className="font-display text-xl font-semibold">Chapter {chapterIndex + 1} is still ahead</h3>
      <p className="mt-2 text-sm text-muted-foreground">
        Something exciting is waiting here for {hero} — but the trail isn&apos;t clear yet. Finish
        Chapter {chapterIndex} first, then this part of the map lights up.
      </p>
      <div className="mt-5 flex justify-center">
        <button
          type="button"
          onClick={onDismiss}
          className="inline-flex min-h-11 items-center justify-center gap-2 rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground"
        >
          <Sparkles className="h-4 w-4" />
          Keep exploring
        </button>
      </div>
    </div>
  );
}
