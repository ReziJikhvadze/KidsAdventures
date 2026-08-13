import { Link } from "@tanstack/react-router";

import { LanguageSwitcher } from "@/components/adventrya/LanguageSwitcher";
import { useT } from "@/lib/i18n";

import { ArrowIcon, ChevronDownIcon, DashboardIcon, GlobeIcon } from "./icons";

export function Header() {
  const t = useT();
  return (
    <header className="landing-v3-header">
      <Link className="landing-v3-logo" to="/" aria-label={t.common.nav.homeAria}>
        ADVENTRYA
        <small>{t.common.brandTagline}</small>
      </Link>

      <nav className="landing-v3-nav" aria-label={t.common.nav.primaryNav}>
        <a href="#books">{t.common.nav.books}</a>
        <a href="#pricing">{t.common.nav.pricing}</a>
        <a href="#faq">{t.common.nav.faq}</a>
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
        <Link className="landing-v3-header-cta" to="/themes">
          {t.common.nav.createBook}
          <ArrowIcon />
        </Link>
      </div>
    </header>
  );
}
