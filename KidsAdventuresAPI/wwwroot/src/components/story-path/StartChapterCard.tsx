import { Loader2, Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";

type StartChapterCardProps = {
  chapterIndex: number;
  themeLabel: string;
  childName?: string;
  starting?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  className?: string;
};

export function StartChapterCard({
  chapterIndex,
  themeLabel,
  childName,
  starting,
  onConfirm,
  onCancel,
  className,
}: StartChapterCardProps) {
  return (
    <div
      className={cn(
        "rounded-3xl border border-border bg-card p-6 text-center shadow-soft",
        className,
      )}
    >
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
        <Sparkles className="h-6 w-6" />
      </div>
      <h3 className="font-display text-xl font-semibold">
        {chapterIndex === 0 ? "Begin the saga?" : `Ready for Chapter ${chapterIndex + 1}?`}
      </h3>
      <p className="mt-2 text-sm text-muted-foreground">
        {childName ? `${childName} steps into a brand-new` : "A brand-new"} {themeLabel.toLowerCase()}{" "}
        chapter — 6 illustrated pages that pick up right where the story left off.
        {chapterIndex === 0 && (
          <span className="mt-1 block font-semibold text-primary">
            Your first chapter is free and fully illustrated.
          </span>
        )}
      </p>
      <div className="mt-5 flex flex-col items-center gap-3 sm:flex-row sm:justify-center">
        <button
          type="button"
          onClick={onCancel}
          disabled={starting}
          className="inline-flex min-h-11 items-center justify-center rounded-full border border-border px-6 py-2.5 text-sm font-semibold text-foreground disabled:opacity-60"
        >
          Not yet
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={starting}
          className="inline-flex min-h-11 items-center justify-center gap-2 rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-60"
        >
          {starting && <Loader2 className="h-4 w-4 animate-spin" />}
          Yes, start the adventure!
        </button>
      </div>
    </div>
  );
}
