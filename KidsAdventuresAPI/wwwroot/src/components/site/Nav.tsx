import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { Library, LogOut, Menu, Sparkles, User } from "lucide-react";

import { BrandLogo } from "@/components/brand/BrandLogo";
import { useAuth } from "@/lib/auth/AuthContext";
import { formatNavQuotaTitle, formatCreditsBadgeLabel } from "@/lib/account/storyQuota";
import { AuthDialog } from "@/components/auth/AuthDialog";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";

const anchorLinks: { label: string; href: string; isRoute?: boolean }[] = [
  { label: "How it works", href: "/#how" },
  { label: "Themes", href: "/#themes" },
  { label: "Gift guide", href: "/gift-guide", isRoute: true },
  { label: "Blog", href: "/blog", isRoute: true },
  { label: "Pricing", href: "/#pricing" },
  { label: "FAQ", href: "/#faq" },
];

function emailLocalPart(email: string): string {
  const at = email.indexOf("@");
  return at === -1 ? email : email.slice(0, at);
}

function NavLinkItem({
  link,
  className,
  onClick,
}: {
  link: (typeof anchorLinks)[number];
  className: string;
  onClick?: () => void;
}) {
  if (link.isRoute) {
    return (
      <Link to={link.href} className={className} onClick={onClick}>
        {link.label}
      </Link>
    );
  }
  return (
    <a href={link.href} className={className} onClick={onClick}>
      {link.label}
    </a>
  );
}

export function Nav() {
  const { user, isAuthenticated, isLoading, logout } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const quotaInput = {
    bookCredits: user?.bookCredits ?? 0,
    storiesRemainingThisMonth: user?.storiesRemainingThisMonth ?? 0,
    welcomeStoryRemaining: user?.welcomeStoryRemaining ?? 0,
    isLoading,
  };

  const creditTitle = formatNavQuotaTitle({
    ...quotaInput,
    storiesAllowedThisMonth: user?.storiesAllowedThisMonth,
  });

  const creditBadge = formatCreditsBadgeLabel(quotaInput);

  return (
    <>
      <header className="sticky top-0 z-50 overflow-hidden 2xl:overflow-visible backdrop-blur-md bg-background/80 border-b border-border/60">
        <div className="mx-auto grid h-16 max-w-7xl grid-cols-[minmax(0,auto)_minmax(0,1fr)_minmax(0,auto)] items-center gap-x-2 px-3 sm:gap-x-4 sm:px-6 xl:gap-x-6">
          <BrandLogo variant="header" />

          <nav
            aria-label="Main"
            className="hidden xl:flex items-center justify-center gap-x-5 2xl:gap-x-7 min-w-0 overflow-hidden text-sm text-muted-foreground px-1"
          >
            {anchorLinks.map((link) => (
              <NavLinkItem
                key={link.href}
                link={link}
                className="whitespace-nowrap shrink-0 hover:text-foreground transition-colors"
              />
            ))}
          </nav>

          <div className="flex items-center justify-end gap-1.5 sm:gap-2 shrink-0">
            {isAuthenticated && (
              <Link
                to="/my-packs"
                title={creditTitle}
                className="group inline-flex h-9 items-center gap-1 rounded-full border border-primary/25 bg-primary/8 pl-1 pr-2 shadow-sm hover:bg-primary/12 hover:border-primary/35 transition"
              >
                <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-primary/15 ring-1 ring-primary/20 group-hover:bg-primary/20 transition">
                  <Library className="h-3.5 w-3.5" />
                </span>
                <span className="hidden 2xl:inline text-xs font-semibold text-primary whitespace-nowrap">
                  My Books
                </span>
                <span className="inline-flex items-center gap-0.5 text-[10px] font-bold text-amber-800 whitespace-nowrap">
                  <Sparkles className="h-3 w-3 shrink-0" />
                  {creditBadge}
                </span>
              </Link>
            )}

            {isAuthenticated && user ? (
              <>
                <span
                  className="hidden xl:inline-flex items-center gap-1.5 text-xs text-muted-foreground max-w-[8rem] truncate"
                  title={user.email}
                >
                  <User className="h-3.5 w-3.5 shrink-0" />
                  {emailLocalPart(user.email)}
                </span>
                <button
                  type="button"
                  onClick={logout}
                  className="inline-flex h-9 w-9 sm:w-auto sm:px-3 items-center justify-center gap-1 rounded-full border border-border text-sm font-medium hover:bg-secondary transition"
                  title="Sign out"
                >
                  <LogOut className="h-4 w-4" />
                  <span className="hidden lg:inline">Sign out</span>
                </button>
              </>
            ) : (
              <button
                type="button"
                onClick={() => setAuthOpen(true)}
                className="inline-flex h-9 items-center rounded-full border border-border px-3 sm:px-4 text-xs sm:text-sm font-semibold hover:bg-secondary transition whitespace-nowrap"
              >
                Sign in
              </button>
            )}

            <a
              href="/#generator"
              className="inline-flex h-9 items-center rounded-full bg-primary text-primary-foreground px-3 sm:px-4 text-xs sm:text-sm font-semibold hover:opacity-90 transition whitespace-nowrap"
            >
              <span className="sm:hidden">Create</span>
              <span className="hidden sm:inline">Create book</span>
            </a>

            <Sheet open={mobileMenuOpen} onOpenChange={setMobileMenuOpen}>
              <SheetTrigger asChild>
                <button
                  type="button"
                  className="xl:hidden inline-flex h-9 w-9 items-center justify-center rounded-full border border-border hover:bg-secondary transition"
                  aria-label="Open menu"
                >
                  <Menu className="h-5 w-5" />
                </button>
              </SheetTrigger>
              <SheetContent side="right" className="w-[min(100vw-2rem,20rem)]">
                <SheetHeader>
                  <SheetTitle className="text-left font-display">Menu</SheetTitle>
                </SheetHeader>
                <nav className="mt-8 flex flex-col gap-5">
                  {anchorLinks.map((link) => (
                    <NavLinkItem
                      key={link.href}
                      link={link}
                      onClick={() => setMobileMenuOpen(false)}
                      className="text-base font-medium text-foreground hover:text-primary transition-colors"
                    />
                  ))}
                </nav>
              </SheetContent>
            </Sheet>
          </div>
        </div>
      </header>

      <AuthDialog open={authOpen} onOpenChange={setAuthOpen} defaultMode="login" />
    </>
  );
}
