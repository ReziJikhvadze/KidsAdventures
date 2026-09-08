import { useEffect, useRef } from "react";

import { useT } from "@/lib/i18n";
import { WORLD_SCENE_ART, type WorldId } from "@/lib/worlds";

/*
  Slow enough to read a title, quick enough that a reader who glances away and back has moved.

  The band is not a slideshow anybody came for; it is a shelf that turns itself so the two worlds
  off the right edge get seen at all. Four and a half seconds is about as long as a cover holds a
  glance before it stops being new.
*/
const ADVANCE_MS = 4500;

export function Books() {
  const t = useT();
  const rail = useRef<HTMLDivElement | null>(null);

  /*
    The shelf turns itself, and stops the moment it is touched.

    Six covers do not fit a screen, so a reader who never drags sees three of the six and has no
    reason to think there are more. This walks the rail along on its own.

    Four conditions, all of them the ones `StorybookVolume` already established for the book on
    this page: nothing moves under `prefers-reduced-motion`, nothing moves in a tab nobody is
    looking at, nothing moves while the reader is scrolling it themselves, and the first touch
    hands the rail over for good — a shelf that keeps sliding out from under a finger is worse
    than one that never moved.
  */
  useEffect(() => {
    const strip = rail.current;
    if (!strip) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    let handedOver = false;
    const handOver = () => {
      handedOver = true;
    };

    // Pointer and wheel rather than `scroll`: the rail's own animated scrolling fires `scroll`
    // too, and listening for that would stop it on its first step.
    strip.addEventListener("pointerdown", handOver, { passive: true });
    strip.addEventListener("wheel", handOver, { passive: true });
    strip.addEventListener("touchstart", handOver, { passive: true });

    const id = window.setInterval(() => {
      if (handedOver || document.hidden) return;

      const card = strip.firstElementChild as HTMLElement | null;
      if (!card) return;

      // One card plus the gap, measured rather than assumed: the gap is a clamp() and the card
      // width is a fraction of the viewport, so neither is a number this file can know.
      const gap = Number.parseFloat(getComputedStyle(strip).columnGap || "0") || 0;
      const step = card.getBoundingClientRect().width + gap;
      const end = strip.scrollWidth - strip.clientWidth;

      // A pixel of slack: a smooth scroll lands fractionally short of the end.
      const next = strip.scrollLeft >= end - 1 ? 0 : strip.scrollLeft + step;
      strip.scrollTo({ left: next, behavior: "smooth" });
    }, ADVANCE_MS);

    return () => {
      window.clearInterval(id);
      strip.removeEventListener("pointerdown", handOver);
      strip.removeEventListener("wheel", handOver);
      strip.removeEventListener("touchstart", handOver);
    };
  }, []);

  return (
    <section className="landing-v3-books landing-v3-section" id="books">
      {/* The h2 went: the books below say what they are, and the eyebrow already names them. */}
      <div className="landing-v3-section-heading">
        <p>{t.landing.books.eyebrow}</p>
        <span>{t.landing.books.lead}</span>
      </div>

      <div className="landing-v3-book-gallery" ref={rail}>
        {t.landing.books.examples.map((example) => {
          const theme = example.theme as WorldId;
          /*
            The world's own painting, not the island cut out of the map.

            These are the six scenes painted one to a picture — sky, light and all — which is what
            a cover looks like. The cut-outs this replaces were transparent islands sitting on a
            flat purple ground, which is what a map marker looks like.
          */
          const image = WORLD_SCENE_ART[theme] ?? WORLD_SCENE_ART.dinosaurs;
          return (
            <article key={example.theme} className="landing-v3-example-book">
              {/*
                The cover is the link, and it is the only one.

                It used to be a picture with a "create a similar one" line under it — a second
                thing to read before the obvious thing could be done. A book cover is already a
                door; making it one costs nothing to explain. It opens the map with this world
                already lit, because choosing the world is the step that actually comes next,
                and /themes reads ?world= on arrival.
              */}
              <a
                className="landing-v3-example-art"
                href={`/themes?world=${theme}`}
                aria-label={t.landing.books.exampleAlt(example.title)}
              >
                <img src={image} alt="" loading="lazy" />
                <div className="landing-v3-example-overlay" />
                <div className="landing-v3-example-title">
                  <small>{example.meta}</small>
                  <strong>{example.title}</strong>
                </div>
                <span className="landing-v3-book-spine" />
              </a>
            </article>
          );
        })}
      </div>
    </section>
  );
}
