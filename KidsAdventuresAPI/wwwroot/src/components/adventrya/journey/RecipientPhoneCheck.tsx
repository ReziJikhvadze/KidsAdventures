import { Check, ShieldCheck } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { ApiError } from "@/lib/api/client";
import {
  getRecipientPhoneStatus,
  requestRecipientPhoneCode,
  verifyRecipientPhone,
} from "@/lib/api/orders";
import type { AuthChallengeResponse, RecipientPhoneStatus } from "@/lib/api/types";
import { formatGeorgianPhone, normalizeGeorgianPhone, useT } from "@/lib/i18n";

type Props = {
  /** The recipient's number as the parent typed it; normalised here. */
  phoneNumber: string;
  /** Fires whenever the answer changes, including back to false on a new number. */
  onVerifiedChange: (verified: boolean) => void;
};

/**
 * Proving the handset a parcel is going to, before it is paid for.
 *
 * A printed book is posted to a phone number as much as to a street: the courier rings before
 * they climb the stairs, and a digit typed wrong is a book that comes back to us. So four digits
 * go to the recipient and checkout waits for them.
 *
 * Once, though. The server remembers every number this parent has proved, and counts their own
 * confirmed number as proved, so the panel is absent far more often than it is present - a second
 * book to the same grandmother goes straight to payment. That is why nothing renders until the
 * server has answered: guessing would mean showing a parent a question they have already
 * answered, which reads as the shop having forgotten them.
 *
 * It is deliberately not written as a security step. Nobody is signing in, the number is often
 * not the parent's own, and the sentence above the boxes says what it is for: the courier calls
 * this number.
 */
export function RecipientPhoneCheck({ phoneNumber, onVerifiedChange }: Props) {
  const t = useT();
  const normalized = normalizeGeorgianPhone(phoneNumber);

  const [status, setStatus] = useState<RecipientPhoneStatus | null>(null);
  const [challenge, setChallenge] = useState<AuthChallengeResponse | null>(null);
  const [digits, setDigits] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cooldown, setCooldown] = useState(0);
  const boxes = useRef<(HTMLInputElement | null)[]>([]);

  /*
    Asked of the server, debounced, and thrown away the moment the number changes.

    A parent typing 599 12 34 56 passes through eight strings that are not a number yet, and the
    two that are - the ninth digit, and the ninth again after a correction - would each be a
    request. The delay is long enough that only the number they stopped on is asked about.
  */
  useEffect(() => {
    if (!normalized) {
      setStatus(null);
      setChallenge(null);
      onVerifiedChange(false);
      return;
    }

    let cancelled = false;
    setChallenge(null);
    setDigits([]);
    setError(null);

    const timer = window.setTimeout(() => {
      void getRecipientPhoneStatus(normalized)
        .then((answer) => {
          if (cancelled) return;
          setStatus(answer);
          onVerifiedChange(answer.verified);
        })
        .catch(() => {
          /*
            Silent, and not verified.

            A status call that fails is a network the parent cannot do anything about, and a red
            line beside a phone number they typed correctly would send them looking for a fault
            in it. The gate on the server is what actually holds; this only decides what is
            drawn, so the safe direction to be wrong in is "ask".
          */
          if (cancelled) return;
          setStatus(null);
          onVerifiedChange(false);
        });
    }, 500);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [normalized, onVerifiedChange]);

  /** The countdown under "send it again", whose length the server decides. */
  useEffect(() => {
    if (cooldown <= 0) return;
    const timer = window.setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => window.clearTimeout(timer);
  }, [cooldown]);

  const sendCode = useCallback(async () => {
    if (!normalized || busy) return;

    setBusy(true);
    setError(null);
    try {
      const issued = await requestRecipientPhoneCode(normalized);
      setChallenge(issued);
      setDigits(Array.from({ length: issued.otpLength || 4 }, () => ""));
      setCooldown(issued.resendAfterSeconds);
      window.setTimeout(() => boxes.current[0]?.focus(), 0);
    } catch (err) {
      /* The server's own sentence, which for a throttle already names the wait in seconds. */
      setError(err instanceof ApiError ? err.message : t.journey.checkout.phoneCheckFailed);
    } finally {
      setBusy(false);
    }
  }, [busy, normalized, t]);

  const submitCode = useCallback(
    async (code: string[]) => {
      if (!normalized || busy) return;

      setBusy(true);
      setError(null);
      try {
        const answer = await verifyRecipientPhone(normalized, code.join(""));
        setStatus(answer);
        setChallenge(null);
        onVerifiedChange(answer.verified);
      } catch (err) {
        setError(err instanceof ApiError ? err.message : t.journey.checkout.phoneCheckFailed);
        /* Cleared rather than left standing: the next attempt starts at the first box. */
        setDigits((previous) => previous.map(() => ""));
        window.setTimeout(() => boxes.current[0]?.focus(), 0);
      } finally {
        setBusy(false);
      }
    },
    [busy, normalized, onVerifiedChange, t],
  );

  /** A code pasted whole, which is what a phone offers to do with an SMS. */
  const fill = (text: string) => {
    const pasted = text.replace(/\D/g, "").slice(0, digits.length).split("");
    if (pasted.length === 0) return;

    const next = digits.map((digit, index) => pasted[index] ?? digit);
    setDigits(next);
    if (next.every(Boolean)) void submitCode(next);
  };

  if (!normalized || !status) return null;

  if (status.verified) {
    return (
      <p className="ux-phone-check-done">
        <Check aria-hidden="true" /> {t.journey.checkout.phoneCheckDone}
      </p>
    );
  }

  return (
    <div className="ux-phone-check" data-phone-check="pending">
      <p className="ux-phone-check-head">
        <ShieldCheck aria-hidden="true" />
        <span>
          <strong>{t.journey.checkout.phoneCheckHeading}</strong>
          {t.journey.checkout.phoneCheckWhy}
        </span>
      </p>

      {challenge ? (
        <>
          <small>
            {t.journey.checkout.phoneCheckSent(`+995 ${formatGeorgianPhone(phoneNumber)}`)}
          </small>
          <div
            className="ux-otp-inputs"
            onPaste={(event) => {
              event.preventDefault();
              fill(event.clipboardData.getData("text"));
            }}
          >
            {digits.map((digit, index) => (
              <input
                key={index}
                ref={(el) => {
                  boxes.current[index] = el;
                }}
                inputMode="numeric"
                autoComplete={index === 0 ? "one-time-code" : "off"}
                maxLength={1}
                value={digit}
                aria-label={t.journey.checkout.phoneCheckDigitAria(index + 1)}
                onChange={(event) => {
                  const value = event.target.value.replace(/\D/g, "").slice(-1);
                  const next = [...digits];
                  next[index] = value;
                  setDigits(next);
                  if (value && index < digits.length - 1) boxes.current[index + 1]?.focus();
                  if (next.every(Boolean)) void submitCode(next);
                }}
                onKeyDown={(event) => {
                  if (event.key === "Backspace" && !digits[index] && index > 0) {
                    boxes.current[index - 1]?.focus();
                  }
                }}
              />
            ))}
          </div>
          {/* Only where nothing was really sent, so the flow is walkable without a gateway. */}
          {!challenge.deliveryLive && challenge.devSecret ? (
            <small className="ux-mock-note">
              {t.journey.checkout.phoneCheckDevCode(challenge.devSecret)}
            </small>
          ) : null}
        </>
      ) : null}

      {error ? (
        <p className="ux-form-error" role="alert">
          {error}
        </p>
      ) : null}

      <button
        type="button"
        className="ux-phone-check-send"
        disabled={busy || cooldown > 0}
        aria-busy={busy}
        onClick={() => void sendCode()}
      >
        {challenge ? t.journey.checkout.phoneCheckResend : t.journey.checkout.phoneCheckSend}
        {cooldown > 0 ? t.journey.checkout.phoneCheckResendIn(cooldown) : ""}
        {busy ? <BekiLoader size={14} /> : null}
      </button>
    </div>
  );
}
