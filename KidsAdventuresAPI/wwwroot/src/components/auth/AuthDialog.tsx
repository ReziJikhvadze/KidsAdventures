import { useEffect, useState } from "react";
import { Link } from "@tanstack/react-router";
import { Loader2 } from "lucide-react";
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
import { getAuthConfig } from "@/lib/api/auth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type AuthDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess?: () => void;
  /** @deprecated kept for call-site compatibility; the dialog is now a single sign-in-or-create flow. */
  defaultMode?: "login" | "register";
};

export function AuthDialog({ open, onOpenChange, onSuccess }: AuthDialogProps) {
  const { loginWithGoogle, continueWith } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [acceptedLegal, setAcceptedLegal] = useState(false);
  const [busy, setBusy] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [recaptcha, setRecaptcha] = useState<{ enabled: boolean; siteKey: string | null }>({
    enabled: false,
    siteKey: null,
  });
  const [recaptchaToken, setRecaptchaToken] = useState<string | null>(null);

  const recaptchaActive = recaptcha.enabled && !!recaptcha.siteKey;
  const googleDisabled = busy || googleBusy || !acceptedLegal;
  const emailSubmitDisabled = googleDisabled || (recaptchaActive && !recaptchaToken);

  useEffect(() => {
    if (open) {
      setAcceptedLegal(false);
      setRecaptchaToken(null);
      void getAuthConfig()
        .then((config) =>
          setRecaptcha({
            enabled: !!config.recaptchaEnabled,
            siteKey: config.recaptchaSiteKey ?? null,
          }),
        )
        .catch(() => setRecaptcha({ enabled: false, siteKey: null }));
    }
  }, [open]);

  const finish = (description: string) => {
    notify.success("You're signed in!", { description });
    onOpenChange(false);
    onSuccess?.();
  };

  const handleGoogleSuccess = async (idToken: string) => {
    setGoogleBusy(true);
    try {
      await loginWithGoogle(idToken);
      finish("You're ready to create a story.");
    } catch (err) {
      notify.fromError(err, "Google sign-in failed. Try again.");
    } finally {
      setGoogleBusy(false);
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await continueWith(email.trim(), password, recaptchaToken ?? undefined);
      finish("You're ready to create a story.");
    } catch (err) {
      notify.fromError(err, "Something went wrong. Try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="font-display">Sign in or create your account</DialogTitle>
          <DialogDescription>
            One step — enter your email and password and we'll sign you in, or create your account
            automatically. No confirmation email. Writing your story is free.
          </DialogDescription>
        </DialogHeader>

        <label className="flex items-start gap-3 rounded-xl border border-border bg-secondary/30 p-3 text-xs text-muted-foreground cursor-pointer">
          <input
            type="checkbox"
            checked={acceptedLegal}
            onChange={(e) => setAcceptedLegal(e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>
            I am a parent or guardian and agree to the{" "}
            <Link to="/terms" className="text-primary font-semibold hover:underline" onClick={() => onOpenChange(false)}>
              Terms & Conditions
            </Link>{" "}
            and{" "}
            <Link to="/privacy" className="text-primary font-semibold hover:underline" onClick={() => onOpenChange(false)}>
              Privacy Policy
            </Link>
            . I confirm I have authority to provide any child information or photos I upload.
          </span>
        </label>

        {googleBusy ? (
          <GoogleSignInBusyButton />
        ) : (
          <GoogleSignInButton
            disabled={googleDisabled}
            onSuccess={(idToken) => void handleGoogleSuccess(idToken)}
            onError={() => notify.error("Google sign-in was cancelled or failed.")}
          />
        )}

        <div className="relative">
          <div className="absolute inset-0 flex items-center">
            <span className="w-full border-t border-border" />
          </div>
          <div className="relative flex justify-center text-xs uppercase">
            <span className="bg-background px-2 text-muted-foreground">or</span>
          </div>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="auth-email">Email</Label>
            <Input
              id="auth-email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="auth-password">Password</Label>
            <Input
              id="auth-password"
              type="password"
              autoComplete="current-password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              New here? Use at least 8 characters with upper, lower, and a number.
            </p>
          </div>

          {recaptchaActive && recaptcha.siteKey && (
            <RecaptchaWidget siteKey={recaptcha.siteKey} onToken={setRecaptchaToken} />
          )}

          <Button type="submit" className="w-full" disabled={emailSubmitDisabled}>
            {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Continue
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
