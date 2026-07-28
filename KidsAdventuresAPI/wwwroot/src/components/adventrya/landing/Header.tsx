import { Link } from "@tanstack/react-router";

import { t } from "@/lib/i18n";

import { ArrowIcon, ChevronDownIcon, DashboardIcon, GlobeIcon } from "./icons";

export function Header() {
  return (
    <header className="landing-v3-header">
      <Link className="landing-v3-logo" to="/" aria-label={t.common.nav.homeAria}>
        ADVENTRYA
        <small>{t.common.brandTagline}</small>
      </Link>

      <nav className="landing-v3-nav" aria-label={t.common.nav.primaryNav}>
        <a href="#books">{t.common.nav.books}</a>
        <a href="#how">{t.common.nav.howItWorks}</a>
        <a href="#pricing">{t.common.nav.pricing}</a>
        <a href="#faq">{t.common.nav.faq}</a>
      </nav>

      <div className="landing-v3-header-actions">
        <button type="button" className="landing-v3-language" aria-label={t.common.nav.changeLanguage}>
          <GlobeIcon /> KA <ChevronDownIcon />
        </button>
        <Link className="landing-v3-dashboard-link" to="/dashboard" aria-label={t.common.nav.openDashboard}>
          <DashboardIcon />
          <span>{t.common.nav.mySpace}</span>
        </Link>
        <Link className="landing-v3-header-cta" to="/create" hash="profile">
          {t.common.nav.createBook}
          <ArrowIcon />
        </Link>
      </div>
    </header>
  );
}
