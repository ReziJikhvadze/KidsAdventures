import { ArrowRight, Download, Package, Plus, Sparkles, Users } from "lucide-react";
import { useMemo } from "react";

import { StoryPathMap } from "@/components/adventrya/world/StoryPathMap";
import type { AdventureMapResponse, WorldNodeState } from "@/lib/api/types";
import { useT } from "@/lib/i18n";
import { WORLD_COVER_ART, useWorldById, type WorldId } from "@/lib/worlds";

/*
  The sample family behind the sign-in gate.

  Every value below is a module constant on purpose. This screen renders with no session at
  all, so it cannot fetch: the dashboard's own loaders would 401 on sight and the preview
  would be a row of spinners. The names stay Georgian in both interface languages — they are
  one Georgian family, and everything written around them (world names, book titles, the
  story counter) still comes from the active catalogue.
*/
const DEMO_CHILDREN = [
  { name: "ნია", stories: 2 },
  { name: "ლუკა", stories: 1 },
];

/** Two worlds behind them and one open ahead: a family a few books in, not a brand-new one. */
const DEMO_NODES: { worldId: WorldId; state: WorldNodeState; sequenceNumber: number | null }[] = [
  { worldId: "dinosaurs", state: "Completed", sequenceNumber: 1 },
  { worldId: "space", state: "Completed", sequenceNumber: 2 },
  { worldId: "pirates", state: "Next", sequenceNumber: null },
  { worldId: "animals", state: "Locked", sequenceNumber: null },
  { worldId: "airplanes", state: "Locked", sequenceNumber: null },
  { worldId: "magic", state: "Locked", sequenceNumber: null },
];

const DEMO_ACTIVE_WORLD: WorldId = "pirates";

/** The finished books, matching the two completed worlds — one of them printed. */
const DEMO_SHELF: { worldId: WorldId; printed: boolean }[] = [
  { worldId: "dinosaurs", printed: true },
  { worldId: "space", printed: false },
];

/**
 * The Parent Dashboard as a still life.
 *
 * A copy of the signed-in layout — the same aside and `.dashboard-main`, the same class names —
 * filled from the constants above. It is rendered inside an `inert` layer, so nothing here is
 * reachable and none of the controls carry handlers or destinations: an anchor without an href
 * and a button without an onClick keep the styling without pretending to work.
 */
export function DashboardDemo() {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const heroName = DEMO_CHILDREN[0].name;
  const activeWorld = WORLD_BY_ID[DEMO_ACTIVE_WORLD];
  const completedCount = DEMO_NODES.filter((n) => n.state === "Completed").length;

  const map = useMemo<AdventureMapResponse>(
    () => ({
      characterId: "demo",
      characterName: heroName,
      isFirstJourney: false,
      completedCount,
      totalWorlds: DEMO_NODES.length,
      nextWorldId: DEMO_ACTIVE_WORLD,
      worlds: DEMO_NODES.map((node, index) => ({
        worldId: node.worldId,
        name: WORLD_BY_ID[node.worldId].theme,
        sortOrder: index,
        state: node.state,
        canStart: node.state !== "Locked",
        bookTitle:
          node.state === "Completed" ? WORLD_BY_ID[node.worldId].bookTitle(heroName) : null,
        sequenceNumber: node.sequenceNumber,
      })),
    }),
    [WORLD_BY_ID, completedCount, heroName],
  );

  return (
    <>
      <aside className="dashboard-sidebar">
        <div className="parent-label">
          <Users aria-hidden="true" />
          <span>
            <small>Parent Dashboard</small>
            {t.common.nav.myFamily}
          </span>
        </div>

        <p className="sidebar-title">{t.dashboard.sidebar.parentLabel}</p>

        {DEMO_CHILDREN.map((child, index) => (
          <button
            key={child.name}
            type="button"
            className={`child-switch-card ${index === 0 ? "selected" : ""}`}
          >
            <span className="child-avatar nia-avatar" aria-hidden="true">
              {child.name.slice(0, 1)}
            </span>
            <span>
              <strong>{child.name}</strong>
              <small>
                {child.stories
                  ? t.dashboard.sidebar.storyCount(child.stories)
                  : t.dashboard.sidebar.noStoriesYet}
              </small>
            </span>
            <ArrowRight aria-hidden="true" />
          </button>
        ))}

        <span className="sidebar-new-book">
          <Sparkles aria-hidden="true" />
          {t.dashboard.sidebar.newBook}
        </span>

        <span className="add-child">
          <Plus aria-hidden="true" />
          {t.dashboard.sidebar.addChild}
        </span>
      </aside>

      <section className="dashboard-main map-first-dashboard">
        <div className="map-dashboard-heading">
          <div>
            <p className="eyebrow">
              <Sparkles aria-hidden="true" /> {t.story.world.welcomeBack.trim()}
            </p>
            <h1>
              {heroName}
              {t.story.world.titleSuffix}
            </h1>
            <p>{t.story.map.lead}</p>
          </div>
          <div className="map-dashboard-summary">
            <span>
              <strong>{DEMO_SHELF.length}</strong>
              {t.story.world.statBook}
            </span>
            <span>
              <strong>{completedCount}</strong>
              {t.story.world.statMemory}
            </span>
            <span>
              <strong>{DEMO_NODES.length}</strong>
              {t.story.world.statWorld}
            </span>
          </div>
        </div>

        <div className="dashboard-map-frame">
          <StoryPathMap map={map} activeWorldId={DEMO_ACTIVE_WORLD} onSelect={() => {}} compact />

          <div className="dashboard-map-action">
            <div>
              <small>{activeWorld.chapter}</small>
              <strong>{activeWorld.mapTitle}</strong>
              <p>{t.story.world.readyNote}</p>
            </div>
            <span className="button button-primary">
              {t.story.world.unlockNext}
              <ArrowRight aria-hidden="true" />
            </span>
          </div>
        </div>

        <div className="dashboard-section-heading map-library-heading">
          <div>
            <h2>{t.dashboard.library.heading(heroName)}</h2>
            <p>{t.story.world.archiveNote}</p>
          </div>
          <a>{t.common.actions.seeAll}</a>
        </div>

        <div className="book-library">
          {DEMO_SHELF.map((book, index) => {
            const world = WORLD_BY_ID[book.worldId];
            const title = world.bookTitle(heroName);
            return (
              <article className="library-book" key={book.worldId}>
                <span
                  className={`library-cover cover-${book.worldId === "space" ? "space" : "dino"}`}
                  style={{
                    backgroundImage: `url("${WORLD_COVER_ART[book.worldId]}")`,
                    backgroundSize: "cover",
                  }}
                >
                  <span>{t.dashboard.library.bookIndex(index + 1)}</span>
                  <strong>{title}</strong>
                </span>
                <div>
                  <small>
                    {world.theme} ·{" "}
                    {book.printed
                      ? t.dashboard.library.formatBoth
                      : t.dashboard.library.formatDigital}
                  </small>
                  <h3>{title}</h3>
                  <div>
                    <a>Online Reader</a>
                    <button type="button">
                      <Download aria-hidden="true" /> PDF
                    </button>
                  </div>
                  {book.printed ? (
                    <div className="library-print-status">
                      <Package aria-hidden="true" />
                      {t.dashboard.library.printOrdered}
                    </div>
                  ) : (
                    <span className="library-print-upgrade">
                      {t.dashboard.library.orderPrint}
                      <ArrowRight aria-hidden="true" />
                    </span>
                  )}
                </div>
              </article>
            );
          })}
        </div>
      </section>
    </>
  );
}
