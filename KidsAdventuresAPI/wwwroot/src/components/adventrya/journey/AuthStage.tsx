import { Check, Sparkles } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import * as authApi from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import type { AuthChallengeResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import {
  formatGeorgianPhone,
  normalizeGeorgianPhone,
  t,
} from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { WORLD_BY_ID, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onAuthenticated: () => void;
};

type AuthTab = "email" | "phone";

export function AuthStage({ draft, onAuthenticated }: Props) {
  const { isAuthenticated, isLoading, signInWithPhoneCode } = useAuth();
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle =
    draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  const [tab, setTab] = useState<AuthTab>("email");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [otp, setOtp] = useState(["", "", "", "", "", ""]);
  const [challenge, setChallenge] = useState<AuthChallengeResponse | null>(null);
  const [otpMode, setOtpMode] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [magicSent, setMagicSent] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const otpRefs = useRef<Array<HTMLInputElement | null>>([]);

  useEffect(() => {
    if (!isLoading && isAuthenticated) onAuthenticated();
  }, [isAuthenticated, isLoading, onAuthenticated]);

  useEffect(() => {
    if (cooldown <= 0) return;
    const id = window.setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => window.clearTimeout(id);
  }, [cooldown]);

  const sendMagicLink = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await authApi.requestMagicLink(email.trim(), "/create#checkout");
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

  if (isLoading) {
    return (
      <section className="journey-stage auth-stage ux-auth-stage">
        <p>{t.common.actions.checking}</p>
      </section>
    );
  }

  return (
    <section className="journey-stage auth-stage ux-auth-stage">
      <div className="ux-auth-book">
        <article className="ux-book-cover">
          <div
            className="ux-cover-art"
            style={{ backgroundImage: `url("${coverSrc}")` }}
            aria-hidden="true"
          />
          <div className="ux-cover-shade" aria-hidden="true" />
          <span className="ux-cover-brand">ADVENTRYA</span>
          <small>{world.theme}</small>
          <h2>{bookTitle}</h2>
          <span className="ux-cover-name">{hero.name || t.common.fallbackHeroName}</span>
        </article>
        <p>
          <Check aria-hidden="true" /> {t.journey.auth.previewSaved}
        </p>
      </div>

      <div className="auth-panel">
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.auth.eyebrow}
        </p>
        <h1>
          {t.journey.auth.titlePrefix}
          {hero.name || t.common.fallbackHeroName}
        </h1>
        <p>{t.journey.auth.lead}</p>

        {!otpMode ? (
          <>
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
                <label className="field">
                  <span>{t.common.labels.email}</span>
                  <input
                    type="email"
                    value={email}
                    autoComplete="email"
                    onChange={(e) => {
                      setEmail(e.target.value);
                      setMagicSent(false);
                    }}
                  />
                </label>
                {magicSent ? (
                  <p className="ux-mock-note">{t.journey.auth.magicLinkSent(email)}</p>
                ) : null}
                {challenge && !challenge.deliveryLive && challenge.devSecret ? (
                  <p className="ux-mock-note">
                    {t.journey.auth.devDelivery} {t.journey.auth.devCode(challenge.devSecret)}
                  </p>
                ) : null}
                <button
                  className="button journey-primary"
                  type="button"
                  disabled={busy || !email.trim() || cooldown > 0}
                  onClick={() => void sendMagicLink()}
                >
                  {t.journey.auth.sendMagicLink}
                  {cooldown > 0 ? t.journey.auth.resendIn(cooldown) : ""}
                </button>
              </>
            ) : (
              <>
                <label className="field ux-phone-field">
                  <span>{t.journey.auth.phoneLabel}</span>
                  <div>
                    <b>+995</b>
                    <input
                      inputMode="numeric"
                      value={formatGeorgianPhone(phone)}
                      onChange={(e) => setPhone(e.target.value)}
                      placeholder="5XX XX XX XX"
                    />
                  </div>
                </label>
                <button
                  className="button journey-primary"
                  type="button"
                  disabled={busy || cooldown > 0}
                  onClick={() => void sendPhoneCode()}
                >
                  {t.journey.auth.sendCode}
                  {cooldown > 0 ? t.journey.auth.resendIn(cooldown) : ""}
                </button>
              </>
            )}
          </>
        ) : (
          <div className="ux-otp-panel">
            <small>{formatGeorgianPhone(phone)}</small>
            <strong>{t.journey.auth.otpHeading}</strong>
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
                  inputMode="numeric"
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
              <p className="ux-mock-note">
                {t.journey.auth.devDelivery} {t.journey.auth.devCode(challenge.devSecret)}
              </p>
            ) : null}
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
              className="button journey-primary"
              type="button"
              disabled={busy || otp.join("").length !== 6}
              onClick={() => void verifyOtp()}
            >
              {t.journey.auth.verify}
            </button>
          </div>
        )}

        {error ? <p className="ux-form-error">{error}</p> : null}
      </div>
    </section>
  );
}
