import { Link } from "@tanstack/react-router";
import { ArrowLeft, ChevronDown, ChevronRight, Globe } from "lucide-react";

import { LanguageSwitcher } from "@/components/adventrya/LanguageSwitcher";
import { useT } from "@/lib/i18n";

export interface AppHeaderProps {
  /** Where the circular back button points. */
  backHref?: string;
  /** When set, the centre slot shows a step progress bar instead of the nav links. */
  progressLabel?: string;
  progressValue?: number;
  /** Highlights the "child world" nav link and switches the header to its dark variant. */
  worldMode?: boolean;
  activeLink?: "home" | "themes" | "world";
  /** Name of the child whose space the header links into, if any. */
  childName?: string;
}

function splitHref(href: string): { to: string; hash?: string } {
  const hashIndex = href.indexOf("#");
  if (hashIndex < 0) return { to: href };
  return {
    to: href.slice(0, hashIndex) || "/",
    hash: href.slice(hashIndex + 1),
  };
}

export function AppHeader({
  backHref = "/",
  progressLabel,
  progressValue = 0,
  worldMode = false,
  activeLink,
  childName,
}: AppHeaderProps) {
  const t = useT();
  const back = splitHref(backHref);

  return (
    <header className={`app-header ${worldMode ? "app-header-world" : ""}`}>
      <div className="app-header-start">
        {/*
          Labelled rather than a bare arrow: on the create journey this is the only way
          back, and an unlabelled icon in a header full of other controls is easy to miss.
          The label collapses on narrow screens, where the arrow alone is unambiguous.
        */}
        <Link
          className="back-button"
          to={back.to}
          hash={back.hash}
          aria-label={t.common.actions.backLink}
        >
          <ArrowLeft aria-hidden="true" />
          <span>{t.common.actions.backLink}</span>
        </Link>
        <Link className="wordmark wordmark-small" to="/">
          ADVENTRYA
        </Link>
      </div>

      {progressLabel ? (
        <div className="step-progress" aria-label={progressLabel}>
          <span>{progressLabel}</span>
          <div>
            <i style={{ width: `${progressValue}%` }} />
          </div>
        </div>
      ) : (
        <nav className="frame-links" aria-label={t.common.nav.screenNav}>
          <Link className={activeLink === "home" ? "active" : ""} to="/">
            {t.common.nav.home}
          </Link>
          <Link className={activeLink === "themes" ? "active" : ""} to="/themes">
            {t.common.nav.themes}
          </Link>
          <Link className={activeLink === "world" ? "active" : ""} to="/world">
            {t.common.nav.childWorld}
          </Link>
        </nav>
      )}

      <div className="app-header-end">
        <LanguageSwitcher
          className="header-pill"
          globe={<Globe />}
          chevron={<ChevronDown />}
        />
        <Link className="child-pill" to="/dashboard" aria-label={t.common.nav.openDashboard}>
          <span className="child-avatar" aria-hidden="true">
            {childName?.trim().charAt(0) ?? "A"}
          </span>
          <span>
            <small>Parent Dashboard</small>
            {t.common.nav.myFamily}
          </span>
          <ChevronRight />
        </Link>
      </div>
    </header>
  );
}
