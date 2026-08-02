import { ArrowLeft, ArrowRight, Check, CreditCard, Lock, Sparkles } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import * as ordersApi from "@/lib/api/orders";
import type { OrderPackage, QuoteResponse, ShippingAddressRequest } from "@/lib/api/types";
import { formatGel, formatGelAmount, normalizeGeorgianPhone, useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { ensureServerCharacters } from "@/lib/journey/syncCharacters";
import { PRICES } from "@/lib/pricing";
import { heroDemoPages } from "@/lib/story/heroDemoPages";
import { useWorldById, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  onPaid: (orderId: string, bookId?: string | null) => void;
};

/**
 * Partner Demo checkout layout:
 * left = payment form (wallets / card / promo)
 * right = order summary with compact book thumb
 * Pay still creates a real order and redirects to Stripe Checkout when not free.
 */
export function CheckoutStage({ draft, onChange, onPaid }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const hero = primaryCharacter(draft);
  const heroName = hero.name.trim() || t.common.fallbackHeroName;
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle =
    draft.preview?.title?.trim() || world.bookTitle(heroName);
  const orderPackage: OrderPackage = draft.bookPackage === "print" ? "Print" : "Digital";
  const isPrint = orderPackage === "Print";
  const langLabel = draft.bookLanguage === "en" ? "English" : "ქართული";

  const [promoInput, setPromoInput] = useState(draft.promoCode);
  const [quote, setQuote] = useState<QuoteResponse | null>(null);
  const [promoState, setPromoState] = useState<"idle" | "applying" | "applied" | "invalid">(
    "idle",
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const baseMinor = isPrint ? PRICES.print : PRICES.digital;
  const subtotalMinor = quote?.subtotalMinor ?? baseMinor;
  const discountMinor = quote?.discountMinor ?? 0;
  const totalMinor = quote?.totalMinor ?? baseMinor;
  const isFree = quote?.isFree === true || totalMinor === 0;
  const packageLabel = isPrint
    ? t.journey.packages.print.title
    : t.journey.packages.digital.title;

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await ordersApi.quoteOrder({
          type: "NewBook",
          package: orderPackage,
          promoCode: draft.promoCode || undefined,
        });
        if (cancelled) return;
        setQuote(result);
        if (draft.promoCode) {
          setPromoState(result.promo?.isValid ? "applied" : "invalid");
        }
      } catch {
        if (!cancelled) setQuote(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [orderPackage, draft.promoCode]);

  const applyPromo = async () => {
    const code = promoInput.trim().toUpperCase();
    if (!code) return;
    setPromoState("applying");
    setError(null);
    try {
      const result = await ordersApi.quoteOrder({
        type: "NewBook",
        package: orderPackage,
        promoCode: code,
      });
      setQuote(result);
      if (result.promo?.isValid) {
        setPromoState("applied");
        onChange({ promoCode: code });
      } else {
        setPromoState("invalid");
      }
    } catch (err) {
      setPromoState("invalid");
      setError(err instanceof ApiError ? err.message : t.journey.checkout.promoInvalid);
    }
  };

  const updateShipping = (patch: Partial<ShippingAddressRequest>) => {
    onChange((prev) => ({ ...prev, shipping: { ...prev.shipping, ...patch } }));
  };

  const validateShipping = (): string | null => {
    if (!isPrint) return null;
    const s = draft.shipping;
    if (!s.recipientName.trim() || !s.city.trim() || !s.addressLine1.trim()) {
      return "მიუთითე მიწოდების მისამართი.";
    }
    if (!normalizeGeorgianPhone(s.recipientPhone) && !s.recipientPhone.trim()) {
      return "მიუთითე ტელეფონის ნომერი.";
    }
    return null;
  };

  const placeOrder = async () => {
    const shippingError = validateShipping();
    if (shippingError) {
      setError(shippingError);
      return;
    }
    if (!draft.worldId) {
      setError("აირჩიე სამყარო.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const { primaryId, supportingIds, updated } = await ensureServerCharacters(draft.characters);
      onChange({ characters: updated });

      const phone = draft.shipping.recipientPhone.trim();
      const normalizedPhone = normalizeGeorgianPhone(phone) ?? phone;

      const checkout = await ordersApi.createOrder({
        package: orderPackage,
        promoCode: draft.promoCode || undefined,
        draft: {
          primaryCharacterId: primaryId,
          supportingCharacterIds: supportingIds,
          worldId: draft.worldId,
          bookLanguage: draft.bookLanguage,
          storyNotes: draft.storyNotes || undefined,
          continuesFromBookId: draft.continuesFromBookId || undefined,
          previewBookId: draft.preview?.storyId || undefined,
        },
        shippingAddress: isPrint
          ? {
              ...draft.shipping,
              recipientPhone: normalizedPhone,
            }
          : undefined,
        returnPath: "/create#generating",
      });

      onChange({ orderId: checkout.orderId, bookId: checkout.bookId ?? null });

      if (checkout.isFree || !checkout.checkoutUrl) {
        onPaid(checkout.orderId, checkout.bookId);
        return;
      }

      window.location.assign(checkout.checkoutUrl);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "შეკვეთა ვერ შეიქმნა.");
    } finally {
      setBusy(false);
    }
  };

  const thumbPages = heroDemoPages(heroName, worldId).slice(0, 1);

  return (
    <section className="journey-stage checkout-stage ux-checkout-stage">
      <div className="checkout-form">
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.checkout.secure}
        </p>
        <h1>{isPrint ? t.journey.checkout.printTitle : t.journey.checkout.title}</h1>

        {isFree ? (
          <div className="ux-zero-total">
            <Check aria-hidden="true" />
            <div>
              <strong>{t.journey.checkout.zeroTotal}</strong>
              <p>{t.journey.checkout.zeroTotalNote}</p>
            </div>
          </div>
        ) : (
          <>
            <div className="quick-pay">
              <button type="button" disabled={busy} onClick={() => void placeOrder()}>
                Pay
              </button>
              <button type="button" disabled={busy} onClick={() => void placeOrder()}>
                G Pay
              </button>
            </div>

            <div className="auth-divider">
              <span>{t.journey.checkout.orCard}</span>
            </div>

            <label className="field field-wide">
              <span>{t.journey.checkout.cardNumber}</span>
              <div className="card-input">
                <CreditCard aria-hidden="true" size={18} />
                <input
                  id="checkout-card-number"
                  name="cardNumber"
                  readOnly
                  defaultValue="4242 4242 4242 4242"
                  aria-label={t.journey.checkout.cardNumber}
                  autoComplete="cc-number"
                />
                <small>VISA · MC</small>
              </div>
            </label>

            <div className="form-grid">
              <label className="field" htmlFor="checkout-card-expiry">
                <span>{t.journey.checkout.expiry}</span>
                <input
                  id="checkout-card-expiry"
                  name="cardExpiry"
                  readOnly
                  defaultValue="12 / 29"
                  autoComplete="cc-exp"
                />
              </label>
              <label className="field" htmlFor="checkout-card-cvc">
                <span>CVC</span>
                <input
                  id="checkout-card-cvc"
                  name="cardCvc"
                  readOnly
                  defaultValue="123"
                  autoComplete="cc-csc"
                />
              </label>
            </div>
          </>
        )}

        {isPrint ? (
          <label className="field field-wide" htmlFor="checkout-ship-recipient" style={{ marginTop: 16 }}>
            <span>{t.journey.checkout.shippingAddress}</span>
            <input
              id="checkout-ship-recipient"
              name="recipientName"
              autoComplete="name"
              value={draft.shipping.recipientName}
              placeholder="მიმღები"
              onChange={(e) => updateShipping({ recipientName: e.target.value })}
            />
          </label>
        ) : null}

        {isPrint ? (
          <div className="form-grid">
            <label className="field" htmlFor="checkout-ship-phone">
              <span>{t.common.labels.phone}</span>
              <input
                id="checkout-ship-phone"
                name="recipientPhone"
                type="tel"
                autoComplete="tel"
                value={draft.shipping.recipientPhone}
                onChange={(e) => updateShipping({ recipientPhone: e.target.value })}
              />
            </label>
            <label className="field" htmlFor="checkout-ship-city">
              <span>ქალაქი</span>
              <input
                id="checkout-ship-city"
                name="city"
                autoComplete="address-level2"
                value={draft.shipping.city}
                onChange={(e) => updateShipping({ city: e.target.value })}
              />
            </label>
            <label className="field" htmlFor="checkout-ship-address" style={{ gridColumn: "1 / -1" }}>
              <span>მისამართი</span>
              <input
                id="checkout-ship-address"
                name="addressLine1"
                autoComplete="street-address"
                value={draft.shipping.addressLine1}
                onChange={(e) => updateShipping({ addressLine1: e.target.value })}
              />
            </label>
            <label className="field" htmlFor="checkout-ship-address2">
              <span>დამატებითი</span>
              <input
                id="checkout-ship-address2"
                name="addressLine2"
                autoComplete="address-line2"
                value={draft.shipping.addressLine2 ?? ""}
                onChange={(e) => updateShipping({ addressLine2: e.target.value })}
              />
            </label>
          </div>
        ) : null}

        <div className="ux-promo-panel">
          <label className="field" htmlFor="checkout-promo">
            <span>Promocode</span>
            <div>
              <input
                id="checkout-promo"
                name="promoCode"
                value={promoInput}
                disabled={promoState === "applied"}
                placeholder={t.journey.checkout.promoPlaceholder}
                onChange={(e) => {
                  setPromoInput(e.target.value);
                  if (promoState === "invalid") setPromoState("idle");
                }}
              />
              {promoState === "applied" ? (
                <button
                  type="button"
                  onClick={() => {
                    setPromoInput("");
                    onChange({ promoCode: "" });
                    setPromoState("idle");
                  }}
                >
                  {t.journey.checkout.promoRemove}
                </button>
              ) : (
                <button
                  type="button"
                  disabled={busy || promoState === "applying" || !promoInput.trim()}
                  onClick={() => void applyPromo()}
                >
                  {promoState === "applying" ? "მოწმდება…" : t.common.actions.apply}
                </button>
              )}
            </div>
          </label>
          {promoState === "applied" ? (
            <p className="valid">
              <Check aria-hidden="true" /> {t.journey.checkout.promoApplied}
            </p>
          ) : null}
          {promoState === "invalid" ? (
            <p className="invalid" role="alert">
              {quote?.promo?.message || t.journey.checkout.promoInvalid}
            </p>
          ) : null}
        </div>

        {error ? <p className="ux-form-error">{error}</p> : null}

        <button
          className="button button-primary checkout-pay"
          type="button"
          disabled={busy}
          onClick={() => void placeOrder()}
        >
          {isFree
            ? t.journey.checkout.activateOrder
            : t.journey.checkout.pay(formatGelAmount(totalMinor))}
          <ArrowRight aria-hidden="true" size={16} />
        </button>

        <Link className="text-back" to="/create" hash="preview">
          <ArrowLeft aria-hidden="true" size={13} /> უკან
        </Link>
      </div>

      <aside className="order-summary ux-order-summary">
        <div className="ux-compact-product">
          <StorybookVolume
            variant="display"
            className={`storybook storybook-thumbnail theme-${worldId}`}
            heroName={heroName}
            title={bookTitle}
            coverImageUrl={coverSrc}
            worldId={worldId}
            pages={thumbPages}
            lockedPageCount={0}
            isUnlocked={false}
            interactive={false}
            initialIndex={0}
          />
          <div>
            <small>{isPrint ? "Print + Digital" : "Digital"}</small>
            <strong>{bookTitle}</strong>
            <span>{formatGel(totalMinor)}</span>
          </div>
        </div>

        <div className="summary-lines">
          <h2>{t.journey.checkout.summaryHeading}</h2>
          <span>
            {isPrint ? "Print + Digital Book" : "Digital Book"}
            <strong>{formatGel(subtotalMinor)}</strong>
          </span>
          <span>
            წიგნის ენა <strong>{langLabel}</strong>
          </span>
          {isPrint ? (
            <span>
              {t.journey.checkout.deliveryLine}
              <strong>0 ₾</strong>
            </span>
          ) : null}
          {discountMinor > 0 ? (
            <span className="ux-discount-line">
              {t.journey.checkout.discountLine}
              {draft.promoCode} <strong>−{formatGel(discountMinor)}</strong>
            </span>
          ) : null}
          <div>
            {t.journey.checkout.total}
            <strong>{formatGel(totalMinor)}</strong>
          </div>
        </div>

        <p>
          <Lock aria-hidden="true" size={13} />
          {isPrint ? t.journey.checkout.printReuseNote : t.journey.checkout.payFirstNote}
        </p>
      </aside>
    </section>
  );
}
