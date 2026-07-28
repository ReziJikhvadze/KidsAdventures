import { Link } from "@tanstack/react-router";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { t, formatGel } from "@/lib/i18n";
import { PRICES } from "@/lib/pricing";
import { heroDemoPages } from "@/lib/story/heroDemoPages";

import { BookIcon, SparkleIcon, ArrowIcon } from "./icons";

const HERO_NAME = "ზუკა";
const HERO_TITLE = "ზუკა და დაკარგული ხეობის საიდუმლო";

export function Hero() {
  const pages = heroDemoPages(HERO_NAME, "dinosaurs");

  return (
    <>
      <section className="landing-v3-hero" aria-labelledby="landing-v3-title">
        <div className="landing-v3-hero-art" aria-hidden="true" />
        <div className="landing-v3-hero-wash" aria-hidden="true" />
        <div className="landing-v3-stars" aria-hidden="true" />

        <div className="landing-v3-hero-copy">
          <p className="landing-v3-kicker">
            <span />
            {t.landing.hero.kicker}
          </p>
          <h1 id="landing-v3-title">
            <span>{t.landing.hero.titleLine1}</span>
            <em>{t.landing.hero.titleEm}</em>
          </h1>
          <p className="landing-v3-hero-lead">{t.landing.hero.lead}</p>

          <div className="landing-v3-hero-actions">
            <div className="landing-v3-primary-wrap">
              <Link className="landing-v3-primary" to="/create" hash="profile">
                {t.landing.hero.primaryCta}
                <ArrowIcon />
              </Link>
              <small>{t.landing.hero.primaryNote}</small>
            </div>
            <a className="landing-v3-secondary" href="#books">
              <BookIcon />
              {t.landing.hero.secondaryCta}
            </a>
          </div>

          <div className="landing-v3-hero-proof">
            <div>
              <strong>{formatGel(PRICES.digital)}</strong>
              <span>{t.landing.hero.proofDigital}</span>
            </div>
            <i />
            <div>
              <strong>{formatGel(PRICES.print)}</strong>
              <span>{t.landing.hero.proofPrint}</span>
            </div>
          </div>
        </div>

        <div className="landing-v3-hero-product" aria-label={t.landing.hero.bookExample}>
          <div className="landing-v3-floating-note note-one">
            <SparkleIcon />
            <span>
              <small>{t.landing.hero.floatingNoteOne}</small>
              {t.landing.hero.floatingNoteTwo}
            </span>
          </div>

          {/* Demo uses interactive Ot storybook (variant=hero), not a static cover stack. */}
          <StorybookVolume
            className="storybook storybook-hero theme-dinosaurs"
            variant="hero"
            heroName={HERO_NAME}
            title={HERO_TITLE}
            worldId="dinosaurs"
            pages={pages}
            lockedPageCount={0}
            isUnlocked
            interactive
            initialIndex={0}
          />

          <div className="landing-v3-floating-note note-two">
            <span className="landing-v3-avatar">ზ</span>
            <span>
              <small>{t.landing.hero.confidence[0]}</small>
              {t.landing.hero.confidence[1]}
            </span>
          </div>
        </div>

        <a className="landing-v3-scroll" href="#books">
          <span />
          {t.landing.hero.scroll}
        </a>
      </section>

      <section className="landing-v3-confidence" aria-label={t.landing.benefits.eyebrow}>
        {t.landing.benefits.items.map((item, index) => (
          <div key={item.title}>
            <span className="landing-v3-confidence-icon">{String(index + 1).padStart(2, "0")}</span>
            <p>
              <strong>{item.title}</strong>
              {item.body}
            </p>
          </div>
        ))}
      </section>
    </>
  );
}
