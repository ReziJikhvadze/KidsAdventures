import { ArrowRight, BookOpen, Check, Download, Loader2, Lock, Plus, Printer } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { ApiError } from "@/lib/api/client";
import {
  downloadAdventurePack,
  generatePackPdf,
  listAdventurePacks,
  pollAdventurePack,
} from "@/lib/api/adventure-packs";
import { listCharacters } from "@/lib/api/characters";
import { listPrintOrders } from "@/lib/api/print-orders";
import type {
  AdventureMapResponse,
  AdventurePackResponse,
  CharacterResponse,
  PrintOrderResponse,
  WorldNodeResponse,
} from "@/lib/api/types";
import { getAdventureMap, listAdventureMaps } from "@/lib/api/worlds";
import { useAuth } from "@/lib/auth/AuthContext";
import { continueViaPickerHref, newBookHref } from "@/lib/continue";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useT } from "@/lib/i18n";
import { SESSION_KEYS } from "@/lib/storage/session";
import { useWorldById, WORLD_COVER_ART, WORLD_IDS, isWorldId, type WorldId } from "@/lib/worlds";

type ChildWorldScreenProps = {
  /** The just-finished book, supplied by the reader through the transient URL query. */
  celebrationBookId?: string;
};

/** A book the pipeline has not finished writing: no cover to open, no PDF to fetch. */
function isPackGenerating(status: AdventurePackResponse["status"]): boolean {
  return status === "Pending" || status === "Generating" || status === "GeneratingStory";
}

/**
 * A preview the parent walked away from.
 *
 * Leaving the creation screen keeps the run id on the device — that is what lets a return pick up
 * the same book rather than paying for a second one — but nothing outside `/create` ever looked
 * at it, so walking away was a one-way door. Read once on mount: this is a signpost, and the
 * screen it points at owns the polling.
 */
function readPendingRunId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(SESSION_KEYS.pendingBookRunId);
  } catch {
    return null;
  }
}

export function ChildWorldScreen({ celebrationBookId }: ChildWorldScreenProps) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const navigate = useNavigate();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [characters, setCharacters] = useState<CharacterResponse[]>([]);
  const [characterId, setCharacterId] = useState<string | null>(null);
  const [map, setMap] = useState<AdventureMapResponse | null>(null);
  /*
    Every child's progress, not only the one on screen: the switcher above the shelf says how far
    each child has come, and `listAdventureMaps` answers for the whole family in one request.
  */
  const [mapsByCharacter, setMapsByCharacter] = useState<Record<string, AdventureMapResponse>>({});
  const [packs, setPacks] = useState<AdventurePackResponse[]>([]);
  const [printOrders, setPrintOrders] = useState<PrintOrderResponse[]>([]);
  const [activeWorldId, setActiveWorldId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [pendingRunId] = useState<string | null>(readPendingRunId);
  const initialCelebrationBookId = useRef(celebrationBookId);
  const consumedCelebrationBookId = useRef<string | null>(null);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      void navigate({ to: "/dashboard" });
      return;
    }

    let cancelled = false;
    void (async () => {
      try {
        const [rows, allPacks, prints, maps] = await Promise.all([
          listCharacters(),
          listAdventurePacks().catch(() => [] as AdventurePackResponse[]),
          listPrintOrders().catch(() => [] as PrintOrderResponse[]),
          listAdventureMaps().catch(() => null),
        ]);
        if (cancelled) return;

        const kids = rows.filter((c) => c.characterType === "child" || c.isPrimary);
        const list = kids.length ? kids : rows;
        setCharacters(list);
        setPacks(allPacks);
        setPrintOrders(prints);

        if (maps) {
          const byCharacter: Record<string, AdventureMapResponse> = {};
          for (const candidate of maps) byCharacter[candidate.characterId] = candidate;
          setMapsByCharacter(byCharacter);
        }

        // The child whose book just arrived, ahead of the nominal primary — a parent lands here
        // straight from finishing one, and another child's shelf reads as the book not existing.
        const newestPack = [...allPacks].sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
        const byNewestBook = newestPack?.primaryCharacterId
          ? list.find((c) => c.id === newestPack.primaryCharacterId)
          : null;
        let selectedCharacterId =
          byNewestBook?.id ?? list.find((c) => c.isPrimary)?.id ?? list[0]?.id ?? null;

        // A finished book handed over by the reader belongs to whichever child earned it.
        const handoffBookId = initialCelebrationBookId.current;
        if (handoffBookId && maps) {
          const eligible = new Set(list.map((character) => character.id));
          const owner = maps.find(
            (candidate) =>
              eligible.has(candidate.characterId) &&
              candidate.worlds.some(
                (node) => node.state === "Completed" && node.bookId === handoffBookId,
              ),
          );
          selectedCharacterId = owner?.characterId ?? selectedCharacterId;
        }

        setCharacterId(selectedCharacterId);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : "პერსონაჟები ვერ ჩაიტვირთა.");
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated, navigate]);

  useEffect(() => {
    if (!characterId || !isAuthenticated) return;
    let cancelled = false;
    setLoading(true);
    setError(null);

    void getAdventureMap(characterId)
      .then((response) => {
        if (cancelled) return;
        setMap(response);
        /*
          Open on somewhere the child can actually go. `nextWorldId` is a suggestion the server
          makes without checking it against this child's own progress, and a locked suggestion
          left the one button on the screen greyed out.
        */
        const startable = (id: string | null | undefined) =>
          !!id &&
          response.worlds.some((w) => w.worldId === id && (w.canStart || w.state !== "Locked"));
        setActiveWorldId(
          (startable(response.nextWorldId) ? response.nextWorldId : null) ||
            response.worlds.find((w) => w.state === "Next")?.worldId ||
            response.worlds.find((w) => w.state === "Unlocked")?.worldId ||
            response.worlds.find((w) => w.state === "Completed")?.worldId ||
            response.worlds[0]?.worldId ||
            null,
        );
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : "რუკა ვერ ჩაიტვირთა.");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [characterId, isAuthenticated]);

  useEffect(() => {
    if (!celebrationBookId || !map || consumedCelebrationBookId.current === celebrationBookId) {
      return;
    }
    // The query is a hint only; the authenticated map response owns progress.
    const completedNode = map.worlds.find(
      (node) => node.state === "Completed" && node.bookId === celebrationBookId,
    );
    if (!completedNode) return;

    consumedCelebrationBookId.current = celebrationBookId;
    setActiveWorldId(completedNode.worldId);
    // Keep the handoff ephemeral: preserving it would replay the celebration after a refresh.
    void navigate({ to: "/world", search: { bookId: undefined }, replace: true });
  }, [celebrationBookId, map, navigate]);

  const character = characters.find((c) => c.id === characterId) ?? null;
  const heroName = map?.characterName || character?.name || t.common.fallbackHeroName;

  const nodesById = useMemo(() => {
    const dict: Record<string, WorldNodeResponse> = {};
    for (const node of map?.worlds ?? []) dict[node.worldId] = node;
    return dict;
  }, [map]);

  const activeNode = activeWorldId ? nodesById[activeWorldId] : undefined;
  const worldId = (
    activeWorldId && isWorldId(activeWorldId) ? activeWorldId : "dinosaurs"
  ) as WorldId;
  const world = WORLD_BY_ID[worldId];

  const childPacks = useMemo(
    () =>
      packs
        .filter((p) => p.primaryCharacterId === characterId && !isPackGenerating(p.status))
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
    [packs, characterId],
  );

  const printByBook = useMemo(() => {
    const dict: Record<string, PrintOrderResponse> = {};
    for (const order of printOrders) dict[order.bookId] = order;
    return dict;
  }, [printOrders]);

  const completedCount = map?.completedCount ?? 0;
  const totalWorlds = map?.totalWorlds || map?.worlds.length || WORLD_IDS.length;

  /*
    One button, and it opens the map of worlds.

    Every branch of this used to end at `/create#preview`, which writes a book the moment it is on
    screen — so the child's own map had a single button that skipped the choice of world and
    started generating one. All of them lead to the picker now.
  */
  const continueHref = useMemo(() => {
    if (!characterId) return newBookHref(null, { from: "world" });
    if (map?.isFirstJourney) return newBookHref(characterId, { from: "world" });

    const continuation = map?.continuation;
    const priorBookId =
      continuation?.fromBookId ??
      (activeNode?.state === "Completed" ? (activeNode.bookId ?? null) : null);

    return continueViaPickerHref({
      characterId,
      continuesFromBookId: priorBookId,
      characterIds: continuation?.carryForwardCharacters.map((c) => c.id) ?? [characterId],
    });
  }, [characterId, map, activeNode]);

  const continueParts = useMemo(() => {
    const [pathAndQuery, hash] = continueHref.split("#");
    const [to, query = ""] = (pathAndQuery || "/create").split("?");
    return {
      to: to || "/create",
      search: Object.fromEntries(new URLSearchParams(query).entries()),
      hash: hash || undefined,
    };
  }, [continueHref]);

  const lockedSelected = activeNode?.state === "Locked";
  const summaryBody = lockedSelected
    ? t.story.world.lockedNote
    : activeNode?.state === "Completed"
      ? world.memoryBody
      : t.story.world.readyNote;

  if (authLoading || loading) {
    return (
      <div className="screen journey-screen">
        <AppHeader backHref="/dashboard" />
        <div className="journey-wrap">
          <p className="journey-empty" role="status" aria-live="polite">
            {t.common.states.loading}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="screen journey-screen">
      <AppHeader backHref="/dashboard" />

      <main className="journey-wrap">
        <section className="journey-welcome">
          <div>
            <span className="journey-eyebrow">
              <Sparkle /> {t.story.world.spaceOf(heroName)}
            </span>
            <h1>{t.dashboard.library.heading(heroName)}</h1>
            <p>{t.story.world.shelfLead}</p>
          </div>
          <Link
            className="journey-button journey-primary-button"
            to={continueParts.to}
            search={continueParts.search}
            hash={continueParts.hash}
          >
            <Plus aria-hidden="true" />
            {t.dashboard.sidebar.newBook}
          </Link>
        </section>

        <section aria-labelledby="journey-books-title">
          <div className="journey-section-head">
            <div>
              <h2 id="journey-books-title">{t.common.nav.books}</h2>
              <p>{t.dashboard.library.bookCount(childPacks.length)}</p>
            </div>

            {/* The children, as the handoff's filter pills: one row, one selected, each carrying
                how many worlds that child has opened. */}
            {characters.length > 1 ? (
              <div
                className="journey-kids"
                role="tablist"
                aria-label={t.dashboard.sidebar.parentLabel}
              >
                {characters.map((c) => {
                  const opened = mapsByCharacter[c.id]?.completedCount ?? 0;
                  return (
                    <button
                      key={c.id}
                      type="button"
                      role="tab"
                      aria-selected={c.id === characterId}
                      className={`journey-kid ${c.id === characterId ? "is-on" : ""} ${
                        opened === 0 ? "is-empty" : ""
                      }`}
                      onClick={() => setCharacterId(c.id)}
                    >
                      <i aria-hidden="true" />
                      {c.name}
                      <span aria-hidden="true">· {opened}</span>
                    </button>
                  );
                })}
              </div>
            ) : null}
          </div>

          {/* A book still being written, and the way back into it. */}
          {pendingRunId ? (
            <div className="journey-resume">
              <span className="journey-resume-spark" aria-hidden="true">
                <Loader2 />
              </span>
              <div className="journey-resume-copy">
                <strong>{t.story.world.resumeTitle}</strong>
                <small>{t.story.world.resumeBody}</small>
              </div>
              <Link
                className="journey-button journey-resume-button"
                to="/create"
                hash="preview"
                search={{ resume: "1" }}
              >
                {t.story.world.resumeAction}
                <ArrowRight aria-hidden="true" />
              </Link>
            </div>
          ) : null}

          {childPacks.length > 0 ? (
            <div className="journey-books">
              {childPacks.map((pack) => (
                <JourneyBookCard
                  key={pack.id}
                  pack={pack}
                  heroName={heroName}
                  printOrder={printByBook[pack.id]}
                />
              ))}
            </div>
          ) : (
            <p className="journey-empty">{t.dashboard.library.otherChild(heroName)}</p>
          )}

          {error ? <p className="journey-note">{error}</p> : null}
        </section>

        <section className="journey-story" aria-labelledby="journey-path-title">
          <div className="journey-section-head journey-section-head-story">
            <div>
              <span className="journey-eyebrow journey-eyebrow-violet">
                <Sparkle /> STORY PATH
              </span>
              <h2 id="journey-path-title">
                {heroName}
                {t.story.world.journeySuffix}
              </h2>
              <p>{t.story.map.lead}</p>
            </div>
            <div
              className="journey-progress"
              aria-label={t.story.map.progress(completedCount, totalWorlds)}
            >
              <strong>{completedCount}</strong>
              {/* The handoff's separator. Without it the two numbers read as one. */}
              <span>/ {t.story.map.ofTotal(totalWorlds)}</span>
            </div>
          </div>

          <div
            className="journey-map"
            style={{ backgroundImage: `url("${WORLD_COVER_ART[worldId]}")` }}
          >
            <div className="journey-summary" aria-live="polite">
              <span className="journey-kicker">
                {activeNode?.state === "Completed"
                  ? t.story.map.statusCompleted(activeNode.sequenceNumber ?? 1)
                  : lockedSelected
                    ? t.journey.worldSelector.locked
                    : t.story.world.nextAdventure}
              </span>
              <h3>{activeNode?.bookTitle || world.mapTitle}</h3>
              <p>{summaryBody}</p>
              {lockedSelected ? (
                <button className="journey-button journey-story-cta" type="button" disabled>
                  {t.journey.worldSelector.locked}
                </button>
              ) : (
                <Link
                  className="journey-button journey-story-cta"
                  to={continueParts.to}
                  search={continueParts.search}
                  hash={continueParts.hash}
                >
                  {activeNode?.state === "Completed"
                    ? t.story.world.continueFromMemory
                    : t.story.world.unlockNext}
                  <ArrowRight aria-hidden="true" />
                </Link>
              )}
            </div>

            {/*
              The six worlds along the foot of the painting — the milestone, said plainly. A
              finished world is a gold disc with a tick, the next is violet and numbered, the rest
              are locked, and the bar behind them fills to how far this child has come.
            */}
            <div className="journey-track" aria-label={t.story.map.ariaLabel(heroName)}>
              <div className="journey-track-line" aria-hidden="true">
                <span
                  style={{
                    width: `${totalWorlds > 0 ? Math.min(100, (completedCount / totalWorlds) * 100) : 0}%`,
                  }}
                />
              </div>

              {WORLD_IDS.map((id, index) => {
                const node = nodesById[id];
                const state = node?.state ?? "Locked";
                const place = WORLD_BY_ID[id];
                const trackState =
                  state === "Completed" ? "done" : state === "Locked" ? "locked" : "available";
                return (
                  <button
                    key={id}
                    type="button"
                    className={`journey-node ${activeWorldId === id ? "is-selected" : ""}`}
                    data-state={trackState}
                    aria-pressed={activeWorldId === id}
                    aria-label={`${place.mapTitle} — ${
                      trackState === "done"
                        ? t.journey.worldSelector.visited
                        : trackState === "locked"
                          ? t.journey.worldSelector.locked
                          : t.story.map.statusNext
                    }`}
                    onClick={() => setActiveWorldId(id)}
                  >
                    <span className="journey-marker" aria-hidden="true">
                      {trackState === "done" ? (
                        <Check />
                      ) : trackState === "locked" ? (
                        <Lock />
                      ) : (
                        index + 1
                      )}
                    </span>
                    <span className="journey-node-label">{place.mapLabel}</span>
                  </button>
                );
              })}
            </div>
          </div>
        </section>
      </main>
    </div>
  );
}

/** The handoff's own four-point star, drawn rather than typed: the eyebrows lead with it. */
function Sparkle() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" aria-hidden="true">
      <path d="M12 3v6M12 15v6M3 12h6M15 12h6" />
    </svg>
  );
}

/**
 * One finished book: its cover, what format it is in, and the two things a parent wants from it —
 * open it, and take the PDF away.
 */
function JourneyBookCard({
  pack,
  heroName,
  printOrder,
}: {
  pack: AdventurePackResponse;
  heroName: string;
  printOrder?: PrintOrderResponse;
}) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfBusy, setPdfBusy] = useState(false);

  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const cover = useIllustrationUrl(pack.coverImageUrl) ?? WORLD_COVER_ART[worldId];
  const title = pack.title?.trim() || world.bookTitle(heroName);
  const hasPrint = pack.hasPrintEntitlement || !!printOrder;

  // Built on demand: a finished book whose PDF was never generated used to show a dead button.
  const handlePdf = useCallback(async () => {
    setPdfError(null);
    setPdfBusy(true);
    try {
      if (!pack.pdfUrl) {
        if (pack.status !== "GeneratingPdf") await generatePackPdf(pack.id);
        await pollAdventurePack(pack.id, undefined, { untilPdfReady: true, maxAttempts: 90 });
      }
      await downloadAdventurePack(pack.id, `${title}.pdf`);
    } catch (err) {
      setPdfError(
        err instanceof ApiError ? err.message : (err as Error)?.message || "PDF ვერ მომზადდა.",
      );
    } finally {
      setPdfBusy(false);
    }
  }, [pack.id, pack.pdfUrl, pack.status, title]);

  /*
    Built from the month names this product already ships, not from `toLocaleDateString`.

    `ka-GE` came back "August 21, 2026" here: the locale is only as good as the ICU data in the
    browser, and a Georgian page printing an English month is worse than no date. `t.common.date`
    is the same list the birth-date field offers, so the two always agree.
  */
  const created = new Date(pack.createdAt);
  const dateLabel = Number.isNaN(created.getTime())
    ? null
    : `${created.getDate()} ${t.common.date.months[created.getMonth()]}, ${created.getFullYear()}`;

  return (
    <article className="journey-book">
      <Link
        className="journey-cover"
        to="/reader/$bookId"
        params={{ bookId: pack.id }}
        style={{ backgroundImage: `url("${cover}")` }}
        aria-label={t.dashboard.library.openBook(title)}
      >
        <span className="journey-cover-brand">BEKI</span>
        <span className="journey-cover-title">{title}</span>
      </Link>

      <div className="journey-book-body">
        <div className="journey-book-top">
          <span
            className={`journey-badge ${hasPrint ? "journey-badge-printed" : "journey-badge-digital"}`}
          >
            {hasPrint ? <Printer aria-hidden="true" /> : <BookOpen aria-hidden="true" />}
            {hasPrint ? t.dashboard.library.statusPrinted : t.dashboard.library.statusCreated}
          </span>
          {dateLabel ? <span className="journey-book-date">{dateLabel}</span> : null}
        </div>

        <div>
          <h3>{title}</h3>
          <p>{world.theme}</p>
        </div>

        {printOrder ? (
          <div className="journey-delivery">
            <Check aria-hidden="true" />
            {printOrder.statusLabel || t.dashboard.library.printOrdered}
          </div>
        ) : null}

        <div className="journey-book-actions">
          <Link
            className="journey-button journey-small-button journey-outline-button"
            to="/reader/$bookId"
            params={{ bookId: pack.id }}
          >
            <BookOpen aria-hidden="true" />
            {t.dashboard.library.read}
          </Link>
          <button
            className="journey-button journey-small-button journey-gold-button"
            type="button"
            onClick={() => void handlePdf()}
            disabled={pdfBusy}
          >
            <Download aria-hidden="true" />
            {pdfBusy ? t.dashboard.library.pdfBusy : "PDF"}
          </button>
        </div>

        {pdfError ? (
          <p className="journey-book-error" role="alert">
            {pdfError}
          </p>
        ) : null}
      </div>
    </article>
  );
}
