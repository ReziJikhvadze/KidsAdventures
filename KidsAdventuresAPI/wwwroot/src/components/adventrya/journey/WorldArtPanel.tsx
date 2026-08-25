import { useT } from "@/lib/i18n";
import { ISLAND_SPOTS, SELECTOR_ART, SELECTOR_WORLDS } from "@/lib/journey/worldSelector";
import { useWorldById, type WorldId } from "@/lib/worlds";

type Props = {
  worldId: WorldId;
  /**
   * How much of the painting's width the panel shows. Smaller is closer: 0.34 frames one island
   * with its clouds, which is the crop the map itself uses when it lights a world.
   */
  window?: number;
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
 * with the map, and a repainted master arrives here on the same deploy. The island's own
 * coordinates come from ISLAND_SPOTS — the same numbers the hotspots and the star's flight use.
 */
export function WorldArtPanel({ worldId, window: windowShare = 0.34, className }: Props) {
  const t = useT();
  const worldById = useWorldById();
  const place = worldById[worldId];

  /*
    The selector's own id for this world, which is what the island coordinates are keyed by. A
    world with no island on the painting — there is none today, but the catalogue is longer than
    the map — draws nothing rather than a crop of somebody else's island.
  */
  const selectorId = SELECTOR_WORLDS.find((world) => world.worldId === worldId)?.id;
  if (!selectorId) return null;

  const spot = ISLAND_SPOTS.desktop[selectorId];

  /*
    Framing, exactly.

    The plate carries the painting at its own aspect ratio and is grown until it covers the panel
    in both directions. Then two transforms, read right to left: the island's centre is moved to
    the plate's centre, the plate is scaled about that same centre — so the island stays on it —
    and the plate is finally pulled back by half its size, having been pinned at the panel's
    middle. The island lands in the middle of the panel whatever shape the panel is.
  */
  const zoom = 1 / windowShare;
  const style = {
    "--world-art": `url("${SELECTOR_ART.desktop}")`,
    "--world-zoom": zoom,
    "--world-dx": `${50 - spot.cx}%`,
    "--world-dy": `${50 - spot.cy}%`,
  } as React.CSSProperties;

  return (
    <figure className={`world-art-panel${className ? ` ${className}` : ""}`} style={style}>
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
