import { useState } from "react";

import { Link } from "@tanstack/react-router";

import { Library, LogOut, Menu, Sparkles, User } from "lucide-react";



import { BrandLogo } from "@/components/brand/BrandLogo";

import { useAuth } from "@/lib/auth/AuthContext";

import { formatNavQuotaLabel, formatNavQuotaTitle, formatCreditsBadgeLabel } from "@/lib/account/storyQuota";

import { AuthDialog } from "@/components/auth/AuthDialog";

import {

  Sheet,

  SheetContent,

  SheetHeader,

  SheetTitle,

  SheetTrigger,

} from "@/components/ui/sheet";



export function Nav() {

  const { user, isAuthenticated, isLoading, logout } = useAuth();

  const [authOpen, setAuthOpen] = useState(false);

  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);



  const anchorLinks: { label: string; href: string; isRoute?: boolean }[] = [

    { label: "How it works", href: "/#how" },

    { label: "Themes", href: "/#themes" },

    { label: "Gift guide", href: "/gift-guide", isRoute: true },

    { label: "Blog", href: "/blog", isRoute: true },

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

      <header className="sticky top-0 z-50 overflow-hidden md:overflow-visible backdrop-blur-md bg-background/80 border-b border-border/60">

        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-2 px-3 sm:gap-3 sm:px-6">

          <BrandLogo variant="header" />



          <nav className="hidden md:flex items-center gap-8 text-sm text-muted-foreground">

            {anchorLinks.map((l) =>
              l.isRoute ? (
                <Link key={l.href} to={l.href} className="hover:text-foreground transition-colors">
                  {l.label}
                </Link>
              ) : (
                <a key={l.href} href={l.href} className="hover:text-foreground transition-colors">
                  {l.label}
                </a>
              ),
            )}

          </nav>



          <div className="relative z-10 flex items-center gap-1 sm:gap-1.5 shrink-0 min-w-0">

            {isAuthenticated && (

              <Link

                to="/my-packs"

                title={creditTitle}

                className="group inline-flex items-center gap-1.5 rounded-full border border-primary/25 bg-primary/8 shadow-sm hover:bg-primary/12 hover:border-primary/35 transition md:pl-2 md:pr-3 md:py-1.5 p-1.5 max-w-[7.5rem] sm:max-w-none"

              >

                <span className="grid place-items-center h-8 w-8 rounded-full bg-primary/15 ring-1 ring-primary/20 group-hover:bg-primary/20 transition shrink-0">

                  <Library className="h-4 w-4" />

                </span>

                <span className="hidden md:flex flex-col items-start leading-tight min-w-0">

                  <span className="text-sm font-semibold text-primary">My Books</span>

                  <span className="inline-flex items-center gap-1 text-[10px] font-bold text-amber-800">

                    <Sparkles className="h-3 w-3 shrink-0" />

                    {creditLabel}

                  </span>

                </span>

                <span className="md:hidden inline-flex items-center gap-1 text-[10px] font-bold text-amber-800 min-w-0 truncate">

                  <Sparkles className="h-3 w-3 shrink-0" />

                  <span className="truncate">{formatCreditsBadgeLabel({

                    bookCredits: user?.bookCredits ?? 0,

                    storiesRemainingThisMonth: user?.storiesRemainingThisMonth ?? 0,

                    welcomeStoryRemaining: user?.welcomeStoryRemaining ?? 0,

                    isLoading,

                  })}</span>

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

                  className="inline-flex items-center gap-1 rounded-full border border-border p-2 sm:px-3 sm:py-2 text-sm font-medium hover:bg-secondary transition"

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

                className="inline-flex items-center rounded-full border border-border px-2.5 py-1.5 sm:px-4 sm:py-2 text-xs sm:text-sm font-semibold hover:bg-secondary transition whitespace-nowrap"

              >

                Sign in

              </button>

            )}

            <a

              href="/#generator"

              className="inline-flex items-center rounded-full bg-primary text-primary-foreground px-2.5 py-1.5 text-xs sm:px-4 sm:py-2 sm:text-sm font-semibold hover:opacity-90 transition whitespace-nowrap"

            >

              <span className="sm:hidden">Create</span>

              <span className="hidden sm:inline">Create book</span>

            </a>



            <Sheet open={mobileMenuOpen} onOpenChange={setMobileMenuOpen}>

              <SheetTrigger asChild>

                <button

                  type="button"

                  className="md:hidden inline-flex items-center justify-center h-9 w-9 rounded-full border border-border hover:bg-secondary transition"

                  aria-label="Open menu"

                >

                  <Menu className="h-5 w-5" />

                </button>

              </SheetTrigger>

              <SheetContent side="right" className="w-[min(100vw-2rem,20rem)]">

                <SheetHeader>

                  <SheetTitle className="text-left font-display">Menu</SheetTitle>

                </SheetHeader>

                <nav className="mt-6 flex flex-col gap-4">

                  {anchorLinks.map((l) =>
                    l.isRoute ? (
                      <Link
                        key={l.href}
                        to={l.href}
                        onClick={() => setMobileMenuOpen(false)}
                        className="text-base font-medium text-foreground hover:text-primary transition-colors"
                      >
                        {l.label}
                      </Link>
                    ) : (
                      <a
                        key={l.href}
                        href={l.href}
                        onClick={() => setMobileMenuOpen(false)}
                        className="text-base font-medium text-foreground hover:text-primary transition-colors"
                      >
                        {l.label}
                      </a>
                    ),
                  )}

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


