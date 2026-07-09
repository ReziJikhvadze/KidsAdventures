import { BookOpen, Lock } from "lucide-react";
import { fetchIllustrationObjectUrl } from "@/lib/api/adventure-packs";
import { useAuthedImageUrl } from "@/hooks/useAuthedImageUrl";
import type { ThemeType } from "@/lib/api/types";
import { STORY_THEMES } from "@/lib/themes";
import { cn } from "@/lib/utils";

type PackCoverProps = {
  packId: string;
  theme: ThemeType;
  /** True once page 1 has a real illustration (a paid/free-unlocked book). */
  hasCover: boolean;
  /** True when the story text exists but illustrations are still locked behind a purchase. */
  locked?: boolean;
  className?: string;
};

/**
 * Small bookshelf thumbnail for a story row. Shows the real illustrated cover when available,
 * otherwise a theme-tinted "locked" plate so an un-illustrated book still looks like a book
 * waiting to be unlocked.
 */
export function PackCover({ packId, theme, hasCover, locked, className }: PackCoverProps) {
  const tint = STORY_THEMES.find((t) => t.apiTheme === theme)?.tint ?? "var(--primary)";
  const coverUrl = useAuthedImageUrl(
    hasCover ? `/api/adventure-packs/${packId}/illustrations/0` : null,
    fetchIllustrationObjectUrl,
  );

  return (
    <div
      className={cn(
        "relative h-20 w-16 shrink-0 overflow-hidden rounded-xl border border-border shadow-sm sm:h-24 sm:w-20",
        className,
      )}
      style={{ background: `color-mix(in oklab, ${tint} 30%, var(--card))` }}
    >
      {hasCover && coverUrl ? (
        <img src={coverUrl} alt="" className="h-full w-full object-cover" aria-hidden />
      ) : (
        <div className="grid h-full w-full place-items-center">
          {locked ? (
            <Lock className="h-6 w-6 text-foreground/40" aria-hidden />
          ) : (
            <BookOpen className="h-6 w-6 text-foreground/40" aria-hidden />
          )}
        </div>
      )}
      {locked && hasCover && (
        <div className="absolute inset-0 grid place-items-center bg-background/40 backdrop-blur-[3px]">
          <Lock className="h-6 w-6 text-foreground/70" aria-hidden />
        </div>
      )}
    </div>
  );
}
