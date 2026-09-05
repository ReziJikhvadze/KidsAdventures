import { ArrowLeft, ArrowRight, Check, Lock, Sparkles } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError, resolveApiUrl } from "@/lib/api/client";
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
import { clearJourneyResume } from "@/lib/journey/resume";
import { readyPreviewPatch } from "@/lib/journey/previewRecovery";
import { getGuestPreviewStatus } from "@/lib/api/adventure-packs";
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
 * The last screen before the bank.
 *
 * Left: who the parcel goes to, and what happens when the button is pressed. Right: what is
 * being bought and for how much. Nothing here takes a card number — the order is created and
 * the parent is handed to Bank of Georgia's page, which is where the card is entered.
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

  const [quote, setQuote] = useState<QuoteResponse | null>(null);
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
  const packageLabel = isPrint
    ? t.journey.checkout.packagePrint
    : t.journey.checkout.packageDigital;

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
      } catch {
        if (!cancelled) setQuote(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [orderPackage, draft.promoCode]);

  const updateShipping = (patch: Partial<ShippingAddressRequest>) => {
    onChange((prev) => ({ ...prev, shipping: { ...prev.shipping, ...patch } }));
  };

  const validateShipping = (): string | null => {
    if (!isPrint) return null;
    const s = draft.shipping;
    if (!s.recipientName.trim() || !s.addressLine1.trim()) {
      return "მიუთითე მიწოდების მისამართი.";
    }
    if (!normalizeGeorgianPhone(s.recipientPhone) && !s.recipientPhone.trim()) {
      return "მიუთითე ტელეფონის ნომერი.";
    }
    return null;
  };

  const placeOrder = async () => {
    // A second click on a slow connection is a second order and a second payment page, so the
    // guard is here rather than only on the button's disabled attribute, which is a rendering
    // detail rather than a promise.
    if (busy) return;

    const shippingError = validateShipping();
    if (shippingError) {
      setError(shippingError);
      return;
    }
    if (!draft.worldId && !draft.preview?.storyId) {
      setError("აირჩიე სამყარო.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      let orderDraft = draft;
      // Repair already-open/stale checkouts from the authoritative preview without generating
      // another story or selecting a different world on the parent's behalf.
      if (!orderDraft.worldId && orderDraft.preview?.storyId) {
        const status = await getGuestPreviewStatus(orderDraft.preview.storyId);
        const restored = readyPreviewPatch(orderDraft, {
          ...status,
          coverImageUrl: status.coverImageUrl ? resolveApiUrl(status.coverImageUrl) : "",
        });
        orderDraft = { ...orderDraft, ...restored };
        onChange(restored);
      }
      const { primaryId, supportingIds, updated } = await ensureServerCharacters(
        orderDraft.characters,
      );
      onChange({ characters: updated });

      const phone = draft.shipping.recipientPhone.trim();
      const normalizedPhone = normalizeGeorgianPhone(phone) ?? phone;

      const checkout = await ordersApi.createOrder({
        package: orderPackage,
        promoCode: draft.promoCode || undefined,
        draft: {
          primaryCharacterId: primaryId,
          supportingCharacterIds: supportingIds,
          worldId: orderDraft.worldId!,
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
              /*
                The form asks for the address once, but the server still keeps a City of its
                own: it is required, it is what the shipping email names as the destination,
                and GeorgianDelivery reads it to decide whether to quote 4-5 days or 5-8.
                Sending the whole line keeps all three working — the Tbilisi check is a
                substring match, so "თბილისი, გროზნოს 11ა" still resolves to the city window,
                and anything unrecognised falls to the regional one, which is the safe
                direction to be wrong in.
              */
              city: draft.shipping.addressLine1.trim(),
            }
          : undefined,
        returnPath: "/create#generating",
      });

      onChange({ orderId: checkout.orderId, bookId: checkout.bookId ?? null });
      // The book is bought; the pointer that let a new tab find it has done its job.
      clearJourneyResume();

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

  /*
    A digital order goes straight to the bank.

    There is nothing on this screen for it to collect. The print order asks who the parcel is
    for, where it goes and on what number to ring the door; the digital one asks nothing at all,
    so what was left was a heading, a price the parent has just read on the package panel, and a
    button whose only job was to be pressed. A screen that exists to be clicked through is a step.

    A print order still gets the screen, because it still has questions.

    The ref, not `busy`: React can mount an effect twice before any state it sets is visible, and
    two runs here are two orders and two payment pages. On failure the form is revealed instead —
    a parent who cannot be sent to the bank needs somewhere to read why and press again.
  */
  const autoStarted = useRef(false);
  useEffect(() => {
    if (isPrint || autoStarted.current) return;
    autoStarted.current = true;
    void placeOrder();
    // placeOrder closes over this render's draft, which is what it must send.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isPrint]);

  const handingOver = !isPrint && !error;

  const thumbPages = heroDemoPages(heroName, worldId).slice(0, 1);

  /*
    Over the page, not instead of it.

    This replaced the screen with a bare spinner, and swapping a whole page out for a second and
    a half reads as the site losing its place — the parent watched what they were doing vanish
    before anything took them anywhere. Laid over the top, the page they were on stays where it
    was and the mark sits in the middle of it.

    It covers the viewport, so the pay button behind it cannot be reached; `busy` had already
    disabled it, and this means nobody has to rely on that being true.

    The label is for a screen reader, which cannot see that something is turning.
  */
  const handoverOverlay = handingOver ? (
    <div
      className="ux-checkout-handover"
      role="status"
      aria-live="polite"
      aria-label={t.common.states.loading}
    >
      <BekiLoader size={44} />
    </div>
  ) : null;

  return (
    <section className="journey-stage checkout-stage ux-checkout-stage">
      {handoverOverlay}
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
        ) : null}

        {/*
          Three fields, where there were five.

          City, street and a second line were separate boxes, which is how a postal form is
          built and not how anybody says where they live. One line takes the whole address as
          the parent would write it on a parcel; the courier reads it the same way either way.
        */}
        {isPrint ? (
          <div className="ux-ship-fields">
            <label className="field" htmlFor="checkout-ship-recipient">
              <span>{t.journey.checkout.recipient}</span>
              <input
                id="checkout-ship-recipient"
                name="recipientName"
                autoComplete="name"
                value={draft.shipping.recipientName}
                onChange={(e) => updateShipping({ recipientName: e.target.value })}
              />
            </label>
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
            <label className="field field-wide" htmlFor="checkout-ship-address">
              <span>{t.journey.checkout.shippingAddress}</span>
              <input
                id="checkout-ship-address"
                name="addressLine1"
                autoComplete="street-address"
                placeholder={t.journey.checkout.addressPlaceholder}
                value={draft.shipping.addressLine1}
                onChange={(e) => updateShipping({ addressLine1: e.target.value })}
              />
            </label>
          </div>
        ) : null}

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
              <BekiLoader size={16} />
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
          <ArrowLeft aria-hidden="true" size={13} /> {t.common.actions.back}
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
            <small>{packageLabel}</small>
            <strong>{bookTitle}</strong>
            <span>{formatGel(totalMinor)}</span>
          </div>
        </div>

        <div className="summary-lines">
          <h2>{t.journey.checkout.summaryHeading}</h2>
          <span>
            {packageLabel}
            <strong>{formatGel(subtotalMinor)}</strong>
          </span>
          <span>
            {t.journey.checkout.bookLanguage} <strong>{langLabel}</strong>
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
