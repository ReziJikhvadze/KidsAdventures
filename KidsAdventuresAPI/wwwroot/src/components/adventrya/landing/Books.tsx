import { ChevronLeft, ChevronRight } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import { useT } from "@/lib/i18n";
import { EXAMPLE_BOOK_ART, type WorldId } from "@/lib/worlds";

/*
  Slow enough to read a title, quick enough that a reader who glances away and back has moved.

  The band is not a slideshow anybody came for; it is a shelf that turns itself so the two worlds
  off the right edge get seen at all. Four and a half seconds is about as long as a cover holds a
  glance before it stops being new.
*/
const ADVANCE_MS = 4500;

/*
  One card plus the gap, measured rather than assumed: the gap is a clamp() and the card width is
  a fraction of the rail, so neither is a number this file can know.

  A module function rather than a line inside the timer, because the arrows step by exactly the
  same amount. A shelf whose two ways of moving disagree about what one move is lands the covers
  between snap points, and `scroll-snap-type: x mandatory` then drags them somewhere neither the
  reader nor the timer asked for.
*/
function cardStep(strip: HTMLElement) {
  const card = strip.firstElementChild as HTMLElement | null;
  if (!card) return 0;
  const gap = Number.parseFloat(getComputedStyle(strip).columnGap || "0") || 0;
  return card.getBoundingClientRect().width + gap;
}

export function Books() {
  const t = useT();
  const rail = useRef<HTMLDivElement | null>(null);
  /*
    The shelf stops turning itself once it has been moved by hand, and the arrows count as hands.

    A ref rather than the effect-local flag this used to be: the buttons sit outside the rail, so
    a press on one never reaches the rail's own `pointerdown`, and a shelf that resumed sliding
    four seconds after a reader pressed "next" would move under them exactly when they had just
    said where they wanted to be.
  */
  const handedOver = useRef(false);
  /* Both false while all six covers fit, which is what hides the arrows rather than greying
     them: an arrow on a shelf that cannot move is a control that does nothing. */
  const [reach, setReach] = useState({ back: false, on: false });

  /*
    The shelf turns itself, and stops the moment it is touched.

    Six covers do not fit a screen, so a reader who never drags sees three of the six and has no
    reason to think there are more. This walks the rail along on its own.

    Four conditions, all of them the ones `StorybookVolume` already established for the book on
    this page: nothing moves under `prefers-reduced-motion`, nothing moves in a tab nobody is
    looking at, nothing moves while the reader is scrolling it themselves, and the first touch
    hands the rail over for good - a shelf that keeps sliding out from under a finger is worse
    than one that never moved.
  */
  useEffect(() => {
    const strip = rail.current;
    if (!strip) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const handOver = () => {
      handedOver.current = true;
    };

    // Pointer and wheel rather than `scroll`: the rail's own animated scrolling fires `scroll`
    // too, and listening for that would stop it on its first step.
    strip.addEventListener("pointerdown", handOver, { passive: true });
    strip.addEventListener("wheel", handOver, { passive: true });
    strip.addEventListener("touchstart", handOver, { passive: true });

    const id = window.setInterval(() => {
      if (handedOver.current || document.hidden) return;

      const step = cardStep(strip);
      if (!step) return;
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

  /*
    Where the shelf stands: at the start, at the end, or between.

    Read from the element rather than counted from the six covers, because what matters is how
    much is off-screen, and that depends on the width the cards settled on - three on a laptop,
    one and a bit on a phone. Recomputed on scroll, which the timer's own stepping fires too, and
    on resize, which is what takes the arrows away if the rail ever stops overflowing.
  */
  useEffect(() => {
    const strip = rail.current;
    if (!strip) return;

    const measure = () => {
      // A pixel of slack, for the same reason as above.
      const max = strip.scrollWidth - strip.clientWidth;
      setReach({ back: strip.scrollLeft > 1, on: strip.scrollLeft < max - 1 });
    };

    measure();
    strip.addEventListener("scroll", measure, { passive: true });
    const observer = new ResizeObserver(measure);
    observer.observe(strip);
    return () => {
      strip.removeEventListener("scroll", measure);
      observer.disconnect();
    };
  }, []);

  /* Either end reachable means there is something off the edge, whichever way it sits. */
  const scrolls = reach.back || reach.on;

  const nudge = (direction: -1 | 1) => {
    const strip = rail.current;
    if (!strip) return;
    if (direction < 0 ? !reach.back : !reach.on) return;
    handedOver.current = true;
    strip.scrollBy({
      left: direction * cardStep(strip),
      behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
    });
  };

  return (
    <section className="landing-v3-books landing-v3-section" id="books">
      {/* The h2 went: the books below say what they are, and the eyebrow already names them. */}
      <div className="landing-v3-section-heading">
        <p>{t.landing.books.eyebrow}</p>
        <span>{t.landing.books.lead}</span>
      </div>

      {/*
        The shelf, and the two ways to move it.

        The rail used to be the whole of this section's body, and the only way past the third
        cover was to drag it or to wait for it to turn itself. A drag is a gesture a touchscreen
        teaches and a mouse does not, so on the screen this page is most often opened on there
        were three books and no sign of the other three. The arrows say there are more, and they
        hand back the timing, which is the part a reader actually wants.
      */}
      <div className="landing-v3-book-rail">
        {/*
          Greyed at the end of the shelf rather than taken away, the way the hero picker's arrows
          already are: removing the control you have just pressed drops the keyboard focus to the
          top of the document, so a visitor tabbing through the covers would be thrown out of the
          shelf by reaching the end of it. `disabled` does the same thing - a focused control
          that becomes disabled is blurred - so the end is said with `aria-disabled` and a press
          that does nothing. Both arrows go only when nothing can scroll at all.
        */}
        <button
          type="button"
          className="landing-v3-book-arrow"
          aria-label={t.landing.books.shelfBack}
          hidden={!scrolls}
          aria-disabled={!reach.back}
          onClick={() => nudge(-1)}
        >
          <ChevronLeft aria-hidden="true" size={19} />
        </button>

        <div className="landing-v3-book-gallery" ref={rail}>
          {t.landing.books.examples.map((example) => {
            const theme = example.theme as WorldId;
            /*
              The printed book, photographed, rather than a picture dressed as one.

              What stood here was the world's scene painting with a cream offset behind it for a
              stack of paper, a strip down its left for a spine and a gradient over it so a title
              could be typed on. Every one of those was CSS imitating an object this product now
              has photographs of, and the imitation is the thing a parent is being asked to
              believe in. These carry their own spine, their own page block and their own printed
              title.
            */
            const image = EXAMPLE_BOOK_ART[theme] ?? EXAMPLE_BOOK_ART.dinosaurs;
            return (
              <article key={example.theme} className="landing-v3-example-book">
                {/*
                  The book is the link, and it is the only one.

                  It used to be a picture with a "create a similar one" line under it - a second
                  thing to read before the obvious thing could be done. A book cover is already a
                  door; making it one costs nothing to explain. It opens the map with this world
                  already lit, because choosing the world is the step that actually comes next,
                  and /themes reads ?world= on arrival.

                  The caption is inside it rather than beside it for the same reason: one target,
                  and the words under a cover belong to the cover. It sits under the photograph
                  now instead of over the artwork - the title is printed on the object at a size
                  that reads in the hand and not on a 368-pixel card, so the card says it again
                  in type it can set at its own size.
                */}
                <a
                  className="landing-v3-example-link"
                  href={`/themes?world=${theme}`}
                  aria-label={t.landing.books.exampleAlt(example.title)}
                >
                  <span className="landing-v3-example-art">
                    <img src={image} alt="" loading="lazy" />
                  </span>
                  <span className="landing-v3-example-title">
                    <small>{example.meta}</small>
                    <strong>{example.title}</strong>
                  </span>
                </a>
              </article>
            );
          })}
        </div>

        <button
          type="button"
          className="landing-v3-book-arrow"
          aria-label={t.landing.books.shelfOn}
          hidden={!scrolls}
          aria-disabled={!reach.on}
          onClick={() => nudge(1)}
        >
          <ChevronRight aria-hidden="true" size={19} />
        </button>
      </div>
    </section>
  );
}
