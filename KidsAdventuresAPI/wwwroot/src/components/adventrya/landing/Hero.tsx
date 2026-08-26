import { Link } from "@tanstack/react-router";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { useT } from "@/lib/i18n";
import { heroDemoPages } from "@/lib/story/heroDemoPages";

import { BookIcon, SparkleIcon, ArrowIcon } from "./icons";

const HERO_NAME = "ზუკა";
const HERO_TITLE = "ზუკა დაკარგულ ხეობაში";

export function Hero() {
  const t = useT();
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
              <Link className="landing-v3-primary" to="/themes">
                {t.landing.hero.primaryCta}
                <ArrowIcon />
              </Link>
              <small>{t.landing.hero.primaryNote}</small>
            </div>
          </div>

          {/*
            The two-price strip that stood here is gone. The pricing section says the same two
            numbers properly, with what each one buys; repeating them under the first sentence
            asked a visitor to compare packages before they knew what the product was.
          */}
        </div>

        <div className="landing-v3-hero-product" aria-label={t.landing.hero.bookExample}>
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
            isSpreadBook
            interactive
            /*
              The book waits to be opened rather than turning itself.

              It used to advance every two seconds, on the reasoning that a shop window should
              move. In practice it took the page out from under anyone who had started reading,
              and a visitor who wanted a second look at a picture had to race it.
            */
            initialIndex={0}
          />
        </div>

        {/*
          The "scroll down" mouse used to sit here. The hero now ends exactly where the viewport
          does, so the shelf below it is already the next thing a wheel reaches — a marker that
          points at the obvious was only spending the last strip of the first screen.
        */}
      </section>
    </>
  );
}
