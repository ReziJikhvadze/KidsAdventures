import { Link } from "@tanstack/react-router";
import { ArrowLeft, ChevronDown, ChevronRight, Globe, LogOut } from "lucide-react";

import { LanguageSwitcher } from "@/components/adventrya/LanguageSwitcher";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";

export interface AppHeaderProps {
  /** Where the circular back button points. */
  backHref?: string;
  /** When set, the centre slot shows a step progress bar instead of the nav links. */
  progressLabel?: string;
  progressValue?: number;
  /** Switches the header to its dark, child-facing variant. */
  worldMode?: boolean;
  /** Strip account and brand controls from a child-facing, immersive step. */
  minimal?: boolean;
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
  minimal = false,
}: AppHeaderProps) {
  const t = useT();
  const { isAuthenticated, logout, user } = useAuth();
  const back = splitHref(backHref);

  /*
    This pill says "Parent Dashboard" and opens the parent's account, so it wears the parent's
    initial. It wore the child's — and on the world picker, where no child has been entered
    yet, that was the first letter of the placeholder name "პატარა გმირი": a "პ" that belonged
    to nobody. Falls back to the brand letter while signed out, when there is no one to name.
  */
  const parentInitial =
    (user?.displayName?.trim() || user?.email?.trim() || "").charAt(0).toUpperCase() || "A";

  return (
    <header
      className={`app-header ${worldMode ? "app-header-world" : ""}${minimal ? " app-header-minimal" : ""}`}
    >
      <div className="app-header-start">
        <Link
          className="back-button"
          to={back.to}
          hash={back.hash}
          aria-label={t.common.actions.backLink}
        >
          {/* The arrow alone. The word beside it repeated what the arrow already said, in the
              one place on every page where horizontal room is scarcest; the label lives on
              aria-label, where it is read to the people who cannot see the arrow. */}
          <ArrowLeft aria-hidden="true" />
        </Link>
        {!minimal ? (
          <Link className="wordmark wordmark-small" to="/">
            ADVENTRYA
          </Link>
        ) : null}
      </div>

      {/*
        The middle slot is the step counter or nothing.

        It used to fall back to a three-link nav — home, themes, the child's world — on every
        screen without a counter: the dashboard, the reader, the child's world, the legal pages
        and a shared book. On a phone it became a third row that scrolled sideways, and it
        duplicated navigation the back button and the dashboard pill already provide.
      */}
      {progressLabel ? (
        <div className="step-progress" aria-label={progressLabel}>
          <span>{progressLabel}</span>
          <div>
            <i style={{ width: `${progressValue}%` }} />
          </div>
        </div>
      ) : null}

      <div className="app-header-end">
        {/* "KA"/"EN", as in the marketing header. The full name is wide enough to sit on top of
            the step counter on a phone, and the code is the part a parent reads anyway. */}
        <LanguageSwitcher
          className="header-pill"
          globe={<Globe />}
          chevron={<ChevronDown />}
          labelStyle="short"
        />
        {!minimal ? (
          /*
            Straight to their space, signed in or not: the dashboard opens the sign-in itself now
            rather than showing a sample household first, so there is no longer a detour to route
            around.
          */
          <Link className="child-pill" to="/dashboard" aria-label={t.common.nav.openDashboard}>
            <span className="child-avatar" aria-hidden="true">
              {parentInitial}
            </span>
            <span>
              <small>Parent Dashboard</small>
              {t.common.nav.myFamily}
            </span>
            <ChevronRight />
          </Link>
        ) : null}

        {/*
          Sign out was only ever in the marketing header, so once a parent was inside
          the app there was no way out of the session at all. That matters most on a
          shared device, which is exactly where these screens get used.
        */}
        {isAuthenticated && !minimal ? (
          <button
            type="button"
            className="icon-button"
            onClick={logout}
            title={t.common.actions.signOut}
            aria-label={t.common.actions.signOut}
          >
            <LogOut />
          </button>
        ) : null}
      </div>
    </header>
  );
}
