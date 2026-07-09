import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "@tanstack/react-router";
import { ChevronLeft, Loader2, Map, Sparkles } from "lucide-react";
import { notify } from "@/lib/ui/notify";
import { useAuth } from "@/lib/auth/AuthContext";
import { AuthDialog } from "@/components/auth/AuthDialog";
import { listChildren } from "@/lib/api/children";
import { getAdventurePack, pollAdventurePack, computePackProgressPercent } from "@/lib/api/adventure-packs";
import type { AdventurePackDetailResponse, ChildResponse, ThemeType } from "@/lib/api/types";
import {
  confirmCampfire,
  completeChapter,
  generateChapter,
  getCampfirePrompt,
  getStoryPathOverview,
  type StoryPathOverview,
  type StoryPathWorld,
} from "@/lib/api/story-path";
import { isCampfireNode } from "@/lib/story-path/campfireNodes";
import { StoryBookReader } from "@/components/story/StoryBookReader";
import { WorldMap } from "@/components/story-path/WorldMap";
import { CampfireScreen } from "@/components/story-path/CampfireScreen";
import { AchievementsShelf } from "@/components/story-path/AchievementsShelf";
import { WorldCompleteCelebration } from "@/components/story-path/WorldCompleteCelebration";
import { StoryPathPaywallCard } from "@/components/story-path/StoryPathPaywallCard";
import { StartChapterCard } from "@/components/story-path/StartChapterCard";
import { GeneratingChapterCard } from "@/components/story-path/GeneratingChapterCard";
import { LockedChapterCard } from "@/components/story-path/LockedChapterCard";
import { STORY_THEMES } from "@/lib/themes";
import { THEME_ORDER } from "@/lib/story-path/mapLayouts";
import { cn } from "@/lib/utils";

type ViewMode = "map" | "reader" | "campfire";

type StoryPathViewProps = {
  initialChildId?: string;
  initialTheme?: ThemeType;
  compact?: boolean;
};

function themeLabel(theme: ThemeType): string {
  return STORY_THEMES.find((t) => t.apiTheme === theme)?.name ?? theme;
}

export function StoryPathView({ initialChildId, initialTheme, compact }: StoryPathViewProps) {
  const { isAuthenticated, isLoading: authLoading, user } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [children, setChildren] = useState<ChildResponse[]>([]);
  const [selectedChildId, setSelectedChildId] = useState<string | null>(initialChildId ?? null);
  const [selectedTheme, setSelectedTheme] = useState<ThemeType>(initialTheme ?? "Dinosaurs");
  const [overview, setOverview] = useState<StoryPathOverview | null>(null);
  const [loading, setLoading] = useState(false);
  const [viewMode, setViewMode] = useState<ViewMode>("map");

  const [activeChapterIndex, setActiveChapterIndex] = useState<number | null>(null);
  const [activePageIndex, setActivePageIndex] = useState<number | null>(null);
  const [activePack, setActivePack] = useState<AdventurePackDetailResponse | null>(null);
  const [campfirePrompt, setCampfirePrompt] = useState("");
  const [confirming, setConfirming] = useState(false);

  const [confirmChapterIndex, setConfirmChapterIndex] = useState<number | null>(null);
  const [lockedChapterIndex, setLockedChapterIndex] = useState<number | null>(null);
  const [startingChapter, setStartingChapter] = useState(false);
  const [generatingChapterIndex, setGeneratingChapterIndex] = useState<number | null>(null);
  const [generationProgress, setGenerationProgress] = useState(0);

  const [highlightNode, setHighlightNode] = useState<number | null>(null);
  const [newAchievement, setNewAchievement] = useState<StoryPathOverview["achievements"][number] | null>(null);
  const [showPaywall, setShowPaywall] = useState(false);
  const [paywallTheme, setPaywallTheme] = useState<ThemeType | null>(null);

  const currentWorld = useMemo(
    () => overview?.worlds.find((w) => w.theme === selectedTheme) ?? null,
    [overview, selectedTheme],
  );

  const selectedChild = children.find((c) => c.id === selectedChildId);

  const updateWorld = useCallback((world: StoryPathWorld) => {
    setOverview((prev) =>
      prev
        ? {
            ...prev,
            worlds: prev.worlds.map((w) => (w.theme === world.theme ? world : w)),
          }
        : prev,
    );
  }, []);

  const loadOverview = useCallback(async (childId: string) => {
    setLoading(true);
    try {
      const data = await getStoryPathOverview(childId);
      setOverview(data);
    } catch {
      notify.error("Could not load Story Path. Try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated) return;
    void listChildren()
      .then((rows) => {
        setChildren(rows);
        if (!selectedChildId && rows.length > 0) {
          setSelectedChildId(initialChildId ?? rows[0].id);
        }
      })
      .catch(() => notify.error("Could not load children."));
  }, [isAuthenticated, initialChildId, selectedChildId]);

  useEffect(() => {
    if (selectedChildId && isAuthenticated) {
      void loadOverview(selectedChildId);
    }
  }, [selectedChildId, isAuthenticated, loadOverview]);

  useEffect(() => {
    if (initialTheme) setSelectedTheme(initialTheme);
  }, [initialTheme]);

  const goBackToMap = useCallback(() => {
    setViewMode("map");
    setActivePack(null);
    setActiveChapterIndex(null);
    setActivePageIndex(null);
  }, []);

  const handleNodeSelect = useCallback(
    async (chapterIndex: number) => {
      const node = currentWorld?.nodes.find((n) => n.chapterIndex === chapterIndex);
      if (!node || !selectedChildId) return;

      if (node.status === "Locked") {
        setLockedChapterIndex(chapterIndex);
        return;
      }

      if (node.status === "Unlocked") {
        setLockedChapterIndex(null);
        setConfirmChapterIndex(chapterIndex);
        return;
      }

      if (node.status === "Generating" && node.adventurePackId) {
        setGenerationProgress(0);
        setGeneratingChapterIndex(chapterIndex);
        return;
      }

      if ((node.status === "ReadyToRead" || node.status === "Complete") && node.adventurePackId) {
        try {
          const pack = await getAdventurePack(node.adventurePackId);
          setActivePack(pack);
          setActiveChapterIndex(chapterIndex);
          setActivePageIndex(0);
          setViewMode("reader");
        } catch {
          notify.error("Could not open this chapter.");
        }
      }
    },
    [currentWorld, selectedChildId],
  );

  const handleConfirmStartChapter = useCallback(async () => {
    if (confirmChapterIndex === null || !selectedChildId) return;
    setStartingChapter(true);
    try {
      const result = await generateChapter(selectedTheme, confirmChapterIndex, selectedChildId);
      updateWorld(result.world);
      setGenerationProgress(0);
      setGeneratingChapterIndex(confirmChapterIndex);
      setConfirmChapterIndex(null);
    } catch {
      notify.error("Could not start this chapter. Try again.");
    } finally {
      setStartingChapter(false);
    }
  }, [confirmChapterIndex, selectedChildId, selectedTheme, updateWorld]);

  // Poll the generating chapter's pack until it's readable, then refresh chapter status.
  useEffect(() => {
    if (generatingChapterIndex === null || !currentWorld) return;
    const node = currentWorld.nodes.find((n) => n.chapterIndex === generatingChapterIndex);
    const packId = node?.adventurePackId;
    if (!packId) return;

    let cancelled = false;
    void (async () => {
      try {
        await pollAdventurePack(
          packId,
          (pack) => {
            if (!cancelled) setGenerationProgress(computePackProgressPercent(pack));
          },
          { untilReadable: true, maxAttempts: 400 },
        );
      } catch {
        if (!cancelled) notify.error("This chapter is taking longer than expected. Check back soon.");
      }

      if (cancelled || !selectedChildId) return;
      try {
        const refreshed = await getStoryPathOverview(selectedChildId);
        if (!cancelled) {
          setOverview(refreshed);
          setHighlightNode(generatingChapterIndex);
          window.setTimeout(() => setHighlightNode(null), 1500);
        }
      } catch {
        /* map will refresh next time overview loads */
      } finally {
        if (!cancelled) setGeneratingChapterIndex(null);
      }
      // eslint-disable-next-line react-hooks/exhaustive-deps
    })();

    return () => {
      cancelled = true;
    };
    // Only re-run when the chapter being generated changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [generatingChapterIndex]);

  const finishChapter = useCallback(async () => {
    if (!selectedChildId || activeChapterIndex === null) return;
    try {
      const result = await completeChapter(selectedTheme, activeChapterIndex, selectedChildId);
      updateWorld(result.world);
      setHighlightNode(activeChapterIndex + 1);
      window.setTimeout(() => setHighlightNode(null), 1500);

      if (result.newAchievement) {
        setNewAchievement(result.newAchievement);
      }

      if (result.suggestNextWorld && result.nextTheme) {
        setPaywallTheme(result.nextTheme as ThemeType);
        setShowPaywall(true);
      }
    } catch {
      notify.error("Could not save your progress. Try again.");
    }
  }, [activeChapterIndex, selectedChildId, selectedTheme, updateWorld]);

  const handlePageComplete = useCallback(
    async (pageIndex: number) => {
      if (!selectedChildId || !activePack) return;
      const isLastPage = pageIndex === (activePack.storyPages?.length ?? 0) - 1;

      if (!isCampfireNode(pageIndex)) {
        try {
          const result = await confirmCampfire({
            childId: selectedChildId,
            adventurePackId: activePack.id,
            nodeIndex: pageIndex,
          });
          updateWorld(result.world);
          if (isLastPage) {
            await finishChapter();
          }
          goBackToMap();
        } catch {
          notify.error("Could not save progress. Try again.");
        }
        return;
      }

      try {
        const { prompt } = await getCampfirePrompt(selectedTheme, pageIndex);
        setCampfirePrompt(prompt);
        setActivePageIndex(pageIndex);
        setViewMode("campfire");
      } catch {
        setCampfirePrompt("What was your favorite part of this page? Talk about it together.");
        setActivePageIndex(pageIndex);
        setViewMode("campfire");
      }
    },
    [selectedChildId, selectedTheme, activePack, finishChapter, goBackToMap, updateWorld],
  );

  const handleCampfireConfirm = useCallback(async () => {
    if (!selectedChildId || !activePack || activePageIndex === null) return;
    setConfirming(true);
    try {
      const isLastPage = activePageIndex === (activePack.storyPages?.length ?? 0) - 1;
      const result = await confirmCampfire({
        childId: selectedChildId,
        adventurePackId: activePack.id,
        nodeIndex: activePageIndex,
      });
      updateWorld(result.world);
      if (isLastPage) {
        await finishChapter();
      }
      goBackToMap();
    } catch {
      notify.error("Could not save progress. Try again.");
    } finally {
      setConfirming(false);
    }
  }, [activePageIndex, activePack, finishChapter, goBackToMap, selectedChildId, updateWorld]);

  if (authLoading) {
    return (
      <div className="flex min-h-[40vh] items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="mx-auto max-w-lg px-4 py-16 text-center">
        <Sparkles className="mx-auto h-10 w-10 text-primary" />
        <h1 className="mt-4 font-display text-2xl font-semibold">Story Path</h1>
        <p className="mt-2 text-muted-foreground">
          Sign in to follow your child&apos;s adventure map — page by page, with cozy campfire moments.
        </p>
        <button
          type="button"
          onClick={() => setAuthOpen(true)}
          className="mt-6 inline-flex min-h-11 items-center rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground"
        >
          Sign in
        </button>
        <AuthDialog open={authOpen} onOpenChange={setAuthOpen} />
      </div>
    );
  }

  if (children.length === 0 && !loading) {
    return (
      <div className="mx-auto max-w-lg px-4 py-16 text-center">
        <Map className="mx-auto h-10 w-10 text-primary" />
        <h1 className="mt-4 font-display text-2xl font-semibold">Add a child first</h1>
        <p className="mt-2 text-muted-foreground">
          Story Path needs a child profile before it can start your first saga.
        </p>
        <Link
          to="/"
          hash="generator"
          className="mt-6 inline-flex min-h-11 items-center rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground"
        >
          Get started
        </Link>
      </div>
    );
  }

  return (
    <div className={cn("mx-auto max-w-5xl px-4 sm:px-6", compact ? "py-4" : "py-8")}>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          {!compact && (
            <p className="text-xs font-semibold uppercase tracking-wide text-primary">Story Path</p>
          )}
          <h1 className="font-display text-2xl font-semibold sm:text-3xl">
            {selectedChild ? `${selectedChild.name}'s journey` : "Your journey"}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Tap a chapter to start a new story, read one, or share a campfire moment together.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {children.length > 1 && (
            <select
              value={selectedChildId ?? ""}
              onChange={(e) => {
                setSelectedChildId(e.target.value);
                setViewMode("map");
                setNewAchievement(null);
                setShowPaywall(false);
                setConfirmChapterIndex(null);
                setLockedChapterIndex(null);
                setGeneratingChapterIndex(null);
              }}
              className="min-h-11 rounded-full border border-border bg-card px-4 text-sm font-medium"
              aria-label="Select child"
            >
              {children.map((child) => (
                <option key={child.id} value={child.id}>
                  {child.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex flex-wrap gap-1 rounded-full border border-border bg-card p-1">
            {THEME_ORDER.map((theme) => (
              <button
                key={theme}
                type="button"
                onClick={() => {
                  setSelectedTheme(theme);
                  setViewMode("map");
                  setNewAchievement(null);
                  setConfirmChapterIndex(null);
                  setLockedChapterIndex(null);
                  setGeneratingChapterIndex(null);
                }}
                className={cn(
                  "rounded-full px-3 py-1.5 text-xs font-semibold transition",
                  selectedTheme === theme
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-secondary",
                )}
              >
                {themeLabel(theme)}
              </button>
            ))}
          </div>
        </div>
      </div>

      {loading && (
        <div className="flex justify-center py-16">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      )}

      {!loading && viewMode === "map" && currentWorld && (
        <div className="space-y-6">
          <WorldMap
            world={currentWorld}
            theme={selectedTheme}
            highlightNodeIndex={highlightNode}
            onNodeSelect={(index) => void handleNodeSelect(index)}
          />
          {confirmChapterIndex !== null && (
            <StartChapterCard
              chapterIndex={confirmChapterIndex}
              themeLabel={themeLabel(selectedTheme)}
              childName={selectedChild?.name}
              starting={startingChapter}
              onConfirm={() => void handleConfirmStartChapter()}
              onCancel={() => setConfirmChapterIndex(null)}
            />
          )}
          {lockedChapterIndex !== null && (
            <LockedChapterCard
              chapterIndex={lockedChapterIndex}
              childName={selectedChild?.name}
              onDismiss={() => setLockedChapterIndex(null)}
            />
          )}
          {generatingChapterIndex !== null && (
            <GeneratingChapterCard
              chapterIndex={generatingChapterIndex}
              progress={generationProgress}
              childName={selectedChild?.name}
            />
          )}
          {newAchievement && (
            <WorldCompleteCelebration
              achievement={newAchievement}
              themeLabel={themeLabel(selectedTheme)}
            />
          )}
          {showPaywall && paywallTheme && (
            <StoryPathPaywallCard
              nextThemeLabel={themeLabel(paywallTheme)}
              bookCredits={user?.bookCredits ?? 0}
            />
          )}
          {overview && <AchievementsShelf achievements={overview.achievements} />}
        </div>
      )}

      {!loading && viewMode === "reader" && activePack && (
        <div>
          <button
            type="button"
            onClick={goBackToMap}
            className="mb-4 inline-flex min-h-11 items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ChevronLeft className="h-4 w-4" />
            Back to map
          </button>
          <StoryBookReader
            pages={activePack.storyPages ?? []}
            theme={activePack.theme}
            title={activePack.title ?? "Your story"}
            childName={activePack.childName ?? selectedChild?.name}
            previewIllustrationStatus={activePack.previewIllustrationStatus}
            isCompleted={activePack.status === "Completed"}
            initialPageIndex={activePageIndex ?? 0}
            singlePageMode
            startFullscreen
            onExitFullscreen={goBackToMap}
            onPageComplete={(pageIndex) => void handlePageComplete(pageIndex)}
            hasHeroPhoto={!!selectedChild?.photoUrl}
            packId={activePack.id}
          />
        </div>
      )}

      {!loading && viewMode === "campfire" && (
        <div>
          <CampfireScreen
            prompt={campfirePrompt}
            childName={selectedChild?.name}
            themeLabel={themeLabel(selectedTheme)}
            onConfirm={() => void handleCampfireConfirm()}
            confirming={confirming}
          />
        </div>
      )}
    </div>
  );
}
