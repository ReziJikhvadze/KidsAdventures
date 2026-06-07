import { useCallback, useEffect, useMemo, useState } from "react";
import { format } from "date-fns";
import { Link } from "@tanstack/react-router";
import { toast } from "sonner";
import {
  BookOpen,
  ChevronLeft,
  ChevronRight,
  Download,
  Library,
  Loader2,
  Package,
  Plus,
  RefreshCw,
  Sparkles,
} from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { AuthDialog } from "@/components/auth/AuthDialog";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { listChildren } from "@/lib/api/children";
import { CreditsBadge } from "@/components/site/CreditsBadge";
import { StoryBookReader } from "@/components/story/StoryBookReader";
import type { AdventurePackDetailResponse, AdventurePackStatus } from "@/lib/api/types";

const BOOKS_PER_PAGE = 6;

const statusStyles: Record<
  AdventurePackStatus,
  { label: string; className: string }
> = {
  Pending: { label: "Queued", className: "bg-amber-100 text-amber-900" },
  Generating: { label: "Writing story…", className: "bg-sky-100 text-sky-900" },
  GeneratingStory: { label: "Writing story…", className: "bg-sky-100 text-sky-900" },
  StoryReady: { label: "Story ready", className: "bg-violet-100 text-violet-900" },
  GeneratingPdf: { label: "Creating PDF…", className: "bg-sky-100 text-sky-900" },
  Completed: { label: "PDF ready", className: "bg-emerald-100 text-emerald-900" },
  Failed: { label: "Failed", className: "bg-red-100 text-red-900" },
};

function slideshowIllustrationsReady(pack: AdventurePackDetailResponse): boolean {
  return adventurePacksApi.isPackReadable(pack);
}

function needsPreviewPoll(pack: AdventurePackDetailResponse): boolean {
  return adventurePacksApi.isPackIllustrating(pack);
}

function packStatusDisplay(pack: AdventurePackDetailResponse): { label: string; className: string } {
  if (adventurePacksApi.isPackIllustrating(pack)) {
    return { label: "Creating illustrations…", className: "bg-sky-100 text-sky-900" };
  }

  return statusStyles[pack.status];
}

export function MyPacks() {
  const { isAuthenticated, isLoading, canCreatePdf, refreshAccountBalance, setBookCredits, user } =
    useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [packs, setPacks] = useState<AdventurePackDetailResponse[]>([]);
  const [childNames, setChildNames] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const [pdfStartingId, setPdfStartingId] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [readerLoadingId, setReaderLoadingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const updatePack = useCallback((updated: AdventurePackDetailResponse) => {
    setPacks((prev) => prev.map((p) => (p.id === updated.id ? { ...p, ...updated } : p)));
  }, []);

  const load = useCallback(async (options?: { refreshInProgressOnly?: boolean }) => {
    if (!isAuthenticated) return;
    const refreshInProgressOnly = options?.refreshInProgressOnly ?? false;
    if (!refreshInProgressOnly) {
      setLoading(true);
    }
    setError(null);
    try {
      const packRows = await adventurePacksApi.listAdventurePacks();
      const children = refreshInProgressOnly ? null : await listChildren();
      const sorted = [...packRows].sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      );
      const detailTargets = sorted.filter(
        (p) => adventurePacksApi.isPackGenerating(p) || needsPreviewPoll(p),
      );
      const details = await Promise.all(
        detailTargets.map((p) => adventurePacksApi.getAdventurePack(p.id).catch(() => p)),
      );
      const detailMap = Object.fromEntries(details.map((d) => [d.id, d]));
      setPacks((prev) => {
        const prevMap = Object.fromEntries(prev.map((p) => [p.id, p]));
        return sorted.map((p) => detailMap[p.id] ?? prevMap[p.id] ?? p);
      });
      if (children) {
        setChildNames(Object.fromEntries(children.map((c) => [c.id, c.name])));
      }
    } catch {
      setError("Could not load your books. Try again.");
    } finally {
      if (!refreshInProgressOnly) {
        setLoading(false);
      }
    }
  }, [isAuthenticated]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (isAuthenticated) {
      void refreshAccountBalance();
    }
  }, [isAuthenticated, refreshAccountBalance]);

  const totalPages = Math.max(1, Math.ceil(packs.length / BOOKS_PER_PAGE));
  const paginatedPacks = useMemo(() => {
    const start = (page - 1) * BOOKS_PER_PAGE;
    return packs.slice(start, start + BOOKS_PER_PAGE);
  }, [packs, page]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  const hasInProgress = useMemo(
    () => packs.some((p) => adventurePacksApi.isPackGenerating(p)),
    [packs],
  );

  useEffect(() => {
    if (!isAuthenticated || !hasInProgress) return;
    const timer = window.setInterval(() => void load({ refreshInProgressOnly: true }), 5000);
    return () => window.clearInterval(timer);
  }, [isAuthenticated, hasInProgress, load]);

  useEffect(() => {
    if (!expandedId) return;
    const pack = packs.find((p) => p.id === expandedId);
    if (!pack || !needsPreviewPoll(pack)) return;

    const timer = window.setInterval(() => {
      void adventurePacksApi.getAdventurePack(expandedId).then(updatePack);
    }, 3000);

    return () => window.clearInterval(timer);
  }, [expandedId, packs, updatePack]);

  const openReader = async (pack: AdventurePackDetailResponse) => {
    if (!slideshowIllustrationsReady(pack)) {
      toast.message("Your story is still being illustrated. Check back in a minute.");
      return;
    }

    if (expandedId === pack.id) {
      setExpandedId(null);
      return;
    }

    setExpandedId(pack.id);
    setReaderLoadingId(pack.id);
    try {
      const fresh = await adventurePacksApi.getAdventurePack(pack.id);
      updatePack(fresh);
    } catch {
      toast.error("Could not load story preview. Try Refresh.");
    } finally {
      setReaderLoadingId(null);
    }
  };

  const openDownload = async (pack: AdventurePackDetailResponse) => {
    if (pack.status !== "Completed" || downloadingId) return;
    const childName = childNames[pack.childId] ?? pack.childName ?? "storybook";
    const fileName = `${childName}-${pack.theme}-storybook.pdf`.replace(/\s+/g, "-").toLowerCase();

    setDownloadingId(pack.id);
    try {
      await adventurePacksApi.downloadAdventurePack(pack.id, fileName);
    } catch {
      toast.error("Could not download PDF. Try creating the illustrated PDF again.");
    } finally {
      setDownloadingId(null);
    }
  };

  const startPdf = async (pack: AdventurePackDetailResponse) => {
    if (pack.status !== "StoryReady" || pdfStartingId) return;
    if (!slideshowIllustrationsReady(pack)) {
      toast.message("Wait until every page is illustrated, then export PDF.");
      return;
    }
    setPdfStartingId(pack.id);
    try {
      const queued = await adventurePacksApi.generatePackPdf(pack.id);
      if (typeof queued.bookCredits === "number") {
        setBookCredits(queued.bookCredits);
      } else {
        await refreshAccountBalance();
      }
      toast.success("Building your PDF from slideshow illustrations — about 30 seconds.");
      await adventurePacksApi.pollAdventurePack(
        pack.id,
        updatePack,
        {
          untilStatus: "Completed",
          maxAttempts: 30,
        },
      );
      toast.success("Your storybook PDF is ready!");
      await refreshAccountBalance();
      await load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "PDF creation failed.");
      await load();
    } finally {
      setPdfStartingId(null);
    }
  };

  return (
    <section className="py-12 md:py-16 bg-secondary/30 border-y border-border/60 min-h-[60vh]">
      <div className="mx-auto max-w-5xl px-6">
        <div className="flex flex-col gap-6 mb-8">
          <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
            <div className="flex items-start gap-4">
              <div className="grid place-items-center h-14 w-14 shrink-0 rounded-2xl bg-primary/10 ring-1 ring-primary/20 shadow-sm">
                <Library className="h-7 w-7 text-primary" />
              </div>
              <div>
                <p className="text-sm font-semibold text-primary uppercase tracking-wide">Your library</p>
                <h2 className="font-display text-3xl font-bold tracking-tight mt-0.5">My Books</h2>
                <p className="text-muted-foreground mt-2 max-w-xl text-sm">
                  Read the slideshow for free. Each <strong className="text-foreground">PDF export</strong> uses one
                  book credit.
                </p>
              </div>
            </div>
            {isAuthenticated && (
              <div className="flex flex-wrap items-center gap-2 sm:justify-end">
                <Link
                  to="/"
                  hash="generator"
                  className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-4 py-2 text-sm font-semibold hover:opacity-90 transition"
                >
                  <Plus className="h-4 w-4" />
                  New story
                </Link>
                <button
                  type="button"
                  onClick={() => void load()}
                  disabled={loading}
                  className="inline-flex items-center justify-center gap-2 rounded-full border border-border bg-card px-4 py-2 text-sm font-semibold hover:bg-background transition disabled:opacity-50"
                >
                  <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
                  Refresh
                </button>
              </div>
            )}
          </div>

          {isAuthenticated && (
            <div className="rounded-2xl border border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50 px-4 py-4 shadow-sm flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-amber-900/80">
                  Your story credits
                </p>
                <p className="text-sm text-amber-950/80 mt-0.5">
                  {user && user.bookCredits > 0
                    ? `${user.bookCredits} purchased credit${user.bookCredits === 1 ? "" : "s"} plus 1 free story every month. PDF export is always free.`
                    : "PDF export is free for every story. Buy credits for extra stories beyond your monthly free one."}
                </p>
              </div>
              {isLoading || !user ? (
                <div className="h-10 w-36 rounded-full bg-amber-200/60 animate-pulse" />
              ) : (
                <CreditsBadge
                  credits={user.bookCredits}
                  storiesRemainingThisMonth={user.storiesRemainingThisMonth}
                  variant="prominent"
                  linkToPricing={user.bookCredits === 0 && user.storiesRemainingThisMonth === 0}
                />
              )}
            </div>
          )}

          {isAuthenticated && user && (
            <div className="grid sm:grid-cols-2 gap-3">
              <div className="rounded-2xl border border-border bg-card px-4 py-3 shadow-sm">
                <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Stories saved</p>
                <p className="font-display text-2xl font-bold mt-1">{packs.length}</p>
              </div>
              <div className="rounded-2xl border border-border bg-card px-4 py-3 shadow-sm flex flex-col justify-center">
                <p className="text-xs text-muted-foreground">
                  {user.storiesRemainingThisMonth > 0
                    ? `${user.storiesRemainingThisMonth} of ${user.storiesAllowedThisMonth} stories left this month — each is a full 6-page illustrated book.`
                    : "Monthly story limit reached. Buy book credits for more adventures — PDF export stays free."}
                </p>
                {user.storiesRemainingThisMonth === 0 && (
                  <Link
                    to="/"
                    hash="pricing"
                    className="mt-2 inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
                  >
                    View book packs
                  </Link>
                )}
              </div>
            </div>
          )}
        </div>

        {!isAuthenticated ? (
          <div className="rounded-3xl border border-border bg-card p-10 text-center">
            <Package className="h-10 w-10 mx-auto text-muted-foreground mb-4" />
            <p className="text-muted-foreground mb-4">Sign in to see books you have created.</p>
            <button
              type="button"
              onClick={() => setAuthOpen(true)}
              className="inline-flex items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
            >
              Sign in
            </button>
          </div>
        ) : loading && packs.length === 0 ? (
          <div className="flex items-center justify-center gap-2 py-16 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            Loading your books…
          </div>
        ) : error ? (
          <div className="rounded-3xl border border-destructive/30 bg-destructive/5 p-6 text-center text-destructive">
            {error}
          </div>
        ) : packs.length === 0 ? (
          <div className="rounded-3xl border border-dashed border-border bg-card p-10 text-center">
            <p className="text-muted-foreground">No books yet. Create your first story below.</p>
            <Link
              to="/"
              hash="generator"
              className="inline-flex mt-4 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
            >
              Create a book
            </Link>
          </div>
        ) : (
          <>
          <ul className="grid gap-4">
            {paginatedPacks.map((pack) => {
              const status = packStatusDisplay(pack);
              const childName = childNames[pack.childId] ?? pack.childName ?? "Child";
              const expanded = expandedId === pack.id;
              const generating = adventurePacksApi.isPackGenerating(pack);
              const progressPct = adventurePacksApi.computePackProgressPercent(pack);
              const readable = slideshowIllustrationsReady(pack);
              return (
                <li
                  key={pack.id}
                  className="rounded-2xl border border-border bg-card p-5 flex flex-col gap-4"
                >
                  <div className="flex flex-col sm:flex-row sm:items-center gap-4">
                    <div className="flex-1 min-w-0">
                      <div className="flex flex-wrap items-center gap-2 mb-1">
                        <span className={`text-xs font-bold px-2.5 py-0.5 rounded-full ${status.className}`}>
                          {status.label}
                        </span>
                        {generating && (
                          <span className="text-xs font-medium text-sky-700 flex items-center gap-1">
                            <Loader2 className="h-3 w-3 animate-spin" />
                            In progress
                          </span>
                        )}
                        <span className="text-xs text-muted-foreground">
                          {format(new Date(pack.createdAt), "MMM d, yyyy · h:mm a")}
                        </span>
                      </div>
                      <p className="font-display text-lg font-semibold truncate">
                        {pack.title ?? `${childName}'s ${pack.theme} story`}
                      </p>
                      {generating && (
                        <div className="mt-3 max-w-md">
                          <div className="h-2 rounded-full bg-border overflow-hidden">
                            <div
                              className="h-full bg-primary transition-all duration-500"
                              style={{ width: `${Math.max(5, progressPct)}%` }}
                            />
                          </div>
                          <p className="mt-1.5 text-xs text-muted-foreground tabular-nums">
                            {progressPct}% · {pack.progressMessage ?? "Working on your storybook…"}
                          </p>
                        </div>
                      )}
                      {pack.status === "Failed" && pack.errorMessage && (
                        <p className="text-xs text-destructive/90 mt-1 line-clamp-3">
                          {pack.errorMessage}
                        </p>
                      )}
                    </div>
                    <div className="flex flex-wrap items-center gap-2 shrink-0">
                      {readable && pack.status === "StoryReady" && isAuthenticated && (
                        <button
                          type="button"
                          onClick={() => void startPdf(pack)}
                          disabled={pdfStartingId === pack.id}
                          className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-4 py-2.5 text-sm font-semibold hover:opacity-90 transition disabled:opacity-60"
                        >
                          {pdfStartingId === pack.id ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Sparkles className="h-4 w-4" />
                          )}
                          Export PDF (free)
                        </button>
                      )}
                      {pack.status === "Completed" && (
                        <button
                          type="button"
                          onClick={() => void openDownload(pack)}
                          disabled={downloadingId === pack.id}
                          className="inline-flex items-center gap-2 rounded-full bg-foreground text-background px-4 py-2.5 text-sm font-semibold hover:opacity-90 transition disabled:opacity-60"
                        >
                          {downloadingId === pack.id ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Download className="h-4 w-4" />
                          )}
                          Download storybook PDF
                        </button>
                      )}
                      {pack.status === "Failed" && (
                        <Link
                          to="/"
                          hash="generator"
                          className="inline-flex items-center rounded-full border border-border px-4 py-2.5 text-sm font-semibold hover:bg-secondary transition"
                        >
                          Try again
                        </Link>
                      )}
                      {readable && (pack.status === "StoryReady" || pack.status === "Completed") && (
                          <button
                            type="button"
                            onClick={() => void openReader(pack)}
                            className="inline-flex items-center gap-1.5 rounded-full border border-border px-4 py-2.5 text-sm font-semibold hover:bg-secondary transition"
                          >
                            <BookOpen className="h-4 w-4" />
                            {expanded ? "Hide story" : "Read story"}
                          </button>
                        )}
                    </div>
                  </div>
                  {expanded && (
                    <div className="animate-rise pt-2">
                      {readerLoadingId === pack.id ? (
                        <div className="flex items-center justify-center gap-2 py-16 text-muted-foreground">
                          <Loader2 className="h-6 w-6 animate-spin" />
                          Opening your storybook…
                        </div>
                      ) : pack.storyPages && pack.storyPages.length > 0 ? (
                        <StoryBookReader
                          pages={pack.storyPages}
                          theme={pack.theme}
                          title={pack.title ?? `${childName}'s ${pack.theme} story`}
                          childName={childName}
                          previewIllustrationStatus={pack.previewIllustrationStatus}
                          isCompleted={pack.status === "Completed"}
                          storiesRemainingThisMonth={user?.storiesRemainingThisMonth}
                          bookCredits={user?.bookCredits}
                        />
                      ) : null}
                    </div>
                  )}
                </li>
              );
            })}
          </ul>

          {totalPages > 1 && (
            <nav
              className="mt-6 flex flex-col sm:flex-row items-center justify-between gap-3"
              aria-label="Books pagination"
            >
              <p className="text-sm text-muted-foreground">
                Showing {(page - 1) * BOOKS_PER_PAGE + 1}–
                {Math.min(page * BOOKS_PER_PAGE, packs.length)} of {packs.length} books
              </p>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page === 1}
                  className="inline-flex items-center gap-1 rounded-full border border-border bg-card px-3 py-1.5 text-sm font-semibold hover:bg-secondary transition disabled:opacity-40"
                >
                  <ChevronLeft className="h-4 w-4" />
                  Previous
                </button>
                <span className="text-sm font-medium px-2">
                  Page {page} of {totalPages}
                </span>
                <button
                  type="button"
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                  className="inline-flex items-center gap-1 rounded-full border border-border bg-card px-3 py-1.5 text-sm font-semibold hover:bg-secondary transition disabled:opacity-40"
                >
                  Next
                  <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </nav>
          )}
          </>
        )}

        {hasInProgress && (
          <p className="text-xs text-muted-foreground text-center mt-4">
            Stories and preview pictures update automatically every few seconds.
          </p>
        )}
      </div>
      <AuthDialog open={authOpen} onOpenChange={setAuthOpen} defaultMode="login" />
    </section>
  );
}
