import { Link, useCanGoBack, useRouter } from "@tanstack/react-router";
import { ArrowLeft, ChevronDown, ChevronRight, Globe, LogOut } from "lucide-react";

import { LanguageSwitcher } from "@/components/adventrya/LanguageSwitcher";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";

export interface AppHeaderProps {
  /**
   * Where the back button goes when there is nothing to go back to — a page opened from a
   * bookmark, a shared link or a fresh tab. When the parent arrived from somewhere inside the
   * app, the button returns them there instead, whatever this says.
   */
  backHref?: string;
  /** When set, the centre slot shows a step progress bar instead of the nav links. */
  progressLabel?: string;
  progressValue?: number;
  /**
   * The back target is a step inside this screen, not a page the parent came from — so it wins
   * over the browser's history. The creation journey needs this: it replaces its history entry
   * on every stage, so going back through the browser would leave the whole flow instead of
   * stepping back one question.
   */
  explicitBack?: boolean;

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
  explicitBack = false,
  worldMode = false,
  minimal = false,
}: AppHeaderProps) {
  const t = useT();
  const { isAuthenticated, logout, user } = useAuth();
  const back = splitHref(backHref);
  const router = useRouter();
  /*
    Back means back.

    Every screen named a fixed destination here, so the arrow always went to the same place
    however the parent had arrived — out of the reader to the dashboard even when they had come
    from their child's world, off the dashboard to the home page even when they had come from
    the themes. `backHref` is now the fallback for the case it was written for: a page opened
    cold, with no history of ours behind it.
  */
  const canGoBack = useCanGoBack();

  const goBack = (event: React.MouseEvent<HTMLAnchorElement>) => {
    // Leave the modified clicks alone: they open the fallback in a tab, which is still a
    // sensible destination, and they are how someone copies the link.
    if (
      !canGoBack ||
      explicitBack ||
      event.defaultPrevented ||
      event.button !== 0 ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey
    ) {
      return;
    }
    event.preventDefault();
    router.history.back();
  };

  /*
    This pill opens the parent's account, so it wears the parent's
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
          onClick={goBack}
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
              <small>{t.common.nav.parentSpace}</small>
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
