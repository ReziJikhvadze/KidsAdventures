import { ArrowRight, Sparkles } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StoryPathMap } from "@/components/adventrya/world/StoryPathMap";
import { ApiError } from "@/lib/api/client";
import { listCharacters } from "@/lib/api/characters";
import type { AdventureMapResponse, CharacterResponse } from "@/lib/api/types";
import { getAdventureMap, listAdventureMaps } from "@/lib/api/worlds";
import { useAuth } from "@/lib/auth/AuthContext";
import { continueViaPickerHref, newBookHref } from "@/lib/continue";
import { useT } from "@/lib/i18n";
import { useWorldById, isWorldId, type WorldId } from "@/lib/worlds";

type ChildWorldScreenProps = {
  /** The just-finished book, supplied by the reader through the transient URL query. */
  celebrationBookId?: string;
};

export function ChildWorldScreen({ celebrationBookId }: ChildWorldScreenProps) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const navigate = useNavigate();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [characters, setCharacters] = useState<CharacterResponse[]>([]);
  const [characterId, setCharacterId] = useState<string | null>(null);
  const [map, setMap] = useState<AdventureMapResponse | null>(null);
  /*
    Every child's progress, not only the one on screen.

    The left column names the family, and a name on its own does not say which child has been
    anywhere. `listAdventureMaps` returns one map per child in a single request, so the counts
    beside the names cost nothing beyond what the celebration handoff already asks for.
  */
  const [mapsByCharacter, setMapsByCharacter] = useState<Record<string, AdventureMapResponse>>({});
  const [activeWorldId, setActiveWorldId] = useState<string | null>(null);
  const [celebrationWorldId, setCelebrationWorldId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
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
        const rows = await listCharacters();
        if (cancelled) return;
        const kids = rows.filter((c) => c.characterType === "child" || c.isPrimary);
        const list = kids.length ? kids : rows;
        setCharacters(list);
        const primary = list.find((c) => c.isPrimary) ?? list[0] ?? null;
        let selectedCharacterId = primary?.id ?? null;

        /*
          Every child's map, always — not only when a finished book has to find its owner.

          It was fetched under that one condition, and the left column needs it on every visit:
          it is what lets each name carry the number of worlds that child has opened. One request
          for the whole family, and the celebration handoff below reads the same answer.
        */
        const maps = await listAdventureMaps().catch(() => null);
        if (cancelled) return;
        if (maps) {
          const byCharacter: Record<string, AdventureMapResponse> = {};
          for (const candidate of maps) byCharacter[candidate.characterId] = candidate;
          setMapsByCharacter(byCharacter);
        }

        // A family can have more than one child. Find the finished book's owner before
        // selecting the default hero, so a sibling's map can receive its earned celebration.
        const handoffBookId = initialCelebrationBookId.current;
        if (handoffBookId) {
          const eligibleCharacterIds = new Set(list.map((character) => character.id));
          const mapWithCompletedBook = maps?.find(
            (candidate) =>
              eligibleCharacterIds.has(candidate.characterId) &&
              candidate.worlds.some(
                (node) => node.state === "Completed" && node.bookId === handoffBookId,
              ),
          );
          selectedCharacterId = mapWithCompletedBook?.characterId ?? selectedCharacterId;
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
          Open on somewhere the child can actually go.

          `nextWorldId` used to win outright, and it is a suggestion the server makes without
          checking it against this child's own progress — so a map whose suggestion was still
          locked opened on that island, with the one button on the screen greyed out and reading
          "this world is locked". A parent switching between two children hit it immediately.
          The suggestion is still preferred; it just has to be a place they may start.
        */
        const startable = (worldId: string | null | undefined) =>
          !!worldId &&
          response.worlds.some(
            (w) => w.worldId === worldId && (w.canStart || w.state !== "Locked"),
          );

        const next =
          (startable(response.nextWorldId) ? response.nextWorldId : null) ||
          response.worlds.find((w) => w.state === "Next")?.worldId ||
          response.worlds.find((w) => w.state === "Unlocked")?.worldId ||
          response.worlds.find((w) => w.state === "Completed")?.worldId ||
          response.worlds[0]?.worldId ||
          null;
        setActiveWorldId(next);
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

    // The query is deliberately only a hint. The authenticated map response owns progress,
    // so a guessed or stale book id cannot select or celebrate an unrelated world.
    const completedNode = map.worlds.find(
      (node) => node.state === "Completed" && node.bookId === celebrationBookId,
    );
    if (!completedNode) return;

    consumedCelebrationBookId.current = celebrationBookId;
    setActiveWorldId(completedNode.worldId);
    setCelebrationWorldId(completedNode.worldId);

    // Keep the handoff ephemeral: preserving it would replay the celebration after a refresh.
    void navigate({ to: "/world", search: { bookId: undefined }, replace: true });
  }, [celebrationBookId, map, navigate]);

  const character = characters.find((c) => c.id === characterId) ?? null;
  const heroName = map?.characterName || character?.name || t.common.fallbackHeroName;
  const activeNode = map?.worlds.find((w) => w.worldId === activeWorldId);
  const worldId = (
    activeWorldId && isWorldId(activeWorldId) ? activeWorldId : "dinosaurs"
  ) as WorldId;
  const world = WORLD_BY_ID[worldId];

  /*
    One button, and it opens the map of worlds.

    Every branch of this used to end at `/create#preview`, which writes a book the moment it is
    on screen. So the child's own map had a single button that skipped the choice of world
    entirely and started generating one — for whichever world the server happened to suggest —
    with nothing on the way to stop it. All of them lead to the picker now; the prior book and
    the friends it carries forward ride along in the query, and the questions after it are where
    a parent says yes.
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
      // No `|| "preview"`: a href that deliberately carries no hash means the world picker, and
      // defaulting it back to the preview is what started generations nobody asked for.
      hash: hash || undefined,
    };
  }, [continueHref]);

  const ctaLabel =
    activeNode?.state === "Completed" ? t.story.world.continueFromMemory : t.story.world.unlockNext;

  const lockedSelected = activeNode?.state === "Locked";

  return (
    <div className="screen world-screen">
      <div className="child-world-art" aria-hidden="true" />
      <div className="world-shade" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/dashboard" worldMode />

      <aside className="world-panel">
        <div className="world-profile">
          <span className="large-avatar" aria-hidden="true">
            {heroName.slice(0, 1)}
          </span>
          <div>
            <small>
              {t.story.world.profileLine(character?.age ?? 5, map?.completedCount ?? 0)}
            </small>
            <h1>{heroName}ს სამყარო</h1>
          </div>
        </div>

        {/*
          The family down the left, the worlds across the right.

          What stood here was one child — the selected one — under a keepsake note about a
          dinosaur named Rex that belonged to no book anyone had bought, a paragraph explaining
          the map beside the map, and, only if there happened to be more than one child, a row of
          bare name buttons at the bottom with nothing to tell them apart. A parent with two
          children could not see, without pressing each in turn, which of them had been anywhere.
          The list says it: the name, and how many worlds that child has opened.
        */}
        <p className="world-children-title">{t.dashboard.sidebar.parentLabel}</p>

        <div className="world-children">
          {characters.map((c) => {
            const opened = mapsByCharacter[c.id]?.completedCount ?? 0;
            return (
              <button
                key={c.id}
                type="button"
                className={`world-child-card ${c.id === characterId ? "selected" : ""}`}
                onClick={() => setCharacterId(c.id)}
                aria-pressed={c.id === characterId}
              >
                <span className="child-avatar" aria-hidden="true">
                  {c.name.slice(0, 1)}
                </span>
                <span>
                  <strong>{c.name}</strong>
                  <small>{t.story.world.worldCount(opened)}</small>
                </span>
                <ArrowRight aria-hidden="true" />
              </button>
            );
          })}
        </div>

        <div className="selected-world-inline" aria-live="polite">
          <span>
            <Sparkles />
          </span>
          <div>
            <small>{world.chapter}</small>
            <strong>{activeNode?.bookTitle || world.mapTitle}</strong>
            <p>
              {lockedSelected
                ? t.story.world.lockedNote
                : activeNode?.state === "Next"
                  ? t.story.world.readyNote
                  : world.memoryBody}
            </p>
          </div>
        </div>

        <div className="world-actions">
          {lockedSelected ? (
            <button className="button button-quiet" type="button" disabled>
              {t.story.world.lockedNote}
            </button>
          ) : (
            <Link
              className="button button-primary"
              to={continueParts.to}
              search={continueParts.search}
              hash={continueParts.hash}
            >
              {ctaLabel}
              <ArrowRight aria-hidden="true" />
            </Link>
          )}
        </div>

        <div className="world-legend">
          <span>
            <i className="legend-visited" /> {t.story.map.legendUnlocked}
          </span>
          <span>
            <i className="legend-current" /> {t.story.map.legendNext}
          </span>
          <span>
            <i className="legend-future" /> {t.story.map.legendFuture}
          </span>
        </div>

        {error ? (
          <p className="eyebrow" style={{ marginTop: 12, color: "#f1c970" }}>
            {error}
          </p>
        ) : null}
        {loading ? (
          <p className="eyebrow" style={{ marginTop: 12, color: "#f8f2e5a8" }}>
            იტვირთება…
          </p>
        ) : null}
      </aside>

      <div className="living-map living-map-v2">
        {map ? (
          <StoryPathMap
            map={map}
            activeWorldId={activeWorldId}
            celebrationWorldId={celebrationWorldId}
            onSelect={setActiveWorldId}
          />
        ) : null}
      </div>
    </div>
  );
}
