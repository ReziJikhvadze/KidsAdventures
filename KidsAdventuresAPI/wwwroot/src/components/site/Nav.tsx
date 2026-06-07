import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { Library, LogOut, Sparkles, User } from "lucide-react";

import { BrandLogo } from "@/components/brand/BrandLogo";
import { useAuth } from "@/lib/auth/AuthContext";
import { formatNavQuotaLabel, formatNavQuotaTitle } from "@/lib/account/storyQuota";
import { AuthDialog } from "@/components/auth/AuthDialog";

export function Nav() {
  const { user, isAuthenticated, isLoading, logout } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);

  const anchorLinks = [
    { label: "How it works", href: "/#how" },
    { label: "Themes", href: "/#themes" },
    { label: "Pricing", href: "/#pricing" },
    { label: "FAQ", href: "/#faq" },
  ];

  const creditLabel = formatNavQuotaLabel({
    bookCredits: user?.bookCredits ?? 0,
    storiesRemainingThisMonth: user?.storiesRemainingThisMonth ?? 0,
    welcomeStoryRemaining: user?.welcomeStoryRemaining ?? 0,
    isLoading,
  });
  const creditTitle = formatNavQuotaTitle({
    bookCredits: user?.bookCredits ?? 0,
    storiesRemainingThisMonth: user?.storiesRemainingThisMonth ?? 0,
    storiesAllowedThisMonth: user?.storiesAllowedThisMonth,
    welcomeStoryRemaining: user?.welcomeStoryRemaining ?? 0,
  });

  return (
    <>
      <header className="sticky top-0 z-50 backdrop-blur-md bg-background/70 border-b border-border/60">
        <div className="mx-auto max-w-7xl px-4 sm:px-6 min-h-16 py-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
          <BrandLogo />
          <nav className="hidden md:flex items-center gap-8 text-sm text-muted-foreground order-3 md:order-none w-full md:w-auto justify-center md:justify-start">
            {anchorLinks.map((l) => (
              <a key={l.href} href={l.href} className="hover:text-foreground transition-colors">
                {l.label}
              </a>
            ))}
          </nav>
          <div className="flex items-center gap-2 shrink-0 ml-auto">
            {isAuthenticated && (
              <Link
                to="/my-packs"
                className="group inline-flex items-center gap-2 rounded-full border border-primary/25 bg-primary/8 pl-2 pr-3 py-1.5 text-sm font-semibold text-primary shadow-sm hover:bg-primary/12 hover:border-primary/35 transition"
              >
                <span className="grid place-items-center h-8 w-8 rounded-full bg-primary/15 ring-1 ring-primary/20 group-hover:bg-primary/20 transition">
                  <Library className="h-4 w-4" />
                </span>
                <span className="flex flex-col items-start leading-tight min-w-0">
                  <span>My Books</span>
                  <span
                    className="inline-flex items-center gap-1 text-[10px] font-bold text-amber-800"
                    title={creditTitle}
                  >
                    <Sparkles className="h-3 w-3 shrink-0" />
                    {creditLabel}
                  </span>
                </span>
              </Link>
            )}
            {isAuthenticated && user ? (
              <>
                <span className="hidden xl:inline-flex items-center gap-1.5 text-xs text-muted-foreground max-w-[140px] truncate">
                  <User className="h-3.5 w-3.5 shrink-0" />
                  {user.email}
                </span>
                <button
                  type="button"
                  onClick={logout}
                  className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-2 text-sm font-medium hover:bg-secondary transition"
                  title="Sign out"
                >
                  <LogOut className="h-4 w-4" />
                  <span className="hidden sm:inline">Sign out</span>
                </button>
              </>
            ) : (
              <button
                type="button"
                onClick={() => setAuthOpen(true)}
                className="inline-flex items-center rounded-full border border-border px-4 py-2 text-sm font-semibold hover:bg-secondary transition"
              >
                Sign in
              </button>
            )}
            <a
              href="/#generator"
              className="inline-flex items-center rounded-full bg-primary text-primary-foreground px-4 py-2 text-sm font-semibold hover:opacity-90 transition"
            >
              Create book
            </a>
          </div>
        </div>
      </header>
      <AuthDialog open={authOpen} onOpenChange={setAuthOpen} defaultMode="login" />
    </>
  );
}
