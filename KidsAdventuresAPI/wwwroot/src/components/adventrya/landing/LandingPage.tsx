import { Link } from "@tanstack/react-router";
import { useState } from "react";

import { Announcement } from "@/components/adventrya/landing/Announcement";
import { Books } from "@/components/adventrya/landing/Books";
import { Header } from "@/components/adventrya/landing/Header";
import { Hero } from "@/components/adventrya/landing/Hero";
import { How } from "@/components/adventrya/landing/How";
import { ArrowIcon, CheckIcon, SparkleIcon, WorldIcon } from "@/components/adventrya/landing/icons";
import { formatGel, useT } from "@/lib/i18n";
import { PRICES } from "@/lib/pricing";
import { WORLD_IDS } from "@/lib/worlds";

const CREATE_PROFILE = "/create";

function Memory() {
  const t = useT();
  const L = t.landing.memory;
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

      <div className="landing-v3-map-story" aria-hidden="true">
        <div className="map-story-path">
          <i />
          <i />
          <i />
          <i />
        </div>
        {L.mapNodes.map((node) => (
          <div key={node.state} className={`map-story-node ${node.state}`}>
            <span>
              {node.state === "future" ? (
                "…"
              ) : (
                <WorldIcon type={node.state === "next" ? "space" : "dinosaurs"} />
              )}
            </span>
            <small>{node.label}</small>
            <strong>{node.title}</strong>
          </div>
        ))}
      </div>
    </section>
  );
}

function Worlds() {
  const t = useT();
  const L = t.landing.worlds;
  return (
    <section id="worlds" className="landing-v3-section landing-v3-worlds">
      <div className="landing-v3-section-heading">
        <p>
          <span aria-hidden="true" />
          {L.eyebrow}
        </p>
        <h2>
          {L.titleLine1} <em>{L.titleEm}</em>
        </h2>
        <span>{L.lead}</span>
      </div>

      <div className="landing-v3-world-grid">
        {WORLD_IDS.map((id, index) => {
          const world = t.worlds[id];
          return (
            <Link
              key={id}
              to="/create"
              hash="profile"
              className={`landing-v3-world-card world-card-${index + 1}`}
            >
              <span>
                <WorldIcon type={id} />
              </span>
              <small>{world.theme}</small>
              <strong>{world.place}</strong>
              <i>
                <ArrowIcon />
              </i>
            </Link>
          );
        })}
      </div>
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
          <a href={`/create?package=Digital#profile`}>
            {L.digital.cta}
            <ArrowIcon />
          </a>
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
          <a href={`/create?package=Print#profile`}>
            {L.print.cta}
            <ArrowIcon />
          </a>
          <p className="landing-v3-upgrade">{L.print.upgrade}</p>
        </article>
      </div>
    </section>
  );
}

function Assurance() {
  const t = useT();
  return (
    <section className="landing-v3-assurance" aria-label="რატომ Adventrya">
      {t.landing.pricing.assurance.map((item) => (
        <div key={item.title}>
          <small>{item.title}</small>
          <strong>{item.heading}</strong>
          <p>{item.body}</p>
        </div>
      ))}
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
        <Link to={CREATE_PROFILE} hash="profile">
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
  return (
    <footer className="landing-v3-footer">
      <div>
        <Link to="/" className="landing-v3-logo">
          ADVENTRYA
          <small>{t.common.brandTagline}</small>
        </Link>
        <p>{F.blurb}</p>
      </div>
      <nav>
        <div>
          <strong>{F.product}</strong>
          <Link to={CREATE_PROFILE} hash="profile">
            {t.common.nav.createBook}
          </Link>
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

function MobileCta() {
  const t = useT();
  return (
    <div className="landing-v3-mobile-cta">
      <Link to={CREATE_PROFILE} hash="profile">
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
      <Announcement />
      <Header />
      <main>
        <Hero />
        <Books />
        <How />
        <Memory />
        <Worlds />
        <Pricing />
        <Assurance />
        <Voices />
        <Faq />
        <Final />
      </main>
      <Footer />
      <MobileCta />
    </div>
  );
}
