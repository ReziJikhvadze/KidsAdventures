import { useState } from "react";
import { LogOut, Sparkles, User } from "lucide-react";

import { useAuth } from "@/lib/auth/AuthContext";
import { AuthDialog } from "@/components/auth/AuthDialog";

export function Nav() {
  const { user, isAuthenticated, logout } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);

  const links = [
    { label: "How it works", href: "#how" },
    { label: "Themes", href: "#themes" },
    { label: "For grandparents", href: "#grandparents" },
    { label: "Pricing", href: "#pricing" },
    { label: "FAQ", href: "#faq" },
  ];

  return (
    <>
      <header className="sticky top-0 z-50 backdrop-blur-md bg-background/70 border-b border-border/60">
        <div className="mx-auto max-w-7xl px-6 h-16 flex items-center justify-between">
          <a
            href="#"
            className="flex items-center gap-2 font-display text-lg font-bold tracking-tight"
          >
            <span className="inline-flex items-center justify-center h-8 w-8 rounded-xl bg-primary text-primary-foreground">
              <Sparkles className="h-4 w-4" />
            </span>
            AdventurePacks
          </a>
          <nav className="hidden md:flex items-center gap-8 text-sm text-muted-foreground">
            {links.map((l) => (
              <a key={l.href} href={l.href} className="hover:text-foreground transition-colors">
                {l.label}
              </a>
            ))}
          </nav>
          <div className="flex items-center gap-2">
            {isAuthenticated && user ? (
              <>
                <span className="hidden sm:inline-flex items-center gap-1.5 text-xs text-muted-foreground max-w-[140px] truncate">
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
              href="#generator"
              className="inline-flex items-center rounded-full bg-primary text-primary-foreground px-4 py-2 text-sm font-semibold hover:opacity-90 transition"
            >
              Create pack
            </a>
          </div>
        </div>
      </header>
      <AuthDialog open={authOpen} onOpenChange={setAuthOpen} />
    </>
  );
}
