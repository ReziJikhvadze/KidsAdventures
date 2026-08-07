import { useCallback, useEffect, useMemo, useState } from "react";
import { format } from "date-fns";
import { Link } from "@tanstack/react-router";
import { notify } from "@/lib/ui/notify";
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
import { createCheckoutSession } from "@/lib/api/subscriptions";
import { listChildren } from "@/lib/api/children";
import { CreditsBadge } from "@/components/site/CreditsBadge";
import { StoryBookReader } from "@/components/story/StoryBookReader";
import type { AdventurePackDetailResponse, AdventurePackStatus } from "@/lib/api/types";

const BOOKS_PER_PAGE = 6;

const statusStyles: Record<AdventurePackStatus, { label: string; className: string }> = {
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

function packStatusDisplay(pack: AdventurePackDetailResponse): {
  label: string;
  className: string;
} {
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
  const [unlockingId, setUnlockingId] = useState<string | null>(null);
  const [buyingCredit, setBuyingCredit] = useState(false);
  const [loadingReaderIds, setLoadingReaderIds] = useState<Set<string>>(() => new Set());
  const [openReaderIds, setOpenReaderIds] = useState<Set<string>>(() => new Set());
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const updatePack = useCallback((updated: AdventurePackDetailResponse) => {
    setPacks((prev) => prev.map((p) => (p.id === updated.id ? { ...p, ...updated } : p)));
  }, []);

  const load = useCallback(
    async (options?: { refreshInProgressOnly?: boolean }) => {
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
          (p) =>
            adventurePacksApi.isPackGenerating(p) ||
            needsPreviewPoll(p) ||
            p.status === "StoryReady" ||
            p.status === "Completed",
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
    },
    [isAuthenticated],
  );

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
    if (!isAuthenticated) return;

    const illustrating = packs.filter((p) => needsPreviewPoll(p));
    if (illustrating.length === 0) return;

    const timer = window.setInterval(() => {
      void Promise.all(
        illustrating.map((p) => adventurePacksApi.getAdventurePack(p.id).then(updatePack)),
      );
    }, 3000);

    return () => window.clearInterval(timer);
  }, [isAuthenticated, packs, updatePack]);

  useEffect(() => {
    if (!isAuthenticated) return;

    const missingDetails = paginatedPacks.filter(
      (p) =>
        (p.status === "StoryReady" || p.status === "Completed") &&
        (!p.storyPages || p.storyPages.length === 0),
    );

    if (missingDetails.length === 0) return;

    for (const pack of missingDetails) {
      setLoadingReaderIds((prev) => new Set(prev).add(pack.id));
      void adventurePacksApi
        .getAdventurePack(pack.id)
        .then(updatePack)
        .catch(() => {
          notify.error("Could not load story preview", {
            description: "Tap Refresh and try again.",
          });
        })
        .finally(() => {
          setLoadingReaderIds((prev) => {
            const next = new Set(prev);
            next.delete(pack.id);
            return next;
          });
        });
    }
  }, [isAuthenticated, paginatedPacks, updatePack]);

  const openDownload = async (pack: AdventurePackDetailResponse) => {
    if (pack.status !== "Completed" || downloadingId) return;
    const childName = childNames[pack.childId ?? ""] ?? pack.childName ?? "storybook";
    const fileName = `${childName}-${pack.theme}-storybook.pdf`.replace(/\s+/g, "-").toLowerCase();

    setDownloadingId(pack.id);
    try {
      await adventurePacksApi.downloadAdventurePack(pack.id, fileName);
    } catch {
      notify.error("PDF download failed", {
        description: "Create the illustrated PDF again from My Books, then retry the download.",
      });
    } finally {
      setDownloadingId(null);
    }
  };

  const toggleReadStory = async (pack: AdventurePackDetailResponse) => {
    if (openReaderIds.has(pack.id)) {
      setOpenReaderIds((prev) => {
        const next = new Set(prev);
        next.delete(pack.id);
        return next;
      });
      return;
    }

    if (!pack.storyPages || pack.storyPages.length === 0) {
      setLoadingReaderIds((prev) => new Set(prev).add(pack.id));
      try {
        const detail = await adventurePacksApi.getAdventurePack(pack.id);
        updatePack(detail);
      } catch {
        notify.error("Could not load story", {
          description: "Tap Refresh and try Read story again.",
        });
        return;
      } finally {
        setLoadingReaderIds((prev) => {
          const next = new Set(prev);
          next.delete(pack.id);
          return next;
        });
      }
    }

    setOpenReaderIds((prev) => new Set(prev).add(pack.id));
    requestAnimationFrame(() => {
      document
        .getElementById(`reader-${pack.id}`)
        ?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    });
  };

  const buyCredit = async () => {
    if (buyingCredit) return;
    setBuyingCredit(true);
    try {
      const session = await createCheckoutSession("Book1");
      if (session.checkoutUrl) {
        window.location.href = session.checkoutUrl;
        return;
      }
      notify.error("Could not start checkout", { description: "Please try again in a moment." });
    } catch (err) {
      notify.fromError(err, "Could not start checkout.");
    } finally {
      setBuyingCredit(false);
    }
  };

  const startIllustration = async (pack: AdventurePackDetailResponse) => {
    if (pack.status !== "StoryReady" || unlockingId) return;

    const credits = user?.bookCredits ?? 0;
    if (credits <= 0) {
      await buyCredit();
      return;
    }

    setUnlockingId(pack.id);
    try {
      const res = await adventurePacksApi.illustrateAdventurePack(pack.id);
      if (typeof res.bookCredits === "number") {
        setBookCredits(res.bookCredits);
      } else {
        await refreshAccountBalance();
      }
      notify.info("Unlocking illustrations", {
        description: "We're painting every page — about 8–12 minutes. You can leave this page.",
      });
      // Refresh so the pack flips to "Creating illustrations…" and the auto-poller takes over.
      const detail = await adventurePacksApi.getAdventurePack(pack.id);
      updatePack(detail);
    } catch (err) {
      notify.fromError(err, "Could not unlock illustrations. Your credit is safe — try again.");
      await refreshAccountBalance();
    } finally {
      setUnlockingId(null);
    }
  };

  const startPdf = async (pack: AdventurePackDetailResponse) => {
    if (pack.status !== "StoryReady" || pdfStartingId) return;
    if (!adventurePacksApi.canExportPackPdf(pack)) {
      notify.info("PDF not ready yet", {
        description: pack.isWelcomeGiftStory
          ? "Wait until your free illustrated page is ready, then export your preview PDF."
          : "Wait until every illustration is ready, then export your free PDF.",
      });
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
      notify.info("Building your PDF", {
        description:
          "We're assembling your printable storybook from the slideshow — about 30 seconds.",
      });
      await adventurePacksApi.pollAdventurePack(pack.id, updatePack, {
        untilStatus: "Completed",
        maxAttempts: 30,
      });
      notify.success("Your storybook PDF is ready!", {
        description: "Download it now or find it anytime in My Books.",
      });
      await refreshAccountBalance();
      await load();
    } catch (err) {
      notify.fromError(err, "PDF creation failed.");
      await load();
    } finally {
      setPdfStartingId(null);
    }
  };

  return (
    <section className="min-h-[60vh] border-y border-border/60 bg-secondary/30 py-10 sm:py-12 md:py-16">
      <div className="mx-auto max-w-5xl px-4 sm:px-6">
        <div className="flex flex-col gap-6 mb-8">
          <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
            <div className="flex items-start gap-4">
              <div className="grid place-items-center h-14 w-14 shrink-0 rounded-2xl bg-primary/10 ring-1 ring-primary/20 shadow-sm">
                <Library className="h-7 w-7 text-primary" />
              </div>
              <div>
                <p className="text-sm font-semibold text-primary uppercase tracking-wide">
                  Your library
                </p>
                <h2 className="font-display text-3xl font-bold tracking-tight mt-0.5">My Books</h2>
                <p className="text-muted-foreground mt-2 max-w-xl text-sm">
                  Read every story free.{" "}
                  <strong className="text-foreground">Unlock illustrations</strong> for $4.99 per
                  book.
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
                  Your book credits
                </p>
                <p className="text-sm text-amber-950/80 mt-0.5">
                  {user && user.bookCredits > 0
                    ? `${user.bookCredits} credit${user.bookCredits === 1 ? "" : "s"} ready — each unlocks one book.`
                    : "Stories are free. A $4.99 credit unlocks the illustrations."}
                </p>
              </div>
              {isLoading || !user ? (
                <div className="h-10 w-36 rounded-full bg-amber-200/60 animate-pulse" />
              ) : (
                <div className="flex items-center gap-2">
                  <CreditsBadge
                    credits={user.bookCredits}
                    storiesRemainingThisMonth={user.storiesRemainingThisMonth}
                    welcomeStoryRemaining={user.welcomeStoryRemaining}
                    variant="prominent"
                  />
                  {user.bookCredits === 0 && (
                    <button
                      type="button"
                      onClick={() => void buyCredit()}
                      disabled={buyingCredit}
                      className="inline-flex items-center gap-1.5 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground shadow-md transition hover:opacity-90 disabled:opacity-60"
                    >
                      {buyingCredit ? (
                        <Loader2 className="h-4 w-4 animate-spin" />
                      ) : (
                        <Sparkles className="h-4 w-4" />
                      )}
                      Buy a book — $4.99
                    </button>
                  )}
                </div>
              )}
            </div>
          )}

          {isAuthenticated && user && (
            <div className="grid sm:grid-cols-2 gap-3">
              <div className="rounded-2xl border border-border bg-card px-4 py-3 shadow-sm">
                <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  Stories saved
                </p>
                <p className="font-display text-2xl font-bold mt-1">{packs.length}</p>
              </div>
              <div className="rounded-2xl border border-border bg-card px-4 py-3 shadow-sm flex flex-col justify-center">
                <p className="text-xs text-muted-foreground">
                  {user.bookCredits > 0
                    ? `${user.bookCredits} book credit${user.bookCredits === 1 ? "" : "s"} ready.`
                    : "Unlock illustrations for $4.99 per book."}
                </p>
                {user.bookCredits === 0 && (
                  <button
                    type="button"
                    onClick={() => void buyCredit()}
                    disabled={buyingCredit}
                    className="mt-2 inline-flex w-fit items-center gap-1.5 rounded-full bg-primary px-3.5 py-1.5 text-sm font-semibold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-60"
                  >
                    {buyingCredit ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Sparkles className="h-4 w-4" />
                    )}
                    Get a book ($4.99)
                  </button>
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
            <ul className="grid gap-4 sm:gap-5">
              {paginatedPacks.map((pack) => {
                const status = packStatusDisplay(pack);
                const childName = childNames[pack.childId ?? ""] ?? pack.childName ?? "Child";
                const generating = adventurePacksApi.isPackGenerating(pack);
                const progressPct = adventurePacksApi.computePackProgressPercent(pack);
                const readable = slideshowIllustrationsReady(pack);
                const canExportPdf = adventurePacksApi.canExportPackPdf(pack);
                const illustrating = needsPreviewPoll(pack);
                const awaitingUnlock = adventurePacksApi.isAwaitingIllustrationUnlock(pack);
                const hasBookCredit = (user?.bookCredits ?? 0) > 0;
                const readerLoading = loadingReaderIds.has(pack.id);
                const canReadStory =
                  readable ||
                  illustrating ||
                  pack.status === "StoryReady" ||
                  pack.status === "Completed";
                const readerOpen = openReaderIds.has(pack.id);
                return (
                  <li
                    key={pack.id}
                    className="flex flex-col gap-4 rounded-2xl border border-border bg-card p-4 sm:p-5"
                  >
                    <div className="flex flex-col sm:flex-row sm:items-center gap-4">
                      <div className="flex-1 min-w-0">
                        <div className="flex flex-wrap items-center gap-2 mb-1">
                          <span
                            className={`text-xs font-bold px-2.5 py-0.5 rounded-full ${status.className}`}
                          >
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
                              {progressPct}% ·{" "}
                              {pack.progressMessage ?? "Working on your storybook…"}
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
                        {canReadStory && (
                          <button
                            type="button"
                            onClick={() => void toggleReadStory(pack)}
                            disabled={readerLoading}
                            className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-4 py-2.5 text-sm font-semibold hover:opacity-90 transition disabled:opacity-60"
                          >
                            {readerLoading ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <BookOpen className="h-4 w-4" />
                            )}
                            {readerOpen ? "Hide story" : "Read story"}
                          </button>
                        )}
                        {awaitingUnlock && isAuthenticated && (
                          <button
                            type="button"
                            onClick={() => void startIllustration(pack)}
                            disabled={unlockingId === pack.id}
                            className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-4 py-2.5 text-sm font-semibold hover:opacity-90 transition disabled:opacity-60"
                          >
                            {unlockingId === pack.id ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <Sparkles className="h-4 w-4" />
                            )}
                            {hasBookCredit
                              ? "Unlock the full storybook (1 credit)"
                              : "Buy the full storybook — $4.99"}
                          </button>
                        )}
                        {canExportPdf && pack.status === "StoryReady" && isAuthenticated && (
                          <button
                            type="button"
                            onClick={() => void startPdf(pack)}
                            disabled={pdfStartingId === pack.id}
                            className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-4 py-2.5 text-sm font-semibold hover:bg-secondary transition disabled:opacity-60"
                          >
                            {pdfStartingId === pack.id ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <Sparkles className="h-4 w-4" />
                            )}
                            {pack.isWelcomeGiftStory && !readable
                              ? "Export preview PDF (free)"
                              : "Export PDF (free)"}
                          </button>
                        )}
                        {pack.status === "Completed" && (
                          <button
                            type="button"
                            onClick={() => void openDownload(pack)}
                            disabled={downloadingId === pack.id}
                            className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-4 py-2.5 text-sm font-semibold hover:bg-secondary transition disabled:opacity-60"
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
                      </div>
                    </div>
                    {canReadStory && readerOpen && (
                      <div
                        id={`reader-${pack.id}`}
                        className="animate-rise w-full min-w-0 border-t border-border/60 pt-4"
                      >
                        <div className="mb-3 flex items-center gap-2">
                          <BookOpen className="h-4 w-4 text-primary shrink-0" />
                          <p className="text-sm font-semibold text-foreground">
                            {readable
                              ? "Illustrated slideshow"
                              : illustrating
                                ? "Illustrated slideshow loading…"
                                : "Story preview (text)"}
                          </p>
                          {awaitingUnlock && (
                            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-900">
                              Illustrations locked
                            </span>
                          )}
                        </div>
                        {readerLoading || (illustrating && !pack.storyPages?.length) ? (
                          <div className="flex min-h-[12rem] flex-col items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-secondary/30 px-4 py-12 text-muted-foreground sm:min-h-[16rem]">
                            <Loader2 className="h-6 w-6 animate-spin" />
                            <p className="text-sm text-center">
                              {pack.progressMessage ?? "Painting your picture-book pages…"}
                            </p>
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
                            isWelcomeGiftStory={pack.isWelcomeGiftStory}
                          />
                        ) : (
                          <div className="rounded-2xl border border-dashed border-border bg-secondary/20 px-4 py-10 text-center text-sm text-muted-foreground">
                            Story pages will appear here when your book is ready.
                          </div>
                        )}
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
