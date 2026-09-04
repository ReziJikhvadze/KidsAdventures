import { ArrowRight, KeyRound, Mail } from "lucide-react";
import { type ReactNode, useEffect, useRef, useState } from "react";

import { GoogleSignInButton, GoogleSignInBusyButton } from "@/components/auth/GoogleSignInButton";
import * as authApi from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import type { AuthChallengeResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { rememberMagicReturnPath } from "@/lib/auth/magicReturn";
import { formatGeorgianPhone, normalizeGeorgianPhone, useT } from "@/lib/i18n";

type AuthTab = "email" | "phone";

/**
 * Which doors the panel offers, and why one of them closed.
 *
 * Apple still answers a press with "coming soon", so it stays hidden until it does not.
 *
 * The one-time link is hidden for the opposite reason: not because it is unfinished, but because
 * it is no longer wanted. A parent can sign in with a password or with their phone number, and a
 * third way that costs a trip to a mail client — which a spam filter, a webmail login or a
 * different browser can each break — earns less than the room it takes on a phone-sized panel.
 *
 * Hidden here rather than deleted, on purpose. `/auth/magic` still trades a token for a session
 * and the server still knows how to send one, so a link posted minutes before this shipped still
 * works when it is opened; only the way of asking for a new one is gone. Flip this back and the
 * button returns with nothing else to change.
 */
const APPLE_SIGN_IN_READY: boolean = false;
const PHONE_SIGN_IN_READY: boolean = true;
const MAGIC_LINK_SIGN_IN_READY: boolean = false;

/**
 * How many boxes to draw before the server has said how many it wants.
 *
 * The length is the server's `PasswordlessAuth:OtpLength`, and every challenge response carries
 * it, so the boxes on screen are always the ones the code will fit. This is only the value used
 * to build the array before the first request — no boxes are drawn until a challenge exists, so
 * it is never what a parent types into. It matches the server default so the two agree even if
 * an older API answers without the field.
 */
const OTP_FALLBACK_LENGTH = 4;

const emptyOtp = (length: number) =>
  Array.from({ length: Math.max(1, length) || OTP_FALLBACK_LENGTH }, () => "");

/**
 * How a parent proves the email is theirs: a link we send them, a password they already have, or
 * a password they are setting for the first time.
 *
 * The password is the default now that the link is hidden — see MAGIC_LINK_SIGN_IN_READY. The
 * `link` mode is kept whole rather than torn out, because the flag is meant to be flippable and
 * because a token already in someone's inbox is still honoured on the way in.
 *
 * `register` is a separate mode rather than a separate endpoint. `/api/auth/continue` signs in a
 * known email and creates an unknown one, so one form could do both — and did, which is why
 * signing in asked for the password twice. Typing it twice is a guard on the one occasion it is
 * worth guarding: the moment a password is being chosen, when a typo would lock a parent out of
 * an account that did not exist a second ago. Signing in needs no such guard; the server either
 * knows the password or does not.
 */
type EmailMode = "link" | "password" | "register";

/**
 * Mirrors `PasswordValidator.ValidateOrThrow` on the server, so a password the API will refuse
 * is refused here — where the parent can still see both fields — rather than after a round trip
 * that answers in English. `\p{Lu}`/`\p{Ll}` rather than `A-Z`/`a-z` because that is what
 * .NET's `char.IsUpper`/`IsLower` mean.
 */
function passwordMeetsPolicy(value: string): boolean {
  return value.length >= 8 && /\d/.test(value) && /\p{Lu}/u.test(value) && /\p{Ll}/u.test(value);
}

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
  const t = useT();
  const { loginWithGoogle, signInWithPhoneCode, continueWith } = useAuth();

  const [tab, setTab] = useState<AuthTab>("email");
  // Whichever way in the switcher shows first: the link when it is offered, the password when
  // it is not. Starting on a mode with no button to leave it would strand the parent.
  const [emailMode, setEmailMode] = useState<EmailMode>(
    MAGIC_LINK_SIGN_IN_READY ? "link" : "password",
  );
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [passwordRepeat, setPasswordRepeat] = useState("");
  const [phone, setPhone] = useState("");
  const [otp, setOtp] = useState(() => emptyOtp(OTP_FALLBACK_LENGTH));
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
      /*
        Remember where this parent asked from, once a link is actually on its way.

        The address in the email carries `next=`, and that is still what decides where the link
        lands. This is the second copy, for the link that arrives without it — a mail client that
        rewrote the query, a forwarded address, a server whose base URL was configured with one.
        Without it the landing fell back to the checkout for everybody, including the parent who
        had pressed "my space" and only ever wanted their own shelf.

        After the request, not before: a throttled or refused request sends no email, and writing
        the path anyway would leave one behind for some later link to pick up.
      */
      rememberMagicReturnPath(returnPath);
      setChallenge(result);
      setMagicSent(true);
      setCooldown(result.resendAfterSeconds || 30);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ბმული ვერ გაიგზავნა.");
    } finally {
      setBusy(false);
    }
  };

  /*
    One endpoint, two intentions, and the server is told which.

    `continueAuth` signs a known email in and creates an unknown one. That is right for a form
    that asks no question, and this form asks: pressing "sign in" with a mistyped address would
    otherwise be answered with a new empty account carrying the password the parent believes
    belongs to their real one. The intent goes with the request and the server refuses to create
    anything under `signin`.

    The repeated field is checked here and never sent. It guards the one moment worth guarding —
    a password being chosen, where a typo locks a parent out of an account a second old.
  */
  const submitPassword = async () => {
    /*
      Both checks belong to registration only. On the way in, the password either matches what the
      server has or it does not, and refusing it here for being short would refuse a parent their
      own account because the rules changed after they made it.
    */
    if (emailMode === "register") {
      if (password !== passwordRepeat) {
        setError(t.journey.auth.passwordMismatch);
        return;
      }
      if (!passwordMeetsPolicy(password)) {
        setError(t.journey.auth.passwordHint);
        return;
      }
    }

    setBusy(true);
    setError(null);
    try {
      await continueWith(email.trim(), password, emailMode === "register" ? "register" : "signin");
      onAuthenticated();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t.journey.auth.passwordFailed);
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
      // Sized by the answer, not by an assumption: the server says how long the code it just
      // generated is, and the boxes are drawn to match.
      setOtp(emptyOtp(result.otpLength || OTP_FALLBACK_LENGTH));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "კოდი ვერ გაიგზავნა.");
    } finally {
      setBusy(false);
    }
  };

  const verifyOtp = async (digits = otp) => {
    const code = digits.join("");

    // Every box filled — join() drops the empty ones, so a shorter string means a gap.
    if (code.length !== digits.length) {
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
    const digits = value.replace(/\D/g, "").slice(0, otp.length).split("");
    const next = Array.from({ length: otp.length }, (_, i) => digits[i] ?? "");
    setOtp(next);
    if (digits.length === otp.length) void verifyOtp(next);
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
          {APPLE_SIGN_IN_READY ? (
            <button
              className="social-auth"
              type="button"
              disabled={busy}
              onClick={() => setError(t.journey.auth.appleSoon)}
            >
              <span>●</span>
              {t.journey.auth.apple}
            </button>
          ) : null}
          <div className="auth-divider">
            <span>{t.common.labels.or}</span>
          </div>

          {/*
            The switcher names the two ways in that actually work.

            It used to choose between email and a phone number, and the choice between a link and
            a password was a line of small print under the button — which is how a parent who
            wanted to sign in with a password they had already set came away believing there was
            no such thing. With the phone tab put away the row is free, and the real question
            takes it.
          */}
          {/*
            Every way in, on one row.

            It used to be two switchers stacked: email against phone, and then — only if you
            found the line of small print under the form — a link against a password. So the two
            things a parent actually chooses between were a level apart, and the password was the
            one nobody found. Three ways exist, so the row is three wide and each is one tap.

            Registration is not a fourth: it is the same password form with a second box, reached
            from the line under it, and the password side stays lit while it is open.

            A group of buttons, not a tablist. It once claimed `role="tablist"` and gave its
            buttons none of what that promises — no `role="tab"`, no `aria-selected`, no arrow-key
            navigation, no `tabpanel` to own — so a screen reader was told to expect tabs and then
            could not say which one was chosen. `aria-pressed` carries the state instead, which is
            the same state the gold fill shows.
          */}
          <div className="ux-auth-switcher" role="group" aria-label={t.journey.auth.methodGroup}>
            {MAGIC_LINK_SIGN_IN_READY ? (
              <button
                type="button"
                aria-pressed={tab === "email" && emailMode === "link"}
                className={tab === "email" && emailMode === "link" ? "selected" : ""}
                onClick={() => {
                  setTab("email");
                  setEmailMode("link");
                  setError(null);
                }}
              >
                {t.journey.auth.tabMagicLink}
              </button>
            ) : null}
            <button
              type="button"
              aria-pressed={tab === "email" && emailMode !== "link"}
              className={tab === "email" && emailMode !== "link" ? "selected" : ""}
              onClick={() => {
                setTab("email");
                setEmailMode("password");
                setError(null);
              }}
            >
              {t.journey.auth.tabPassword}
            </button>
            {PHONE_SIGN_IN_READY ? (
              <button
                type="button"
                aria-pressed={tab === "phone"}
                className={tab === "phone" ? "selected" : ""}
                onClick={() => {
                  setTab("phone");
                  setError(null);
                }}
              >
                {t.journey.auth.tabPhone}
              </button>
            ) : null}
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
              {emailMode === "link" ? (
                <>
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
                  {/* Both fields wear `auth-email`, which is what dresses the field above them:
                      the label colour, the icon inset into the box and the pale input are all
                      already written there, and this is one form rather than two that resemble
                      each other. */}
                  <label className="field auth-email" htmlFor="journey-auth-password">
                    <span>{t.journey.auth.passwordLabel}</span>
                    <div>
                      <KeyRound aria-hidden="true" />
                      <input
                        id="journey-auth-password"
                        name="new-password"
                        type="password"
                        value={password}
                        autoComplete="new-password"
                        aria-label={t.journey.auth.passwordLabel}
                        onChange={(e) => setPassword(e.target.value)}
                      />
                    </div>
                  </label>
                  {/* Only while a password is being chosen. Signing in, the second box asked a
                      parent to prove they could type twice something the server was about to
                      check anyway — and made the panel tall enough to need a scrollbar. */}
                  {emailMode === "register" ? (
                    <>
                      <label className="field auth-email" htmlFor="journey-auth-password-repeat">
                        <span>{t.journey.auth.passwordRepeatLabel}</span>
                        <div>
                          <KeyRound aria-hidden="true" />
                          <input
                            id="journey-auth-password-repeat"
                            name="confirm-password"
                            type="password"
                            value={passwordRepeat}
                            autoComplete="new-password"
                            aria-label={t.journey.auth.passwordRepeatLabel}
                            onChange={(e) => setPasswordRepeat(e.target.value)}
                            onKeyDown={(e) => {
                              if (e.key === "Enter") void submitPassword();
                            }}
                          />
                        </div>
                      </label>
                      <p className="ux-mock-note">{t.journey.auth.passwordHint}</p>
                    </>
                  ) : null}
                  <button
                    className="button button-primary auth-main"
                    type="button"
                    disabled={
                      busy ||
                      !email.trim() ||
                      !password ||
                      (emailMode === "register" && !passwordRepeat)
                    }
                    onClick={() => void submitPassword()}
                  >
                    {emailMode === "register"
                      ? t.journey.auth.registerSubmit
                      : t.journey.auth.passwordSubmit}
                    <ArrowRight aria-hidden="true" size={16} />
                  </button>
                </>
              )}

              {/*
                The way to an account, on the page that otherwise only lets you back into one.

                This is a sign-in panel: a parent with no account had nothing here to press. The
                endpoint behind it has always created accounts — that is what made the second
                password box necessary on every sign-in — so what was missing was never the
                ability, only the words for it.
              */}
              {emailMode !== "link" ? (
                <button
                  className="text-back"
                  type="button"
                  onClick={() => {
                    setEmailMode(emailMode === "register" ? "password" : "register");
                    /* Both boxes, not just the repeat: a secret typed to sign in is not a secret
                       chosen for a new account, and leaving it loaded lets one be submitted as
                       the other by a press of the button below. */
                    setPassword("");
                    setPasswordRepeat("");
                    setError(null);
                  }}
                >
                  {emailMode === "register"
                    ? t.journey.auth.haveAccount
                    : t.journey.auth.needAccount}
                </button>
              ) : null}
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
                  if (value && index < otp.length - 1) otpRefs.current[index + 1]?.focus();
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
            disabled={busy || otp.join("").length !== otp.length}
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
