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
import { continueHrefFromMap, newBookHref } from "@/lib/continue";
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

        // A family can have more than one child. Find the finished book's owner before
        // selecting the default hero, so a sibling's map can receive its earned celebration.
        const handoffBookId = initialCelebrationBookId.current;
        if (handoffBookId) {
          const maps = await listAdventureMaps().catch(() => null);
          if (cancelled) return;
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
        const next =
          response.nextWorldId ||
          response.worlds.find((w) => w.state === "Next")?.worldId ||
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

  const continueHref = useMemo(() => {
    // Nothing to continue from yet, so this is a new book: it starts at the world picker rather
    // than on the preview stage, which would generate one before anything had been chosen.
    if (!characterId) return newBookHref();
    if (map?.isFirstJourney) return newBookHref(characterId);
    if (activeNode?.canStart || activeNode?.state === "Next" || activeNode?.state === "Unlocked") {
      return continueHrefFromMap(map?.continuation, characterId, worldId);
    }
    if (activeNode?.state === "Completed" && activeNode.bookId) {
      return continueHrefFromMap(
        map?.continuation ?? {
          fromBookId: activeNode.bookId,
          fromWorldId: worldId,
          fromSequenceNumber: activeNode.sequenceNumber ?? 1,
          nextSequenceNumber: (activeNode.sequenceNumber ?? 1) + 1,
          suggestedWorldId: map?.nextWorldId ?? worldId,
          carryForwardCharacters: [
            { id: characterId, name: heroName, characterType: "child", isPrimary: true },
          ],
        },
        characterId,
        map?.nextWorldId ?? worldId,
      );
    }
    return continueHrefFromMap(map?.continuation, characterId, worldId);
  }, [characterId, map, activeNode, worldId, heroName]);

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

        <div className="memory-note">
          <span className="rex-seal" aria-hidden="true">
            R
          </span>
          <div>
            <small>{t.story.world.lastMemory}</small>
            <p>{t.story.world.lastMemoryNote}</p>
          </div>
          <Sparkles aria-hidden="true" />
        </div>

        <p className="world-explanation">{t.story.world.guidance}</p>

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

        {characters.length > 1 ? (
          <div className="world-language-row" style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            {characters.map((c) => (
              <button
                key={c.id}
                type="button"
                className={`button button-quiet ${c.id === characterId ? "button-primary" : ""}`}
                onClick={() => setCharacterId(c.id)}
              >
                {c.name}
              </button>
            ))}
          </div>
        ) : null}

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
