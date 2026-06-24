import { useEffect, useState } from "react";
import { Link } from "@tanstack/react-router";
import { ArrowLeft, Loader2, Mail, ShieldCheck, Sparkles, Wand2 } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { notify } from "@/lib/ui/notify";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { GoogleSignInButton, GoogleSignInBusyButton } from "@/components/auth/GoogleSignInButton";
import { RecaptchaWidget } from "@/components/auth/RecaptchaWidget";
import { getAuthConfig, getEmailStatus, hasUsedGuestPreview } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type AuthDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess?: () => void;
  /** @deprecated kept for call-site compatibility; the dialog is now a single create-or-sign-in flow. */
  defaultMode?: "login" | "register";
};

type Step = "choose" | "email" | "password";
type Mode = "signup" | "signin" | "unknown";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function AuthDialog({ open, onOpenChange, onSuccess }: AuthDialogProps) {
  const { loginWithGoogle, continueWith } = useAuth();

  const [step, setStep] = useState<Step>("choose");
  const [mode, setMode] = useState<Mode>("unknown");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [checking, setChecking] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [recaptcha, setRecaptcha] = useState<{ enabled: boolean; siteKey: string | null }>({
    enabled: false,
    siteKey: null,
  });
  const [recaptchaToken, setRecaptchaToken] = useState<string | null>(null);

  const recaptchaActive = recaptcha.enabled && !!recaptcha.siteKey;
  const emailValid = EMAIL_RE.test(email.trim());

  useEffect(() => {
    if (!open) return;
    // Fresh start each time the dialog opens.
    setStep("choose");
    setMode("unknown");
    setPassword("");
    setRecaptchaToken(null);
    void getAuthConfig()
      .then((config) =>
        setRecaptcha({
          enabled: !!config.recaptchaEnabled,
          siteKey: config.recaptchaSiteKey ?? null,
        }),
      )
      .catch(() => setRecaptcha({ enabled: false, siteKey: null }));
  }, [open]);

  const finish = (description: string) => {
    notify.success("You're in!", { description });
    onOpenChange(false);
    onSuccess?.();
  };

  const handleGoogleSuccess = async (idToken: string) => {
    setGoogleBusy(true);
    try {
      await loginWithGoogle(idToken);
      finish("Let's create your child's book.");
    } catch (err) {
      notify.fromError(err, "Google sign-in failed. Try again.");
    } finally {
      setGoogleBusy(false);
    }
  };

  const goToPasswordStep = async () => {
    if (!emailValid || checking) return;
    setChecking(true);
    try {
      const status = await getEmailStatus(email.trim());
      if (status.exists && status.isGoogleAccount) {
        notify.info("This email uses Google", {
          description: "Tap “Continue with Google” to sign in.",
        });
        setStep("choose");
        return;
      }
      setMode(status.exists ? "signin" : "signup");
    } catch {
      // If the check fails we still let them through; the backend handles new-vs-existing safely.
      setMode("unknown");
    } finally {
      setChecking(false);
    }
    setPassword("");
    setRecaptchaToken(null);
    setStep("password");
  };

  const submitPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!password || busy) return;
    setBusy(true);
    try {
      await continueWith(email.trim(), password, recaptchaToken ?? undefined);
      finish(mode === "signin" ? "Welcome back." : "Your account is ready.");
    } catch (err) {
      // A mismatched password on a known account is the most common case — keep it friendly.
      if (err instanceof ApiError && mode === "unknown") {
        setMode("signin");
      }
      notify.fromError(err, "Something went wrong. Try again.");
    } finally {
      setBusy(false);
    }
  };

  const dialogTitle =
    step === "password" && mode === "signin"
      ? "Welcome back"
      : step === "email"
        ? "Continue with email"
        : "Create My Child's Book";

  const dialogSubtitle =
    step === "password"
      ? mode === "signin"
        ? `Enter your password to sign in as ${email.trim()}.`
        : mode === "signup"
          ? "You're one tap away. Set a password to save your child's stories."
          : "Enter your password — we'll sign you in or create your account automatically."
      : step === "email"
        ? "New or returning — one flow. No separate sign-up step and no confirmation email."
        : "Personalized, beautifully illustrated storybooks where your child is the hero. Free to start.";

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md overflow-hidden p-0 gap-0">
        {/* Warm, magical header band */}
        <div className="relative bg-gradient-to-br from-primary/15 via-primary/5 to-transparent px-6 pt-7 pb-5">
          <div className="absolute inset-0 bg-hero-glow opacity-50 pointer-events-none" />
          <div className="relative flex items-center gap-2 text-primary">
            <span className="grid h-9 w-9 place-items-center rounded-2xl bg-primary text-primary-foreground shadow-card">
              <Wand2 className="h-5 w-5" />
            </span>
            <span className="text-sm font-semibold tracking-wide">Adventrya</span>
          </div>
          <DialogHeader className="relative mt-4 space-y-1.5 text-left">
            <DialogTitle className="font-display text-2xl font-bold text-balance">
              {step === "password" && mode === "signin" && (
                <span className="mr-1">👋</span>
              )}
              {dialogTitle}
            </DialogTitle>
            <DialogDescription className="text-sm text-muted-foreground">
              {dialogSubtitle}
            </DialogDescription>
          </DialogHeader>
        </div>

        <div className="px-6 pb-6 pt-5">
          {/* STEP 1 — choose a method */}
          {step === "choose" && (
            <div className="space-y-3 animate-rise">
              {googleBusy ? (
                <GoogleSignInBusyButton />
              ) : (
                <GoogleSignInButton
                  disabled={googleBusy}
                  onSuccess={(idToken) => void handleGoogleSuccess(idToken)}
                  onError={() => notify.error("Google sign-in was cancelled or failed.")}
                />
              )}

              <Button
                type="button"
                variant="outline"
                className="w-full h-11 justify-center gap-2 font-semibold"
                onClick={() => setStep("email")}
              >
                <Mail className="h-4 w-4" />
                Continue with Email
              </Button>

              <p className="pt-1 text-center text-xs text-muted-foreground">
                One account flow for everyone — we create your account or sign you in automatically.
              </p>

              <div className="mt-3 flex items-center justify-center gap-1.5 text-[11px] text-muted-foreground">
                <ShieldCheck className="h-3.5 w-3.5 text-primary/70" />
                Free to start · No spam · Your photos stay private
              </div>
            </div>
          )}

          {/* STEP 2 — email (single input) */}
          {step === "email" && (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void goToPasswordStep();
              }}
              className="space-y-4 animate-rise"
            >
              <button
                type="button"
                onClick={() => setStep("choose")}
                className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground"
              >
                <ArrowLeft className="h-3.5 w-3.5" />
                Back
              </button>

              <div className="space-y-2">
                <Label htmlFor="auth-email">Your email</Label>
                <Input
                  id="auth-email"
                  type="email"
                  inputMode="email"
                  autoComplete="email"
                  autoFocus
                  required
                  placeholder="you@example.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="h-11"
                />
                <p className="text-xs text-muted-foreground">
                  We'll recognize your email and sign you in, or create a new account if you're new.
                </p>
              </div>

              <Button
                type="submit"
                className="w-full h-11 font-semibold"
                disabled={!emailValid || checking}
              >
                {checking && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                Continue
              </Button>
            </form>
          )}

          {/* STEP 3 — password (contextual) */}
          {step === "password" && (
            <form onSubmit={submitPassword} className="space-y-4 animate-rise">
              <button
                type="button"
                onClick={() => setStep("email")}
                className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground"
              >
                <ArrowLeft className="h-3.5 w-3.5" />
                {email.trim()}
              </button>

              {mode === "signup" && (
                <div className="flex items-start gap-2 rounded-xl border border-primary/20 bg-primary/5 p-3 text-xs text-foreground">
                  <Sparkles className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                  <span>
                    {hasUsedGuestPreview()
                      ? "Continue your story — your first illustrated page is on us. Unlock the full book for $4.99."
                      : "Your first story includes 1 beautifully illustrated free page."}
                  </span>
                </div>
              )}

              <div className="space-y-2">
                <Label htmlFor="auth-password">
                  {mode === "signin" ? "Password" : "Choose a password"}
                </Label>
                <Input
                  id="auth-password"
                  type="password"
                  autoComplete={mode === "signin" ? "current-password" : "new-password"}
                  autoFocus
                  required
                  minLength={8}
                  placeholder={mode === "signin" ? "Your password" : "At least 8 characters"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="h-11"
                />
                {mode !== "signin" && (
                  <p className="text-xs text-muted-foreground">
                    Use 8+ characters with an upper, a lower, and a number.
                  </p>
                )}
              </div>

              {/* reCAPTCHA only matters when creating a brand-new account. */}
              {recaptchaActive && recaptcha.siteKey && mode !== "signin" && (
                <RecaptchaWidget siteKey={recaptcha.siteKey} onToken={setRecaptchaToken} />
              )}

              <Button type="submit" className="w-full h-11 font-semibold" disabled={busy}>
                {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                {mode === "signin" ? "Sign in" : mode === "signup" ? "Create account" : "Continue"}
              </Button>
            </form>
          )}

          <p className="mt-5 text-center text-[11px] leading-relaxed text-muted-foreground">
            By continuing you agree to our{" "}
            <Link
              to="/terms"
              className="font-semibold text-primary hover:underline"
              onClick={() => onOpenChange(false)}
            >
              Terms
            </Link>{" "}
            and{" "}
            <Link
              to="/privacy"
              className="font-semibold text-primary hover:underline"
              onClick={() => onOpenChange(false)}
            >
              Privacy Policy
            </Link>
            .
          </p>
        </div>
      </DialogContent>
    </Dialog>
  );
}
