import { useCallback, useEffect, useRef, useState } from "react";
import { Gift, Loader2, Mail, Sparkles } from "lucide-react";

import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { captureLead } from "@/lib/api/leads";
import { useAuth } from "@/lib/auth/AuthContext";
import { notify } from "@/lib/ui/notify";

const SESSION_KEY = "exitIntentShown";
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type ExitIntentDialogProps = {
  /** Passed to the lead record so we know where the email came from. */
  source?: string;
};

/**
 * A gentle "wait — your first book is free" offer shown once per session when an unauthenticated
 * visitor moves to leave (desktop cursor exits the top of the viewport). Captures an email so we can
 * email them the free-first-book link. Never shown to signed-in users.
 */
export function ExitIntentDialog({ source = "exit-intent" }: ExitIntentDialogProps) {
  const { isAuthenticated, isLoading } = useAuth();
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const armedRef = useRef(false);

  const alreadyShown = useCallback(() => {
    try {
      return sessionStorage.getItem(SESSION_KEY) === "1";
    } catch {
      return armedRef.current;
    }
  }, []);

  const markShown = useCallback(() => {
    armedRef.current = true;
    try {
      sessionStorage.setItem(SESSION_KEY, "1");
    } catch {
      /* sessionStorage unavailable (private mode) — the in-memory ref still guards this session. */
    }
  }, []);

  useEffect(() => {
    if (isLoading || isAuthenticated) return;
    if (alreadyShown()) return;

    const handleMouseOut = (event: MouseEvent) => {
      // Fire only when the cursor actually leaves the top edge of the window.
      if (event.clientY > 0 || event.relatedTarget) return;
      if (alreadyShown()) return;
      markShown();
      setOpen(true);
    };

    // Give the visitor a moment before arming, so it doesn't trigger on an accidental early flick.
    const armTimer = window.setTimeout(() => {
      document.addEventListener("mouseout", handleMouseOut);
    }, 4000);

    return () => {
      window.clearTimeout(armTimer);
      document.removeEventListener("mouseout", handleMouseOut);
    };
  }, [isAuthenticated, isLoading, alreadyShown, markShown]);

  const emailValid = EMAIL_RE.test(email.trim());

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!emailValid || busy) return;
    setBusy(true);
    try {
      await captureLead({ email: email.trim(), source });
      setDone(true);
    } catch (error) {
      notify.fromError(error, "Could not save your email. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent className="max-w-md rounded-3xl">
        <DialogHeader>
          <div className="mx-auto mb-2 flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Gift className="h-7 w-7" />
          </div>
          <DialogTitle className="text-center font-display text-2xl">
            {done ? "Check your inbox!" : "Wait — the first book is on us"}
          </DialogTitle>
          <DialogDescription className="text-center">
            {done
              ? "We've sent your free storybook link. Open it whenever you're ready to create the first adventure."
              : "Leave your email and we'll send you a link to create your child's first fully illustrated storybook — completely free, no card needed."}
          </DialogDescription>
        </DialogHeader>

        {!done && (
          <form onSubmit={handleSubmit} className="mt-2 space-y-3">
            <div className="space-y-1.5">
              <Label htmlFor="exit-intent-email">Email</Label>
              <div className="relative">
                <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  id="exit-intent-email"
                  type="email"
                  autoComplete="email"
                  placeholder="you@example.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="pl-9"
                  required
                />
              </div>
            </div>
            <Button type="submit" className="w-full" disabled={!emailValid || busy}>
              {busy ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Sending…
                </>
              ) : (
                <>
                  <Sparkles className="h-4 w-4" />
                  Send my free storybook link
                </>
              )}
            </Button>
            <p className="text-center text-xs text-muted-foreground">
              No spam — just your free book link. Unsubscribe anytime.
            </p>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
