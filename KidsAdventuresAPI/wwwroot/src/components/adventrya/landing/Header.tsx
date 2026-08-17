import { useState } from "react";
import { Link, useLocation } from "@tanstack/react-router";

import { LanguageSwitcher } from "@/components/adventrya/LanguageSwitcher";
import { useT } from "@/lib/i18n";

import { ChevronDownIcon, DashboardIcon, GlobeIcon } from "./icons";

/**
 * Back to the top when the logo is already home.
 *
 * A router Link to "/" on "/" is a navigation to where you already are, so nothing happens —
 * which is not what a logo in a long page means to anyone. Exported because the footer carries
 * the same mark and owes the same behaviour.
 */
export function useLogoToTop() {
  const { pathname } = useLocation();
  return (event: React.MouseEvent) => {
    if (pathname !== "/") return;
    event.preventDefault();
    const behavior = window.matchMedia("(prefers-reduced-motion: reduce)").matches
      ? "auto"
      : "smooth";
    /*
      Both, because this page does not scroll where you would expect.

      `body` carries `overflow-y: auto`, which stops viewport propagation and makes the body its
      own scroll container — so `window.scrollTo` moves nothing, and neither does scrolling the
      documentElement. Whichever of the two is the real scroller, one of these reaches it.
    */
    window.scrollTo({ top: 0, behavior });
    document.body.scrollTo({ top: 0, behavior });
  };
}

export function Header() {
  const t = useT();
  const toTop = useLogoToTop();
  const [menuOpen, setMenuOpen] = useState(false);

  const links = (
    <>
      <a href="#books" onClick={() => setMenuOpen(false)}>
        {t.common.nav.books}
      </a>
      <a href="#pricing" onClick={() => setMenuOpen(false)}>
        {t.common.nav.pricing}
      </a>
      <a href="#faq" onClick={() => setMenuOpen(false)}>
        {t.common.nav.faq}
      </a>
    </>
  );

  return (
    <header className="landing-v3-header">
      <Link className="landing-v3-logo" to="/" aria-label={t.common.nav.homeAria} onClick={toTop}>
        ADVENTRYA
        <small>{t.common.brandTagline}</small>
      </Link>

      <nav className="landing-v3-nav" aria-label={t.common.nav.primaryNav}>
        {links}
      </nav>

      <div className="landing-v3-header-actions">
        <LanguageSwitcher
          className="landing-v3-language"
          globe={<GlobeIcon />}
          chevron={<ChevronDownIcon />}
          labelStyle="short"
        />
        <Link
          className="landing-v3-dashboard-link"
          to="/dashboard"
          aria-label={t.common.nav.openDashboard}
        >
          <DashboardIcon />
          <span>{t.common.nav.mySpace}</span>
        </Link>

        {/*
          The same three links, for a phone.

          The nav above is display:none below 1180px and nothing replaced it, so books, pricing
          and the FAQ simply did not exist on a phone — and with the header's own call to action
          gone there was nothing else up here either. This is the smallest thing that gives them
          back: one button, three links.
        */}
        <button
          className={`landing-v3-menu-toggle ${menuOpen ? "is-open" : ""}`}
          type="button"
          aria-expanded={menuOpen}
          aria-label={t.common.nav.primaryNav}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <i aria-hidden="true" />
          <i aria-hidden="true" />
          <i aria-hidden="true" />
        </button>
      </div>

      {menuOpen ? (
        <nav className="landing-v3-menu-sheet" aria-label={t.common.nav.primaryNav}>
          {links}
        </nav>
      ) : null}
    </header>
  );
}
