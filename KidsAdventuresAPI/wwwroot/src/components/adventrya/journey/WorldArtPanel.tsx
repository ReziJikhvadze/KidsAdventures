import { useT } from "@/lib/i18n";
import { useWorldById, WORLD_SCENE_ART, type WorldId } from "@/lib/worlds";

type Props = {
  worldId: WorldId;
  className?: string;
};

/**
 * The chosen world, beside the form.
 *
 * The world picker is the best-looking thing in this product and the create form was the
 * plainest, which meant the moment a parent committed to a world it vanished from the screen and
 * they filled in a form beside empty space. This carries the painting forward: the same place,
 * the same amber bloom, the same dimmer and vignette, filling the room the form leaves.
 *
 * It used to be a crop. The six islands share one canvas for the map, and this panel found its
 * world by scaling that canvas up and sliding the chosen island into the middle — coordinates
 * from `ISLAND_FRAMES`, a zoom worked out from the panel's measured shape by a ResizeObserver,
 * and a neighbouring island always just outside the frame. Every world is now painted on its
 * own, at the scale this panel shows it, so the picture is composed for this shape rather than
 * rescued from another one. There is nothing left to measure and nothing to keep in step with a
 * repainted master.
 *
 * The map's own canvas is untouched and still draws /themes, the cabinet's sky and the landing
 * hero; `ISLAND_FRAMES` still measures against it for the picker.
 */
export function WorldArtPanel({ worldId, className }: Props) {
  const t = useT();
  const worldById = useWorldById();
  const place = worldById[worldId];

  const style = {
    "--world-art": `url("${WORLD_SCENE_ART[worldId]}")`,
  } as React.CSSProperties;

  return (
    <figure className={`world-art-panel${className ? ` ${className}` : ""}`} style={style}>
      <div className="world-art-plate" role="img" aria-label={place.mapTitle} />

      {/* The map's own light: a warm bloom on the island, a cool wash over the rest, and the
          vignette that keeps the picture from ending in a hard edge. */}
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
