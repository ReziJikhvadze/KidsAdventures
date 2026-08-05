import { Link } from "@tanstack/react-router";
import { useCallback, useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  BookOpen,
  ChevronLeft,
  ChevronRight,
  Loader2,
  Lock,
  Maximize2,
  Minimize2,
  Sparkles,
  Type,
  X,
} from "lucide-react";

import {
  Carousel,
  CarouselContent,
  CarouselItem,
  type CarouselApi,
} from "@/components/ui/carousel";
import { fetchIllustrationObjectUrl } from "@/lib/api/adventure-packs";
import type { PreviewIllustrationStatus, StoryPageContent, ThemeType } from "@/lib/api/types";
import { THEME_TINTS } from "@/lib/story/themeTints";
import { cn } from "@/lib/utils";

const FONT_SIZE_KEY = "storybook-font-size";

type FontSize = "sm" | "md" | "lg";
type ReaderVariant = "default" | "fullscreen";

// Sizes scale up at the `sm` breakpoint so phones stay legible/uncramped while desktop keeps the larger type.
const FONT_SIZE_STYLES: Record<FontSize, { body: string; title: string; caption: string }> = {
  sm: {
    body: "text-[13px] sm:text-sm leading-relaxed",
    title: "text-base sm:text-lg",
    caption: "text-lg sm:text-2xl",
  },
  md: {
    body: "text-sm sm:text-base leading-relaxed",
    title: "text-lg sm:text-xl",
    caption: "text-xl sm:text-3xl",
  },
  lg: {
    body: "text-base sm:text-lg leading-relaxed",
    title: "text-xl sm:text-2xl",
    caption: "text-2xl sm:text-4xl",
  },
};

/** New-style pages carry a short caption shown on the image; legacy packs fall back to the text panel. */
function pageCaption(page: StoryPageContent): string {
  return page.caption?.trim() ?? "";
}

export type StoryBookReaderProps = {
  pages: StoryPageContent[];
  theme: ThemeType;
  title: string;
  childName?: string;
  previewIllustrationStatus?: PreviewIllustrationStatus;
  isCompleted?: boolean;
  storiesRemainingThisMonth?: number;
  bookCredits?: number;
  isWelcomeGiftStory?: boolean;
  className?: string;
};

function loadFontSize(): FontSize {
  if (typeof window === "undefined") return "md";
  const stored = localStorage.getItem(FONT_SIZE_KEY);
  return stored === "sm" || stored === "lg" ? stored : "md";
}

function isPublicIllustrationUrl(url: string): boolean {
  // API illustration routes require JWT — never use a bare <img src>.
  if (url.startsWith("/api/")) {
    return false;
  }

  return (
    url.startsWith("/public/") ||
    url.startsWith("/demo/") ||
    url.startsWith("http://") ||
    url.startsWith("https://") ||
    url.startsWith("data:")
  );
}

function CaptionOverlay({
  caption,
  pageIndex,
  totalPages,
  captionClassName,
}: {
  caption: string;
  pageIndex: number;
  totalPages: number;
  captionClassName: string;
}) {
  if (!caption) return null;
  return (
    <div className="pointer-events-none absolute inset-x-0 bottom-0 flex flex-col justify-end rounded-b-2xl bg-gradient-to-t from-black/80 via-black/45 to-transparent px-4 pb-4 pt-12 sm:px-6 sm:pb-6 sm:pt-16">
      <span className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-white/70">
        Page {pageIndex + 1} of {totalPages}
      </span>
      <p
        className={cn(
          "font-display font-bold text-balance text-white drop-shadow-[0_2px_10px_rgba(0,0,0,0.65)]",
          captionClassName,
        )}
      >
        {caption}
      </p>
    </div>
  );
}

function PageIllustration({
  page,
  pageIndex,
  previewStatus,
  themeTint,
  totalPages,
  variant = "default",
  caption = "",
  captionClassName = "",
  onOpenFullscreen,
}: {
  page: StoryPageContent;
  pageIndex: number;
  previewStatus: PreviewIllustrationStatus;
  themeTint: string;
  totalPages: number;
  variant?: ReaderVariant;
  caption?: string;
  captionClassName?: string;
  onOpenFullscreen?: () => void;
}) {
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  // Actively painting only when a paid illustration job is running. Locked pages never reach
  // this component — the reader renders them as a clean text page instead of a lock panel.
  const painting = !page.isIllustrated && previewStatus === "Generating";

  useEffect(() => {
    if (!page.isIllustrated || !page.illustrationUrl) {
      setImageUrl(null);
      return;
    }

    if (isPublicIllustrationUrl(page.illustrationUrl)) {
      setImageUrl(page.illustrationUrl);
      setLoading(false);
      setError(false);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    setLoading(true);
    setError(false);

    void fetchIllustrationObjectUrl(page.illustrationUrl)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        objectUrl = url;
        setImageUrl(url);
      })
      .catch(() => {
        if (!cancelled) setError(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [page.illustrationUrl, page.isIllustrated, previewStatus]);

  const imageClassName =
    variant === "fullscreen"
      ? "w-full max-h-[min(70vh,720px)] object-contain rounded-t-2xl bg-black/5"
      : "aspect-[4/3] w-full object-cover rounded-t-2xl";

  if (loading && !imageUrl && !error) {
    return (
      <div
        className={cn(
          "w-full rounded-t-2xl flex flex-col items-center justify-center gap-2",
          variant === "fullscreen" ? "min-h-[40vh]" : "aspect-[4/3]",
        )}
        style={{ background: `color-mix(in oklab, ${themeTint} 55%, white)` }}
      >
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
        <p className="text-sm font-medium text-muted-foreground">Loading illustration…</p>
      </div>
    );
  }

  if (error && page.isIllustrated) {
    return (
      <div
        className={cn(
          "w-full rounded-t-2xl flex flex-col items-center justify-center gap-2 px-4 text-center",
          variant === "fullscreen" ? "min-h-[40vh]" : "aspect-[4/3]",
        )}
        style={{ background: `color-mix(in oklab, ${themeTint} 40%, white)` }}
      >
        <Sparkles className="h-8 w-8 text-primary/50" />
        <p className="text-sm font-medium text-muted-foreground">Illustration could not load</p>
        <p className="text-xs text-muted-foreground">Refresh the page or try again in a moment</p>
      </div>
    );
  }

  if (imageUrl && !error) {
    return (
      <button
        type="button"
        onClick={onOpenFullscreen}
        className="group relative block w-full rounded-t-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        aria-label="View illustration fullscreen"
      >
        <img src={imageUrl} alt="" className={imageClassName} />
        {variant === "default" && onOpenFullscreen && (
          <span className="absolute right-3 top-3 inline-flex items-center gap-1 rounded-full bg-black/55 px-2.5 py-1 text-xs font-semibold text-white opacity-0 transition group-hover:opacity-100 group-focus-visible:opacity-100">
            <Maximize2 className="h-3.5 w-3.5" />
            Fullscreen
          </span>
        )}
        <CaptionOverlay
          caption={caption}
          pageIndex={pageIndex}
          totalPages={totalPages}
          captionClassName={captionClassName}
        />
      </button>
    );
  }

  if (painting) {
    return (
      <div
        className={cn(
          "w-full rounded-t-2xl flex flex-col items-center justify-center gap-2 px-4 text-center",
          variant === "fullscreen" ? "min-h-[40vh]" : "aspect-[4/3]",
        )}
        style={{ background: `color-mix(in oklab, ${themeTint} 55%, white)` }}
      >
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
        <p className="text-sm font-medium text-muted-foreground">Painting page {pageIndex + 1}…</p>
        <p className="text-xs text-muted-foreground/80">
          Page {pageIndex + 1} of {totalPages} · about 1 minute each
        </p>
      </div>
    );
  }

  return (
    <div
      className={cn(
        "w-full rounded-t-2xl flex items-center justify-center",
        variant === "fullscreen" ? "min-h-[40vh]" : "aspect-[4/3]",
      )}
      style={{ background: `color-mix(in oklab, ${themeTint} 50%, white)` }}
    >
      <Sparkles className="h-8 w-8 text-primary/40" />
    </div>
  );
}

export function StoryBookReader({
  pages,
  theme,
  title,
  childName,
  previewIllustrationStatus = "None",
  isCompleted = false,
  storiesRemainingThisMonth,
  bookCredits = 0,
  isWelcomeGiftStory = false,
  className,
}: StoryBookReaderProps) {
  const [api, setApi] = useState<CarouselApi>();
  const [current, setCurrent] = useState(0);
  const [fontSize, setFontSize] = useState<FontSize>("md");

  useEffect(() => {
    setFontSize(loadFontSize());
  }, []);
  const [isFullscreen, setIsFullscreen] = useState(false);
  // Caption-style pages keep the narration hidden by default so kids read the picture, not the text.
  const [showNarration, setShowNarration] = useState(false);

  const themeTint = THEME_TINTS[theme];
  const typography = FONT_SIZE_STYLES[fontSize];
  const variant: ReaderVariant = isFullscreen ? "fullscreen" : "default";

  const onSelect = useCallback(() => {
    if (!api) return;
    setCurrent(api.selectedScrollSnap());
  }, [api]);

  useEffect(() => {
    if (!api) return;
    onSelect();
    api.on("select", onSelect);
    return () => {
      api.off("select", onSelect);
    };
  }, [api, onSelect]);

  useEffect(() => {
    if (!isFullscreen) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsFullscreen(false);
      }
    };

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [isFullscreen]);

  const cycleFontSize = () => {
    const next: FontSize = fontSize === "sm" ? "md" : fontSize === "md" ? "lg" : "sm";
    setFontSize(next);
    localStorage.setItem(FONT_SIZE_KEY, next);
  };

  const allIllustrated = useMemo(() => pages.every((p) => p.isIllustrated), [pages]);
  const hasAnyCaption = useMemo(() => pages.some((p) => pageCaption(p) !== ""), [pages]);
  // A paid illustration job is running; otherwise un-illustrated pages are simply locked.
  const painting = previewIllustrationStatus === "Generating";
  const someLocked = !allIllustrated && !painting;
  const totalSlides = pages.length;
  const onLastStoryPage = current === pages.length - 1;
  const showPdfUpsell = !isCompleted && allIllustrated && !isFullscreen;
  const showCreditsUpsell =
    allIllustrated &&
    !isFullscreen &&
    typeof storiesRemainingThisMonth === "number" &&
    storiesRemainingThisMonth === 0;
  const showCreditsReminder =
    allIllustrated &&
    !isFullscreen &&
    typeof storiesRemainingThisMonth === "number" &&
    storiesRemainingThisMonth > 0 &&
    bookCredits === 0 &&
    onLastStoryPage;

  if (pages.length === 0) {
    return null;
  }

  const readerShell = (
    <div
      className={cn(
        "w-full max-w-full min-w-0 overflow-hidden",
        isFullscreen &&
          "fixed inset-0 z-[100] overflow-y-auto bg-background pt-[env(safe-area-inset-top)] pb-[env(safe-area-inset-bottom)]",
        !isFullscreen && className,
      )}
    >
      <div
        className={cn(
          "w-full",
          isFullscreen && "mx-auto flex min-h-full max-w-4xl flex-col p-4 sm:p-6 md:p-8",
        )}
      >
        <div className="mb-3 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <p className="truncate text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {childName ? `${childName}'s story` : "Your story"}
            </p>
            <p className="truncate font-display font-semibold">{title}</p>
          </div>
          <div className="flex shrink-0 flex-wrap items-center gap-2">
            {hasAnyCaption && (
              <button
                type="button"
                onClick={() => setShowNarration((open) => !open)}
                className={cn(
                  "inline-flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-full border px-2.5 py-1.5 text-xs font-semibold transition sm:min-h-0 sm:min-w-0 sm:px-3",
                  showNarration
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border bg-card hover:bg-secondary",
                )}
                title={showNarration ? "Hide the words" : "Show the words to read aloud"}
                aria-pressed={showNarration}
              >
                <BookOpen className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">
                  {showNarration ? "Hide words" : "Read aloud"}
                </span>
              </button>
            )}
            <button
              type="button"
              onClick={cycleFontSize}
              className="inline-flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-full border border-border bg-card px-2.5 py-1.5 text-xs font-semibold transition hover:bg-secondary sm:min-h-0 sm:min-w-0 sm:px-3"
              title="Change text size"
            >
              <Type className="h-3.5 w-3.5" />
              <span className="hidden sm:inline">
                {fontSize === "sm" ? "Cozy" : fontSize === "md" ? "Default" : "Large"}
              </span>
            </button>
            <button
              type="button"
              onClick={() => setIsFullscreen((open) => !open)}
              className="inline-flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-full border border-border bg-card px-2.5 py-1.5 text-xs font-semibold transition hover:bg-secondary sm:min-h-0 sm:min-w-0 sm:px-3"
              title={isFullscreen ? "Exit fullscreen" : "Enter fullscreen"}
            >
              {isFullscreen ? (
                <>
                  <Minimize2 className="h-3.5 w-3.5" />
                  <span className="hidden sm:inline">Exit</span>
                </>
              ) : (
                <>
                  <Maximize2 className="h-3.5 w-3.5" />
                  <span className="hidden sm:inline">Fullscreen</span>
                </>
              )}
            </button>
            {isFullscreen && (
              <button
                type="button"
                onClick={() => setIsFullscreen(false)}
                className="inline-flex h-11 w-11 items-center justify-center rounded-full border border-border bg-card p-2 hover:bg-secondary transition"
                aria-label="Close fullscreen"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>

        <div className="-mx-1 mb-3 flex items-center justify-start gap-2 overflow-x-auto pb-1 sm:mx-0 sm:flex-wrap sm:justify-center sm:overflow-visible">
          {pages.map((page, index) => (
            <button
              key={`tab-${index}`}
              type="button"
              onClick={() => api?.scrollTo(index)}
              className={cn(
                "shrink-0 rounded-full border px-3 py-1.5 text-xs font-semibold transition",
                index === current
                  ? "bg-primary text-primary-foreground border-primary"
                  : "bg-card text-muted-foreground border-border hover:bg-secondary",
              )}
            >
              Page {index + 1}
              {!page.isIllustrated ? " …" : ""}
            </button>
          ))}
        </div>

        <p className="text-center text-xs text-muted-foreground mb-3">
          {allIllustrated
            ? "Every page is illustrated — swipe or tap a page."
            : painting
              ? "Painting your pages — they fill in one by one."
              : "Read the whole story free. Unlock illustrations to see every page painted."}
        </p>

        <div className={cn("relative", isFullscreen ? "px-10 sm:px-12" : "px-0 sm:px-10")}>
          <Carousel setApi={setApi} opts={{ align: "start", loop: false }} className="w-full">
            <CarouselContent className="ml-0 sm:-ml-4">
              {pages.map((page, index) => {
                const caption = pageCaption(page);
                const hasCaption = caption !== "";
                const illustrated = !!page.isIllustrated;
                const pagePainting = !illustrated && painting;
                // Locked pages read as a clean text page — no lock panel, no clutter.
                return (
                  <CarouselItem key={`page-${index}`} className="pl-0 sm:pl-4">
                    <article
                      className="rounded-2xl border border-border bg-card shadow-card overflow-hidden"
                      style={{
                        background: `color-mix(in oklab, ${themeTint} 12%, var(--card))`,
                      }}
                    >
                      {(illustrated || pagePainting) && (
                        <PageIllustration
                          page={page}
                          pageIndex={index}
                          previewStatus={previewIllustrationStatus}
                          themeTint={themeTint}
                          totalPages={pages.length}
                          variant={variant}
                          caption={caption}
                          captionClassName={typography.caption}
                          onOpenFullscreen={() => setIsFullscreen(true)}
                        />
                      )}

                      {illustrated ? (
                        // Image already carries the caption overlay — only add narration on request.
                        showNarration &&
                        page.content.trim() !== "" && (
                          <div className="border-t border-border/60 bg-card/95 px-4 py-4 sm:px-6 sm:py-5">
                            <p
                              className={cn(
                                "text-foreground/90 text-pretty whitespace-pre-wrap",
                                typography.body,
                              )}
                            >
                              {page.content}
                            </p>
                          </div>
                        )
                      ) : (
                        <div className="px-4 py-5 sm:px-6 sm:py-6">
                          <p className="text-xs font-semibold text-primary mb-2">
                            Page {index + 1} of {pages.length}
                          </p>
                          {hasCaption ? (
                            <p
                              className={cn(
                                "font-display font-bold text-foreground text-balance",
                                typography.caption,
                              )}
                            >
                              {caption}
                            </p>
                          ) : (
                            <h3
                              className={cn(
                                "font-display font-semibold text-foreground text-pretty",
                                typography.title,
                              )}
                            >
                              {page.title}
                            </h3>
                          )}
                          {(!hasCaption || showNarration) && page.content.trim() !== "" && (
                            <p
                              className={cn(
                                "mt-3 text-foreground/90 text-pretty whitespace-pre-wrap",
                                typography.body,
                              )}
                            >
                              {page.content}
                            </p>
                          )}
                        </div>
                      )}
                    </article>
                  </CarouselItem>
                );
              })}
            </CarouselContent>
          </Carousel>

          <button
            type="button"
            onClick={() => api?.scrollPrev()}
            disabled={current === 0}
            className={cn(
              "absolute left-0 top-[38%] grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full border border-border bg-card shadow-soft transition hover:bg-secondary disabled:opacity-30",
              isFullscreen ? "sm:grid" : "hidden sm:grid",
            )}
            aria-label="Previous page"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => api?.scrollNext()}
            disabled={current >= totalSlides - 1}
            className={cn(
              "absolute right-0 top-[38%] grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full border border-border bg-card shadow-soft transition hover:bg-secondary disabled:opacity-30",
              isFullscreen ? "sm:grid" : "hidden sm:grid",
            )}
            aria-label="Next page"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>

        <div className="flex justify-center gap-1.5 mt-4">
          {Array.from({ length: totalSlides }, (_, index) => (
            <button
              key={`dot-${index}`}
              type="button"
              onClick={() => api?.scrollTo(index)}
              className={cn(
                "h-2 rounded-full transition-all",
                index === current ? "w-6 bg-primary" : "w-2 bg-border hover:bg-primary/40",
              )}
              aria-label={`Go to page ${index + 1}`}
            />
          ))}
        </div>

        {someLocked && !isFullscreen && (
          <div
            className="mt-4 flex items-center justify-center gap-2 rounded-2xl border border-primary/20 px-4 py-3 text-center"
            style={{ background: `color-mix(in oklab, ${themeTint} 18%, var(--card))` }}
          >
            <Lock className="h-4 w-4 shrink-0 text-primary" />
            <p className="text-sm text-foreground">
              {isWelcomeGiftStory
                ? "Your free page is ready — unlock every page for $4.99."
                : "Story's free to read — unlock the illustrations for $4.99."}
            </p>
          </div>
        )}

        {showPdfUpsell && (
          <div
            className="mt-4 rounded-2xl border border-primary/20 p-4 text-center"
            style={{ background: `color-mix(in oklab, ${themeTint} 25%, var(--card))` }}
          >
            <p className="text-sm font-medium text-foreground">
              Export a printable PDF from My Books — free.
            </p>
          </div>
        )}

        {showCreditsReminder && (
          <div
            className="mt-4 rounded-2xl border border-amber-300/50 p-4 text-center"
            style={{ background: `color-mix(in oklab, ${themeTint} 18%, var(--card))` }}
          >
            <p className="text-sm font-medium text-foreground">
              {storiesRemainingThisMonth} free{" "}
              {storiesRemainingThisMonth === 1 ? "story" : "stories"} left this month.
            </p>
          </div>
        )}

        {showCreditsUpsell && (
          <div
            className="mt-4 rounded-2xl border border-amber-300/60 p-4 text-center"
            style={{ background: `color-mix(in oklab, ${themeTint} 22%, var(--card))` }}
          >
            <p className="text-sm font-semibold text-foreground">Want another adventure?</p>
            <Link
              to="/"
              hash="pricing"
              className="inline-flex mt-3 items-center rounded-full bg-primary text-primary-foreground px-5 py-2 text-xs font-semibold hover:opacity-90 transition"
            >
              View book packs
            </Link>
          </div>
        )}
      </div>
    </div>
  );

  if (isFullscreen && typeof document !== "undefined") {
    return createPortal(readerShell, document.body);
  }

  return readerShell;
}
