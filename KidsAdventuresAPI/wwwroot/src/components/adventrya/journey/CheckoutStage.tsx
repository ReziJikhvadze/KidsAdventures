import { ArrowLeft, ArrowRight, Check, CreditCard, Loader2, Lock, Sparkles } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import * as ordersApi from "@/lib/api/orders";
import type { OrderPackage, QuoteResponse, ShippingAddressRequest } from "@/lib/api/types";
import {
  bookLanguageLabel,
  formatGel,
  formatGelAmount,
  normalizeGeorgianPhone,
  useLocale,
  useT,
} from "@/lib/i18n";
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
  const bookTitle = draft.preview?.title?.trim() || world.bookTitle(heroName);
  const orderPackage: OrderPackage = draft.bookPackage === "print" ? "Print" : "Digital";
  const isPrint = orderPackage === "Print";
  // The book is written in whatever language the parent is reading the site in — there is no
  // separate choice to make, so there is nothing to remember and nothing to get out of step.
  const { locale } = useLocale();
  const langLabel = bookLanguageLabel(locale);

  const [promoInput, setPromoInput] = useState(draft.promoCode);
  const [quote, setQuote] = useState<QuoteResponse | null>(null);
  const [promoState, setPromoState] = useState<"idle" | "applying" | "applied" | "invalid">("idle");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /** Whether this screen is still the one in front of the parent. See placeOrder. */
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const baseMinor = isPrint ? PRICES.print : PRICES.digital;
  const subtotalMinor = quote?.subtotalMinor ?? baseMinor;
  const discountMinor = quote?.discountMinor ?? 0;
  const totalMinor = quote?.totalMinor ?? baseMinor;
  const isFree = quote?.isFree === true || totalMinor === 0;
  const packageLabel = isPrint ? t.journey.packages.print.title : t.journey.packages.digital.title;

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
    // A second click on a slow connection is a second order and a second Stripe session, so the
    // guard is here rather than only on the button's disabled attribute — which is a rendering
    // detail, and this function is also reached from the two quick-pay buttons.
    if (busy) return;

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
          bookLanguage: locale,
          storyNotes: draft.storyNotes || undefined,
          continuesFromBookId: draft.continuesFromBookId || undefined,
          previewBookId: draft.preview?.storyId || undefined,
          // previewBookId is what carries the story now: fulfilment reads it from the run we
          // wrote rather than from a copy that has been through a browser. Without it the paid
          // book is written from scratch and the parent receives a different story from the one
          // they read and chose to buy.
          previewCoverImage: draft.preview?.coverImageDataUrl || undefined,
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

      // Nobody is on this screen any more.
      //
      // Placing an order takes seconds — a portrait is uploaded, characters are saved, the order
      // is created — and a parent who taps "back" during it used to have the answer arrive
      // anyway: the reply landed on a screen they had left and either pulled them into the
      // generating stage or opened Stripe from wherever they now were. The order itself is real
      // and paid for either way, so it is not discarded; it is simply not allowed to seize a
      // page that has moved on. The dashboard shows it, and returning from Stripe resumes it.
      if (!mounted.current) return;

      if (checkout.isFree || !checkout.checkoutUrl) {
        onPaid(checkout.orderId, checkout.bookId);
        return;
      }

      window.location.assign(checkout.checkoutUrl);

      // Deliberately still busy. `assign` only *starts* the navigation and returns at once, so
      // clearing it here would light the button up again for the second or two the old page is
      // still on screen — and that click is a second order with a second Stripe session.
    } catch (err) {
      if (!mounted.current) return;
      setError(err instanceof ApiError ? err.message : "შეკვეთა ვერ შეიქმნა.");
      // Only a failure gives the button back. Every success either navigates away or hands the
      // journey to the generating stage, and neither wants this screen accepting another press.
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
            {/* These start the same order as the button below, so they wait the same way. */}
            <div className="quick-pay">
              <button
                type="button"
                disabled={busy}
                aria-busy={busy}
                onClick={() => void placeOrder()}
              >
                {busy ? (
                  <Loader2 className="checkout-spinner" aria-hidden="true" size={15} />
                ) : null}
                Pay
              </button>
              <button
                type="button"
                disabled={busy}
                aria-busy={busy}
                onClick={() => void placeOrder()}
              >
                {busy ? (
                  <Loader2 className="checkout-spinner" aria-hidden="true" size={15} />
                ) : null}
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
                <small>Visa / Mastercard</small>
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
          <label
            className="field field-wide"
            htmlFor="checkout-ship-recipient"
            style={{ marginTop: 16 }}
          >
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
            <label
              className="field"
              htmlFor="checkout-ship-address"
              style={{ gridColumn: "1 / -1" }}
            >
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
            <span>{t.journey.checkout.promoLabel}</span>
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
                  {promoState === "applying" ? t.common.actions.checking : t.common.actions.apply}
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

        {/*
          The button says what it is doing. Behind it a portrait is uploaded and an order is
          created, which is seconds on a phone connection — and it used to keep its ordinary
          label throughout, so the only sign anything had happened was that the press did
          nothing. aria-busy so it is not only the sighted parent who is told.
        */}
        <button
          className="button button-primary checkout-pay"
          type="button"
          disabled={busy}
          aria-busy={busy}
          onClick={() => void placeOrder()}
        >
          {busy ? (
            <>
              <Loader2 className="checkout-spinner" aria-hidden="true" size={16} />
              {t.journey.checkout.placingOrder}
            </>
          ) : (
            <>
              {isFree
                ? t.journey.checkout.activateOrder
                : t.journey.checkout.pay(formatGelAmount(totalMinor))}
              <ArrowRight aria-hidden="true" size={16} />
            </>
          )}
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
            // Not turnable: this is an 82px thumbnail in the order summary, not a
            // reading surface. Making it interactive rendered page controls, a page
            // rail and a gesture hint at full size on top of the title and price.
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
