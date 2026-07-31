import { ArrowRight, Mail } from "lucide-react";
import { type ReactNode, useEffect, useRef, useState } from "react";

import { GoogleSignInButton, GoogleSignInBusyButton } from "@/components/auth/GoogleSignInButton";
import * as authApi from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import type { AuthChallengeResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { formatGeorgianPhone, normalizeGeorgianPhone, t } from "@/lib/i18n";

type AuthTab = "email" | "phone";

type Props = {
  /** Where the magic link should land the parent once it is followed. */
  returnPath: string;
  onAuthenticated: () => void;
  /** Screen-specific heading above the providers; the journey and the dashboard differ. */
  header?: ReactNode;
};

/**
 * The one passwordless sign-in panel: Google, Apple (soon), email magic link, phone OTP.
 *
 * It was previously inlined in the create journey while the dashboard and blog used a separate
 * English, password-based dialog — two auth models in one product. This is the journey's panel,
 * lifted out unchanged so every entry point offers the same thing and there is only one place to
 * fix when it changes. Markup and class names are untouched so the existing journey CSS still
 * styles it wherever it is mounted.
 */
export function PasswordlessAuthPanel({ returnPath, onAuthenticated, header }: Props) {
  const { loginWithGoogle, signInWithPhoneCode } = useAuth();

  const [tab, setTab] = useState<AuthTab>("email");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [otp, setOtp] = useState(["", "", "", "", "", ""]);
  const [challenge, setChallenge] = useState<AuthChallengeResponse | null>(null);
  const [otpMode, setOtpMode] = useState(false);
  const [busy, setBusy] = useState(false);
  const [googleBusy, setGoogleBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [magicSent, setMagicSent] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const otpRefs = useRef<Array<HTMLInputElement | null>>([]);

  useEffect(() => {
    if (cooldown <= 0) return;
    const id = window.setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => window.clearTimeout(id);
  }, [cooldown]);

  const sendMagicLink = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await authApi.requestMagicLink(email.trim(), returnPath);
      setChallenge(result);
      setMagicSent(true);
      setCooldown(result.resendAfterSeconds || 30);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ბმული ვერ გაიგზავნა.");
    } finally {
      setBusy(false);
    }
  };

  const sendPhoneCode = async () => {
    const normalized = normalizeGeorgianPhone(phone);
    if (!normalized) {
      setError(t.journey.validation.phoneInvalid);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await authApi.requestPhoneCode(normalized);
      setChallenge(result);
      setOtpMode(true);
      setCooldown(result.resendAfterSeconds || 30);
      setOtp(["", "", "", "", "", ""]);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "კოდი ვერ გაიგზავნა.");
    } finally {
      setBusy(false);
    }
  };

  const verifyOtp = async (digits = otp) => {
    const code = digits.join("");
    if (code.length !== 6) {
      setError(t.journey.validation.otpInvalid);
      return;
    }
    const normalized = normalizeGeorgianPhone(phone);
    if (!normalized) {
      setError(t.journey.validation.phoneInvalid);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await signInWithPhoneCode(normalized, code);
      onAuthenticated();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t.journey.validation.otpInvalid);
    } finally {
      setBusy(false);
    }
  };

  const fillOtp = (value: string) => {
    const digits = value.replace(/\D/g, "").slice(0, 6).split("");
    const next = Array.from({ length: 6 }, (_, i) => digits[i] ?? "");
    setOtp(next);
    if (digits.length === 6) void verifyOtp(next);
  };

  const onGoogleSuccess = async (credential: { idToken?: string; accessToken?: string }) => {
    setGoogleBusy(true);
    setError(null);
    try {
      await loginWithGoogle(credential);
      onAuthenticated();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Google-ით შესვლა ვერ მოხერხდა.");
    } finally {
      setGoogleBusy(false);
    }
  };

  const magicDevUrl =
    challenge?.devUrl ||
    (challenge?.devSecret && !challenge.deliveryLive
      ? `/auth/magic?token=${encodeURIComponent(challenge.devSecret)}&next=${encodeURIComponent(returnPath)}`
      : null);

  return (
    <div className="auth-panel">
      {header}

      {!otpMode ? (
        <>
          {googleBusy ? (
            <GoogleSignInBusyButton variant="social" />
          ) : (
            <GoogleSignInButton
              variant="social"
              label={t.journey.auth.google}
              disabled={busy}
              onSuccess={(credential) => void onGoogleSuccess(credential)}
              onError={() => setError("Google-ით შესვლა ვერ მოხერხდა.")}
              onUnavailable={() => setError(t.journey.auth.googleUnavailable)}
            />
          )}
          <button
            className="social-auth"
            type="button"
            disabled={busy}
            onClick={() => setError(t.journey.auth.appleSoon)}
          >
            <span>●</span>
            {t.journey.auth.apple}
          </button>
          <div className="auth-divider">
            <span>ან</span>
          </div>

          <div className="ux-auth-switcher" role="tablist">
            <button
              type="button"
              className={tab === "email" ? "selected" : ""}
              onClick={() => setTab("email")}
            >
              {t.journey.auth.tabEmail}
            </button>
            <button
              type="button"
              className={tab === "phone" ? "selected" : ""}
              onClick={() => setTab("phone")}
            >
              {t.journey.auth.tabPhone}
            </button>
          </div>

          {tab === "email" ? (
            <>
              <label className="field auth-email" htmlFor="journey-auth-email">
                <span>{t.common.labels.email}</span>
                <div>
                  <Mail aria-hidden="true" />
                  <input
                    id="journey-auth-email"
                    name="email"
                    type="email"
                    value={email}
                    autoComplete="email"
                    aria-label={t.common.labels.email}
                    onChange={(e) => {
                      setEmail(e.target.value);
                      setMagicSent(false);
                      setChallenge(null);
                    }}
                  />
                </div>
              </label>
              {magicSent ? (
                <p className="ux-mock-note">{t.journey.auth.magicLinkSent(email)}</p>
              ) : null}
              {challenge && !challenge.deliveryLive ? (
                <p className="ux-mock-note">{t.journey.auth.devDelivery}</p>
              ) : null}
              {magicDevUrl ? (
                <p className="ux-mock-note">
                  <a href={magicDevUrl}>{t.journey.auth.openMagicLink}</a>
                </p>
              ) : null}
              <button
                className="button button-primary auth-main"
                type="button"
                disabled={busy || !email.trim() || cooldown > 0}
                onClick={() => void sendMagicLink()}
              >
                {t.journey.auth.sendMagicLink}
                {cooldown > 0 ? t.journey.auth.resendIn(cooldown) : ""}
                <ArrowRight aria-hidden="true" size={16} />
              </button>
            </>
          ) : (
            <>
              <label className="field ux-phone-field" htmlFor="journey-auth-phone">
                <span>{t.journey.auth.phoneLabel}</span>
                <div>
                  <b>+995</b>
                  <input
                    id="journey-auth-phone"
                    name="phone"
                    inputMode="numeric"
                    autoComplete="tel-national"
                    value={formatGeorgianPhone(phone)}
                    onChange={(e) => setPhone(e.target.value)}
                    placeholder="5XX XX XX XX"
                  />
                </div>
              </label>
              <p className="ux-mock-note">{t.journey.auth.phoneDemoNote}</p>
              <button
                className="button button-primary auth-main"
                type="button"
                disabled={busy || cooldown > 0}
                onClick={() => void sendPhoneCode()}
              >
                {t.journey.auth.sendCode}
                {cooldown > 0 ? t.journey.auth.resendIn(cooldown) : ""}
                <ArrowRight aria-hidden="true" size={16} />
              </button>
            </>
          )}
        </>
      ) : (
        <div className="ux-otp-panel">
          <small>{t.journey.auth.otpHeading}</small>
          <strong>+995 {formatGeorgianPhone(phone)}</strong>
          <div
            className="ux-otp-inputs"
            onPaste={(e) => {
              e.preventDefault();
              fillOtp(e.clipboardData.getData("text"));
            }}
          >
            {otp.map((digit, index) => (
              <input
                key={index}
                ref={(el) => {
                  otpRefs.current[index] = el;
                }}
                id={`journey-auth-otp-${index + 1}`}
                name={`otp-${index + 1}`}
                inputMode="numeric"
                autoComplete={index === 0 ? "one-time-code" : "off"}
                maxLength={1}
                value={digit}
                aria-label={t.journey.auth.otpDigitAria(index + 1)}
                onChange={(e) => {
                  const value = e.target.value.replace(/\D/g, "").slice(-1);
                  const next = [...otp];
                  next[index] = value;
                  setOtp(next);
                  if (value && index < 5) otpRefs.current[index + 1]?.focus();
                  if (next.every((d) => d)) void verifyOtp(next);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Backspace" && !otp[index] && index > 0) {
                    otpRefs.current[index - 1]?.focus();
                  }
                }}
              />
            ))}
          </div>
          {challenge && !challenge.deliveryLive && challenge.devSecret ? (
            <p className="ux-mock-note">{t.journey.auth.devCode(challenge.devSecret)}</p>
          ) : (
            <p className="ux-mock-note">{t.journey.auth.devDelivery}</p>
          )}
          <div className="ux-otp-actions">
            <button
              type="button"
              disabled={cooldown > 0 || busy}
              onClick={() => void sendPhoneCode()}
            >
              {t.journey.auth.resend}
              {cooldown > 0 ? t.journey.auth.resendIn(cooldown) : ""}
            </button>
            <button
              type="button"
              onClick={() => {
                setOtpMode(false);
                setChallenge(null);
                setError(null);
              }}
            >
              {t.journey.auth.changeNumber}
            </button>
          </div>
          <button
            className="button button-primary auth-main"
            type="button"
            disabled={busy || otp.join("").length !== 6}
            onClick={() => void verifyOtp()}
          >
            {t.journey.auth.verify}
            <ArrowRight aria-hidden="true" size={16} />
          </button>
        </div>
      )}

      {error ? <p className="ux-form-error">{error}</p> : null}
    </div>
  );
}
