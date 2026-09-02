import { useEffect, useRef, useState } from "react";

import { useT } from "@/lib/i18n";
import { ISLAND_FRAMES, SELECTOR_ART, SELECTOR_WORLDS } from "@/lib/journey/worldSelector";
import { useWorldById, type WorldId } from "@/lib/worlds";

type Props = {
  worldId: WorldId;
  /**
   * How much room to leave around the island, as a multiple of its own size. 1 would crop to its
   * exact edges; 1.1 leaves a little of the sky it stands in. It was 1.35, which on boxes drawn
   * around the picture rather than around the pin pulled a neighbouring island into every frame.
   */
  margin?: number;
  className?: string;
};

/**
 * The chosen island, lifted out of the map and framed.
 *
 * The world picker is the best-looking thing in this product and the create form was the
 * plainest, which meant the moment a parent committed to a world it vanished from the screen and
 * they filled in a form beside empty space. This carries the painting forward: the same master
 * art, the same amber focus glow, the same dimmer and vignette, framed on the island the parent
 * chose so it fills the room the form leaves.
 *
 * It is a crop of the master rather than a second asset, so there is nothing new to keep in step
 * with the map, and a repainted master arrives here on the same deploy. The coordinates come from
 * ISLAND_FRAMES, which is the painting's own bounds — not ISLAND_SPOTS, which are tap targets on
 * the map: tighter than the picture and centred on the pin, so cropping to them took the head off
 * the dinosaur and left the forest in a corner.
 */
export function WorldArtPanel({ worldId, margin = 1.1, className }: Props) {
  const t = useT();
  const worldById = useWorldById();
  const place = worldById[worldId];

  /*
    The selector's own id for this world, which is what the island coordinates are keyed by. A
    world with no island on the painting — there is none today, but the catalogue is longer than
    the map — draws nothing rather than a crop of somebody else's island.
  */
  const selectorId = SELECTOR_WORLDS.find((world) => world.worldId === worldId)?.id;

  /*
    The island, or a stand-in for one.

    The bail-out for a world with no island used to sit here, above the hooks — which made every
    hook below it conditional, and a component whose hook order changes with its props is a
    component React will eventually render with somebody else's state. The measurement runs for
    every world; the decision not to draw is taken at the end, where returning early costs
    nothing.
  */
  const spot = selectorId ? ISLAND_FRAMES[selectorId] : ISLAND_FRAMES.animals;

  /*
    Framing, measured.

    The island has to fit the panel with room around it, and how much of the painting a panel can
    show depends on the panel's shape — which is decided by the page, not by this file. A single
    hand-picked zoom is therefore always wrong somewhere: the one that suited the forest cut the
    long-necked one in the dinosaur valley off at the neck, because that island is tall and the
    panel beside a form is narrow.

    So the zoom is worked out from the panel as it actually is. The plate covers the panel; from
    that, the scale at which the island's own footprint — plus its margin — still fits inside both
    edges is arithmetic. Never below 1, or the plate would stop covering and show its corners.
  */
  const panelRef = useRef<HTMLElement | null>(null);
  const [zoom, setZoom] = useState(1.8);

  useEffect(() => {
    const panel = panelRef.current;
    if (!panel) return;

    const fit = () => {
      const { width, height } = panel.getBoundingClientRect();
      if (!width || !height) return;

      const artRatio = 1672 / 941;
      const plateWidth = Math.max(width, height * artRatio);
      const plateHeight = plateWidth / artRatio;

      const byWidth = (width * 100) / (spot.w * margin * plateWidth);
      const byHeight = (height * 100) / (spot.h * margin * plateHeight);
      setZoom(Math.max(1, Math.min(byWidth, byHeight)));
    };

    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(panel);
    return () => observer.disconnect();
  }, [spot.w, spot.h, margin]);

  const style = {
    "--world-art": `url("${SELECTOR_ART.desktop}")`,
    "--world-zoom": zoom,
    "--world-dx": `${50 - spot.cx}%`,
    "--world-dy": `${50 - spot.cy}%`,
  } as React.CSSProperties;

  if (!selectorId) return null;

  return (
    <figure
      ref={panelRef}
      className={`world-art-panel${className ? ` ${className}` : ""}`}
      style={style}
    >
      <div className="world-art-plate" role="img" aria-label={place.mapTitle} />

      {/* The map's own light: a warm bloom on the island, a cool wash over the rest, and the
          vignette that keeps the crop from ending in a hard edge. */}
      <span className="world-art-bloom" aria-hidden="true" />
      <span className="world-art-dimmer" aria-hidden="true" />
      <span className="world-art-vignette" aria-hidden="true" />

      <figcaption className="world-art-caption">
        <small>{t.journey.worldSelector.eyebrow}</small>
        <strong>{place.mapTitle}</strong>
      </figcaption>
    </figure>
  );
}
