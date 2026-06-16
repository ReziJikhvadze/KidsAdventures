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
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type AuthDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess?: () => void;
  defaultMode?: "login" | "register";
};

export function AuthDialog({
  open,
  onOpenChange,
  onSuccess,
  defaultMode = "login",
}: AuthDialogProps) {
  const { login, loginWithGoogle, register } = useAuth();
  const [mode, setMode] = useState<"login" | "register">(defaultMode);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [acceptedLegal, setAcceptedLegal] = useState(false);
  const [busy, setBusy] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);

  const googleDisabled = busy || googleBusy || (mode === "register" && !acceptedLegal);

  useEffect(() => {
    if (open) {
      setMode(defaultMode);
      setAcceptedLegal(false);
    }
  }, [open, defaultMode]);

  const finishSignIn = () => {
    notify.success(mode === "login" ? "Welcome back!" : "Account created!", {
      description:
        mode === "login"
          ? "You're signed in and ready to create a story."
          : "You're signed in with Google and ready to create a story.",
    });
    onOpenChange(false);
    onSuccess?.();
  };

  const handleGoogleSuccess = async (idToken: string) => {
    setGoogleBusy(true);
    try {
      await loginWithGoogle(idToken);
      finishSignIn();
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
      if (mode === "login") {
        await login(email.trim(), password);
        notify.success("Welcome back!", {
          description: "You're signed in and ready to create a story.",
        });
        onOpenChange(false);
        onSuccess?.();
      } else {
        const message = await register(email.trim(), password);
        notify.success("Check your inbox", {
          description: message,
        });
        setMode("login");
      }
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
          <DialogTitle className="font-display">
            {mode === "login" ? "Sign in" : "Create your account"}
          </DialogTitle>
          <DialogDescription>
            {mode === "login"
              ? "Sign in to create personalized storybooks for your child."
              : "You get one free 2-page welcome story. Full 6-page books use book credits. Google sign-in is instant; email sign-up sends a confirmation link."}
          </DialogDescription>
        </DialogHeader>

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
              autoComplete={mode === "login" ? "current-password" : "new-password"}
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {mode === "register" && (
              <p className="text-xs text-muted-foreground">
                At least 8 characters with upper, lower, and a number.
              </p>
            )}
          </div>

          {mode === "register" && (
            <label className="flex items-start gap-3 rounded-xl border border-border bg-secondary/30 p-3 text-xs text-muted-foreground cursor-pointer">
              <input
                type="checkbox"
                checked={acceptedLegal}
                onChange={(e) => setAcceptedLegal(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
                required
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
          )}

          <Button type="submit" className="w-full" disabled={googleDisabled}>
            {busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {mode === "login" ? "Sign in" : "Create account"}
          </Button>
        </form>

        <p className="text-center text-sm text-muted-foreground">
          {mode === "login" ? (
            <>
              New here?{" "}
              <button
                type="button"
                className="text-primary font-semibold hover:underline"
                onClick={() => setMode("register")}
              >
                Create an account
              </button>
            </>
          ) : (
            <>
              Already have an account?{" "}
              <button
                type="button"
                className="text-primary font-semibold hover:underline"
                onClick={() => setMode("login")}
              >
                Sign in
              </button>
            </>
          )}
        </p>
      </DialogContent>
    </Dialog>
  );
}
