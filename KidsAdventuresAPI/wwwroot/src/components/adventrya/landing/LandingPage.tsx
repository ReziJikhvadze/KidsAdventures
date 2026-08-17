import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";

import { WorldMapCanvas, WorldMapPin } from "@/components/adventrya/journey/WorldMapCanvas";
import { Books } from "@/components/adventrya/landing/Books";
import { Header, useLogoToTop } from "@/components/adventrya/landing/Header";
import { Hero } from "@/components/adventrya/landing/Hero";
import { ArrowIcon, CheckIcon, SparkleIcon } from "@/components/adventrya/landing/icons";
import { formatGel, useT } from "@/lib/i18n";
import { PRICES } from "@/lib/pricing";
import { useWorlds, type WorldId } from "@/lib/worlds";

/* Every generic call to action starts at the world, which is now the first step. */
const START_JOURNEY = "/themes";

/**
 * The section that says the world outlives the book — and now shows it.
 *
 * What stood here was a drawing of a map: three cards pinned to the corners of an empty box with
 * a dashed CSS arc between them, naming two worlds and a placeholder. It described the product
 * instead of being it, and every word of it was a second copy of something the app already knew.
 * This is the real map from /themes — the same component reading the same anchor table — so the
 * section cannot name a world the picker does not have, or put an island where the painting has
 * open sea.
 *
 * It stays a marketing section, though: nothing is chosen here. A pin lights when it is pointed
 * at and leads into the journey with that world already picked.
 */
function Memory() {
  const t = useT();
  const L = t.landing.memory;
  const worlds = useWorlds();
  // The one piece of state: which island the light is on. Not a selection — it is forgotten the
  // moment the pointer leaves, and nothing on the page reads it but the glow.
  const [lit, setLit] = useState<WorldId | null>(null);
  return (
    <section className="landing-v3-memory">
      <div className="landing-v3-memory-art" aria-hidden="true" />
      <div className="landing-v3-memory-shade" aria-hidden="true" />

      <div className="landing-v3-memory-copy">
        <p>
          <SparkleIcon />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1}
          <em>{L.titleEm}</em>
        </h2>
        <span>{L.lead}</span>
        <div className="landing-v3-memory-chain">
          <div>
            <small>{L.chain[0].label}</small>
            <strong>{L.chain[0].title}</strong>
            <span>{L.chain[0].note}</span>
          </div>
          <i aria-hidden="true">
            <ArrowIcon />
          </i>
          <div>
            <small>{L.chain[1].label}</small>
            <strong>{L.chain[1].title}</strong>
            <span>{L.chain[1].note}</span>
          </div>
        </div>
        <Link to="/world">{L.cta}</Link>
      </div>

      <WorldMapCanvas focusId={lit} className="landing-v3-memory-map">
        {worlds.map((world) => (
          <WorldMapPin
            key={world.id}
            world={world}
            as="entrance"
            label={L.mapPin(world.mapTitle)}
            onMouseEnter={() => setLit(world.id)}
            onMouseLeave={() => setLit(null)}
            onFocus={() => setLit(world.id)}
            onBlur={() => setLit(null)}
          />
        ))}
      </WorldMapCanvas>
    </section>
  );
}

function Pricing() {
  const t = useT();
  const L = t.landing.pricing;
  return (
    <section id="pricing" className="landing-v3-section landing-v3-pricing">
      <div className="landing-v3-pricing-copy">
        <p>
          <span aria-hidden="true" />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1} <em>{L.titleEm}</em>
        </h2>
        <span>{L.lead}</span>
        <ul>
          {t.landing.benefits.items.map((item) => (
            <li key={item.title}>
              <CheckIcon />
              {item.title}
            </li>
          ))}
        </ul>
      </div>

      <div className="landing-v3-price-cards">
        <article>
          <div className="landing-v3-price-card-head">
            <span>{L.digital.name}</span>
            <small>{L.digital.note}</small>
          </div>
          <strong className="landing-v3-price">{formatGel(PRICES.digital)}</strong>
          <ul>
            {L.digital.features.map((feature) => (
              <li key={feature}>
                <CheckIcon />
                {feature}
              </li>
            ))}
          </ul>
          <Link to={START_JOURNEY}>
            {L.digital.cta}
            <ArrowIcon />
          </Link>
          <p className="landing-v3-upgrade">{L.digital.upgrade}</p>
        </article>

        <article className="featured">
          <span className="landing-v3-popular">{L.popular}</span>
          <div className="landing-v3-price-card-head">
            <span>{L.print.name}</span>
            <small>{L.print.note}</small>
          </div>
          <strong className="landing-v3-price">{formatGel(PRICES.print)}</strong>
          <ul>
            {L.print.features.map((feature) => (
              <li key={feature}>
                <CheckIcon />
                {feature}
              </li>
            ))}
          </ul>
          <Link to={START_JOURNEY}>
            {L.print.cta}
            <ArrowIcon />
          </Link>
          <p className="landing-v3-upgrade">{L.print.upgrade}</p>
        </article>
      </div>
    </section>
  );
}

function Voices() {
  const t = useT();
  const L = t.landing.voices;
  return (
    <section className="landing-v3-section landing-v3-voices">
      <div className="landing-v3-voices-heading">
        <p>
          <span aria-hidden="true" />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1} <em>{L.titleEm}</em>
        </h2>
      </div>
      <div className="landing-v3-quote-grid">
        {L.quotes.map((item) => (
          <blockquote key={item.author}>
            <SparkleIcon />
            <p>{item.quote}</p>
            <footer>{item.author}</footer>
          </blockquote>
        ))}
      </div>
      <p className="landing-v3-prototype-note">{L.prototypeNote}</p>
    </section>
  );
}

function Faq() {
  const t = useT();
  const L = t.landing.faq;
  const [openIndex, setOpenIndex] = useState(0);

  return (
    <section id="faq" className="landing-v3-section landing-v3-faq">
      <div className="landing-v3-faq-heading">
        <p>
          <span aria-hidden="true" />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1} <em>{L.titleEm}</em>
        </h2>
        <span>
          <Link to="/contact">{L.contactLink}</Link>
        </span>
      </div>

      <div className="landing-v3-faq-list">
        {L.items.map((item, index) => (
          <details
            key={item.question}
            open={openIndex === index}
            onToggle={(event) => {
              if ((event.target as HTMLDetailsElement).open) setOpenIndex(index);
            }}
          >
            <summary>
              <span>{String(index + 1).padStart(2, "0")}</span>
              {item.question}
              <i aria-hidden="true">+</i>
            </summary>
            <p>{item.answer}</p>
          </details>
        ))}
      </div>
    </section>
  );
}

function Final() {
  const t = useT();
  const L = t.landing.final;
  return (
    <section className="landing-v3-final">
      <div className="landing-v3-final-art" aria-hidden="true" />
      <div className="landing-v3-final-wash" aria-hidden="true" />
      <div className="landing-v3-final-copy">
        <p>
          <SparkleIcon />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1}
          <em>{L.titleEm}</em>
        </h2>
        <span>{L.lead}</span>
        <Link to={START_JOURNEY}>
          {t.landing.hero.primaryCta}
          <ArrowIcon />
        </Link>
        <small>{L.note}</small>
      </div>
    </section>
  );
}

function Footer() {
  const t = useT();
  const F = t.common.footer;
  // The footer mark is the same mark, and at the bottom of a long page it is the one a reader is
  // actually next to when they want the top.
  const toTop = useLogoToTop();
  return (
    <footer className="landing-v3-footer">
      <div>
        <Link to="/" className="landing-v3-logo" onClick={toTop}>
          ADVENTRYA
          <small>{t.common.brandTagline}</small>
        </Link>
        <p>{F.blurb}</p>
      </div>
      <nav>
        <div>
          <strong>{F.product}</strong>
          <Link to={START_JOURNEY}>{t.common.nav.createBook}</Link>
          <a href="#pricing">{t.common.nav.pricing}</a>
          <Link to="/world">{F.myWorld}</Link>
        </div>
        <div>
          <strong>{F.help}</strong>
          <Link to="/contact">{F.contact}</Link>
          <a href="#faq">{t.common.nav.faq}</a>
          <Link to="/dashboard">{F.adventureMap}</Link>
        </div>
        <div>
          <strong>{F.legal}</strong>
          <Link to="/privacy">კონფიდენციალურობა</Link>
          <Link to="/terms">წესები და პირობები</Link>
        </div>
      </nav>
      <p className="landing-v3-footer-bottom">{F.madeIn}</p>
    </footer>
  );
}

/**
 * The bar that follows you down the page.
 *
 * It used to be fixed and always on, so the first screen of a phone carried three ways to start
 * a book at once: this one, the one in the header, and the one under the headline. It now waits
 * until the headline's button has scrolled out of sight, which is the only moment it adds
 * anything — and the header's own button is gone on small screens.
 */
function MobileCta() {
  const t = useT();
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const anchor = document.querySelector(".landing-v3-primary");
    if (!anchor || typeof IntersectionObserver === "undefined") {
      // No hero button to follow: better to show the bar than to hide the action entirely.
      setVisible(true);
      return;
    }

    const observer = new IntersectionObserver(([entry]) => setVisible(!entry.isIntersecting), {
      rootMargin: "-8px",
    });
    observer.observe(anchor);
    return () => observer.disconnect();
  }, []);

  return (
    <div className={`landing-v3-mobile-cta ${visible ? "is-visible" : ""}`} aria-hidden={!visible}>
      <Link to={START_JOURNEY} tabIndex={visible ? undefined : -1}>
        {t.landing.hero.primaryCta}
        <ArrowIcon />
      </Link>
    </div>
  );
}

/** Landing markup matches Partner Demo v13 class tree (Hero/Header/Books/How modules). */
export function LandingPage() {
  return (
    <div className="landing-v3">
      <Header />
      <main>
        <Hero />
        <Books />
        <Memory />
        <Pricing />
        <Voices />
        <Faq />
        <Final />
      </main>
      <Footer />
      <MobileCta />
    </div>
  );
}
