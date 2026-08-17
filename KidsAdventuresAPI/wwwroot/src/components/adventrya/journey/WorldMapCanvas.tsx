import { Link } from "@tanstack/react-router";
import type { CSSProperties, ReactNode } from "react";

import { WORLD_MAP, WORLD_TINT } from "@/lib/journey/worldMap";
import type { World, WorldId } from "@/lib/worlds";

/**
 * The painted map, and the pins that stand on it.
 *
 * There are two places that show this map: the picker at /themes, where a pin chooses the world
 * the first book happens in, and the memory section on the landing page, where a pin is an
 * invitation to start one. They are the same painting with the same six islands, so they are the
 * same component — the alternative is two copies of the coordinate plumbing, and the copy that
 * nobody is looking at is the one that goes stale the next time the art moves.
 *
 * What each screen still owns is the frame: how big the map is and where it sits. That arrives as
 * a class name, because it is a question about the page, not about the map.
 *
 * Both the wide and the tall coordinates are written onto every element as custom properties and
 * the media queries in first-map.css pick between them, in step with the <picture> below. Nothing
 * here chooses a layout in JavaScript: the server and the browser would disagree about which one
 * is in play on the first paint, and that is a hydration mismatch.
 */

type CanvasProps = {
  /** The world being looked at: the warm light on its island, and the trail out to it. */
  focusId: WorldId | null;
  /** Framing — size, border, where it sits on the page — belongs to the screen around it. */
  className?: string;
  /** The pins, and whatever else a screen floats over the map: the lantern, the guide. */
  children: ReactNode;
  /** True where the map is the first thing on its route; false for a section far down a page. */
  priority?: boolean;
};

export function WorldMapCanvas({ focusId, className, children, priority = false }: CanvasProps) {
  const focusAnchor = focusId ? WORLD_MAP.anchors.wide[focusId] : null;
  const focusAnchorTall = focusId ? WORLD_MAP.anchors.tall[focusId] : null;

  return (
    <div className={`world-map-canvas${className ? ` ${className}` : ""}`}>
      <picture>
        <source
          media="(max-width: 999px) and (orientation: portrait), (min-width: 1000px) and (max-aspect-ratio: 4 / 3)"
          srcSet={WORLD_MAP.optimizedSrc.tall}
          type="image/jpeg"
        />
        <source srcSet={WORLD_MAP.optimizedSrc.wide} type="image/jpeg" />
        <img
          className="first-map-painting"
          src={WORLD_MAP.src.wide}
          alt=""
          aria-hidden="true"
          fetchPriority={priority ? "high" : undefined}
          loading={priority ? undefined : "lazy"}
          decoding={priority ? undefined : "async"}
        />
      </picture>
      <div className="first-map-vignette" aria-hidden="true" />
      {/*
        The warm glow that follows the focused island. Its position comes from the same table the
        pins do, so it cannot drift out of step with them — which is what six hand-written
        `.focus-{id}` rules used to do every time the art moved.
      */}
      <div
        className={`first-map-focus ${focusId ? "is-lit" : ""}`}
        aria-hidden="true"
        style={
          focusAnchor && focusAnchorTall && focusId
            ? ({
                "--focus-x": `${focusAnchor.x}%`,
                "--focus-y": `${focusAnchor.y}%`,
                "--focus-x-tall": `${focusAnchorTall.x}%`,
                "--focus-y-tall": `${focusAnchorTall.y}%`,
                // The island is lit by its own lamp, not a generic warm one.
                "--focus-tint": WORLD_TINT[focusId].tint,
              } as CSSProperties)
            : undefined
        }
      />
      {focusId ? (
        <>
          <svg
            className="first-map-selection-route first-map-selection-route-wide"
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path key={`wide-${focusId}`} d={WORLD_MAP.routes.wide[focusId]} pathLength="1" />
          </svg>
          <svg
            className="first-map-selection-route first-map-selection-route-tall"
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path key={`tall-${focusId}`} d={WORLD_MAP.routes.tall[focusId]} pathLength="1" />
          </svg>
        </>
      ) : null}
      {children}
    </div>
  );
}

/**
 * One island's gateway.
 *
 * The illustration is the portal; this is a generous invisible hit area over it plus the short
 * name of the place. What the two maps disagree about is only what a click means — the picker
 * answers its own question in place, the landing page hands the answer to the journey — so that
 * is the one thing the caller chooses. Everything visible is decided here.
 */
type PinProps = {
  world: World;
  /** The map shows the short name; a screen reader is told the whole invitation. */
  label: string;
  /** Lit: the world the screen is currently describing. */
  focus?: boolean;
  /**
   * Preview handlers, passed straight through rather than invented here: the picker lights a
   * pin on enter and keeps it lit until focus leaves, which is not what a marketing map wants.
   */
  onMouseEnter?: () => void;
  onMouseLeave?: () => void;
  onFocus?: () => void;
  onBlur?: () => void;
} & (
  | {
      /** The picker: choosing happens on this screen, so the pin is a button. */
      as: "choice";
      selected: boolean;
      disabled?: boolean;
      onSelect: () => void;
    }
  | {
      /** The landing page: the pin is the way in, with this world already chosen. */
      as: "entrance";
    }
);

export function WorldMapPin(props: PinProps) {
  const { world, label, focus = false, onMouseEnter, onMouseLeave, onFocus, onBlur } = props;
  const wide = WORLD_MAP.anchors.wide[world.id];
  const tall = WORLD_MAP.anchors.tall[world.id];
  const selected = props.as === "choice" && props.selected;

  /* first-node-{id} carries no position any more; it is left for the handful of per-world label
     nudges the narrow layout still needs. */
  const chrome = {
    className: `world-pick-pin first-node-${world.id}${selected ? " is-selected" : ""}${focus ? " is-focus" : ""}`,
    "data-first-world-id": world.id,
    "data-side": wide.side,
    style: {
      "--pin-x": `${wide.x}%`,
      "--pin-y": `${wide.y}%`,
      "--pin-x-tall": `${tall.x}%`,
      "--pin-y-tall": `${tall.y}%`,
      "--hit-width": `${wide.hitWidth}%`,
      "--hit-height": `${wide.hitHeight}%`,
      "--hit-width-tall": `${tall.hitWidth}%`,
      "--hit-height-tall": `${tall.hitHeight}%`,
      "--world-tint": WORLD_TINT[world.id].tint,
    } as CSSProperties,
    "aria-label": label,
    onMouseEnter,
    onMouseLeave,
    onFocus,
    onBlur,
  };

  /*
    Short name on the map, full title in the panel. The old pins carried the whole book title on
    cards up to 329px wide, which is what made six of them unreadable over one painting.
  */
  const name = <span className="world-pick-pin-name">{world.mapLabel}</span>;

  if (props.as === "entrance") {
    /*
      Straight to the child's details, not to the picker. The world is the answer this pin just
      gave, and ?world= is what the journey draft reads it from — sending a parent to /themes to
      pick again the thing they have already pointed at is a step that asks nothing new.

      A Link, not an <a>: a real navigation would unmount the draft provider on the way in.
    */
    return (
      <Link {...chrome} to="/create" search={{ world: world.id }} hash="profile">
        {name}
      </Link>
    );
  }

  return (
    <button
      {...chrome}
      type="button"
      aria-pressed={props.selected}
      disabled={props.disabled}
      onClick={props.onSelect}
    >
      {name}
    </button>
  );
}
