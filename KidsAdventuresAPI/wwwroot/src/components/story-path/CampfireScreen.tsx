import { Flame } from "lucide-react";
import { HoldToConfirmButton } from "@/components/story-path/HoldToConfirmButton";
import { cn } from "@/lib/utils";

type CampfireScreenProps = {
  prompt: string;
  childName?: string;
  themeLabel: string;
  onConfirm: () => void;
  confirming?: boolean;
  className?: string;
};

export function CampfireScreen({
  prompt,
  childName,
  themeLabel,
  onConfirm,
  confirming,
  className,
}: CampfireScreenProps) {
  return (
    <div
      className={cn(
        "relative overflow-hidden rounded-3xl border border-border px-6 py-10 text-center shadow-card sm:px-10",
        className,
      )}
      style={{
        background:
          "linear-gradient(165deg, color-mix(in oklab, var(--sun) 35%, var(--card)) 0%, color-mix(in oklab, var(--primary) 12%, var(--card)) 55%, var(--card) 100%)",
      }}
    >
      <div className="story-path-fireflies pointer-events-none absolute inset-0 motion-reduce:hidden" aria-hidden />
      <div className="relative mx-auto flex max-w-lg flex-col items-center gap-5">
        <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary/15 text-primary">
          <Flame className="h-7 w-7 motion-safe:animate-float motion-reduce:animate-none" />
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Campfire moment
          </p>
          <h2 className="mt-1 font-display text-2xl font-semibold text-foreground">
            {childName ? `Talk with ${childName}` : "Talk together"}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">{themeLabel} adventure</p>
        </div>
        <p className="text-base leading-relaxed text-foreground text-pretty">{prompt}</p>
        <p className="text-xs text-muted-foreground">
          A gentle pause for grown-ups and kids — not a lock, just a checkpoint.
        </p>
        <HoldToConfirmButton onConfirm={onConfirm} disabled={confirming} />
      </div>
    </div>
  );
}
