import { ArrowRight, Check, Lock, MapPin, Minus, Plus, Sparkles } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { AddressAutocompleteField } from "@/components/adventrya/journey/AddressAutocompleteField";
import { LocationPickerDialog } from "@/components/adventrya/journey/LocationPickerDialog";
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
import { MAX_PRINT_QUANTITY, PRICES } from "@/lib/pricing";
import { heroDemoPages } from "@/lib/story/heroDemoPages";
import { useWorldById, type WorldId } from "@/lib/worlds";

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
  /*
    The real cover, or nothing.

    Falling back to the world painting here and handing it on as `coverImageUrl` left the book
    unable to tell a cover from a stand-in, so its own fallback never ran and the map artwork was
    presented as this child''s cover. Null lets the one fallback in `StorybookVolume` do the job.
  */
  const coverSrc = draft.preview?.coverImageDataUrl || null;
  const bookTitle = draft.preview?.title?.trim() || world.bookTitle(heroName);
  const orderPackage: OrderPackage = draft.bookPackage === "print" ? "Print" : "Digital";
  const isPrint = orderPackage === "Print";
  /*
    Asked for, and possible. There is no parcel on a digital order, so the box is neither shown
    nor sent — a draft that still carries `giftWrap` from a print package the parent then swapped
    away from must not quietly add five lari to a file they download.
  */
  const wantsGiftWrap = isPrint && draft.giftWrap;
  /*
    Copies, and only where there are copies to make. A digital book is one file however many
    times the stepper was pressed before the parent swapped packages, so the number is read
    through the package rather than straight off the draft.
  */
  const copies = isPrint ? Math.min(Math.max(draft.quantity || 1, 1), MAX_PRINT_QUANTITY) : 1;
  const [pickingLocation, setPickingLocation] = useState(false);
  // The book is written in whatever language the parent is reading the site in — there is no
  // separate choice to make, so there is nothing to remember and nothing to get out of step.
  const { locale } = useLocale();
  const langLabel = bookLanguageLabel(locale);

  const [quote, setQuote] = useState<QuoteResponse | null>(null);
  /*
    How many pricing requests are in flight, rather than a boolean.

    Applying a code and changing the copy count can overlap, and with a flag the first one to
    finish would unlock the button while the second was still on its way. A count only reaches
    zero when every price the screen has asked for has come back.
  */
  const [inFlight, setInFlight] = useState(0);
  const pricing = inFlight > 0;
  /** Why a code was refused, kept here rather than read off a quote this screen no longer owns. */
  const [promoMessage, setPromoMessage] = useState<string | null>(null);
  const [promoInput, setPromoInput] = useState(draft.promoCode);
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

  const baseMinor =
    (isPrint ? PRICES.print * copies : PRICES.digital) + (wantsGiftWrap ? PRICES.giftWrap : 0);
  const subtotalMinor = quote?.subtotalMinor ?? baseMinor;
  const discountMinor = quote?.discountMinor ?? 0;
  /*
    The server's figure while it has one, and the local price only until the first quote lands,
    so the summary never shows a wrapping line the order will not carry.
  */
  const giftWrapMinor = quote?.giftWrapMinor ?? (wantsGiftWrap ? PRICES.giftWrap : 0);
  /** The copies the price on screen is actually for — the server's count once it has answered. */
  const quotedCopies = quote?.quantity ?? copies;
  const totalMinor = quote?.totalMinor ?? baseMinor;
  const isFree = quote?.isFree === true || totalMinor === 0;
  const packageLabel = isPrint
    ? t.journey.checkout.packagePrint
    : t.journey.checkout.packageDigital;

  useEffect(() => {
    let cancelled = false;
    /*
      The old price goes before the new one is asked for.

      Wrapping and the copy count are both priced by the server, and leaving the previous
      quote up while the replacement is in flight meant the button could carry the total for
      one set of options while `createOrder` sent another — a parent shown 79 and charged 89.
      Dropping it falls the screen back to the local arithmetic for the options actually
      selected, and the button waits until the server has confirmed that figure.
    */
    setQuote(null);
    setInFlight((n) => n + 1);
    void (async () => {
      try {
        const result = await ordersApi.quoteOrder({
          type: "NewBook",
          package: orderPackage,
          promoCode: draft.promoCode || undefined,
          giftWrap: wantsGiftWrap,
          quantity: copies,
        });
        if (cancelled) return;
        setQuote(result);
        /* A code carried in on the draft is quoted with this first request, so its verdict is
           the panel's opening state rather than something the parent has to press for. */
        if (draft.promoCode) {
          setPromoState(result.promo?.isValid ? "applied" : "invalid");
        }
      } catch {
        if (!cancelled) setQuote(null);
      } finally {
        setInFlight((n) => n - 1);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [orderPackage, draft.promoCode, wantsGiftWrap, copies]);

  /*
    Asks whether the code is any good. It does not price the order.

    This used to install its own answer, and that answer was for whatever the options were when
    it was sent — so a code checked over a slow connection could land after the parent had added
    a copy and overwrite the newer, correct total with an older one. The effect above is now the
    only thing that ever writes a quote, keyed on every input that changes a price; all this does
    is put a valid code on the draft, which the effect then reads and prices properly.
  */
  const applyPromo = async () => {
    const code = promoInput.trim().toUpperCase();
    if (!code) return;
    setPromoState("applying");
    setPromoMessage(null);
    setError(null);
    setInFlight((n) => n + 1);
    try {
      const result = await ordersApi.quoteOrder({
        type: "NewBook",
        package: orderPackage,
        promoCode: code,
        giftWrap: wantsGiftWrap,
        quantity: copies,
      });
      if (result.promo?.isValid) {
        setPromoState("applied");
        onChange({ promoCode: code });
      } else {
        setPromoState("invalid");
        setPromoMessage(result.promo?.message ?? null);
      }
    } catch (err) {
      setPromoState("invalid");
      setError(err instanceof ApiError ? err.message : t.journey.checkout.promoInvalid);
    } finally {
      setInFlight((n) => n - 1);
    }
  };

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
        giftWrap: wantsGiftWrap,
        quantity: copies,
        draft: {
          primaryCharacterId: primaryId,
          supportingCharacterIds: supportingIds,
          worldId: orderDraft.worldId!,
          bookLanguage: locale,
          storyNotes: draft.storyNotes || undefined,
          continuesFromBookId: draft.continuesFromBookId || undefined,
          previewBookId: orderDraft.preview?.storyId || undefined,
          coverRevisionId: orderDraft.preview?.coverRevisionId,
          // previewBookId is what carries the story now: fulfilment reads it from the run we
          // wrote rather than from a copy that has been through a browser. Without it the paid
          // book is written from scratch and the parent receives a different story from the one
          // they read and chose to buy.
          previewCoverImage: orderDraft.preview?.coverImageDataUrl || undefined,
        },
        shippingAddress: isPrint
          ? {
              ...draft.shipping,
              recipientPhone: normalizedPhone,
              /*
                The form asks for the address once, but the server still keeps a City of its
                own: it is required, it is what the shipping email names as the destination,
                and GeorgianDelivery reads it to decide whether to quote 2-3 days or 5-7.
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
    Both packages stop here now, because both have something to answer.

    A digital order used to place itself the moment this screen mounted: it collected nothing, so
    a screen that exists to be clicked through is a step, and it was skipped. The promo field is
    back, and it is the only place in the product where a code can be typed — so skipping the
    screen for digital would mean a digital buyer holding a code with nowhere to enter it, and
    the field being dead markup behind the handover cover for half the orders taken.

    A print order always stopped here. It still does; it simply no longer has the screen to
    itself.
  */

  /*
    The whole screen waits, whichever way the order was placed.

    A digital order has nothing to collect, so this screen is a handover from the moment it opens
    and has always been covered. A print order asks for an address first — and once its button is
    pressed it does exactly the same thing: saves the characters, creates the order, then hands
    the browser to the bank. That is seconds on a phone, and all it showed was a 16px mark inside
    a button on an otherwise unchanged form, which reads as the press not having landed.

    `busy` rather than a state of its own, because `busy` is already held deliberately across
    `location.assign` — see the note there — so the cover stays up for the second or two the old
    page is still on screen, instead of blinking off just as the parent is being taken away.
  */
  const handingOver = busy && !error;

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

      <LocationPickerDialog
        open={pickingLocation}
        onOpenChange={setPickingLocation}
        onChoose={({ address, city }) =>
          updateShipping(city ? { addressLine1: address, city } : { addressLine1: address })
        }
      />
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
        {/*
          Two questions, named.

          The form used to be an unlabelled run of fields followed by three boxes that looked
          alike, so nothing on it said where one question ended and the next began. Where the
          parcel goes and how it is made up are different questions and now say so.
        */}
        {isPrint ? <p className="ux-checkout-step">{t.journey.checkout.stepAddress}</p> : null}
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
            {/*
              The field searches, and the map is under it.

              A Georgian street typed from memory is the easiest thing on this form to get wrong
              and the most expensive: the parcel reaches a courier, and a courier with a misspelt
              street rings or gives up. So the field itself now offers Google's own addresses as
              the parent types, which is where nearly every address will come from, and the map
              is the second way in for the one nobody can spell. Typing still works on its own —
              both are shortcuts, never gates, and both are simply absent where there is no key.
            */}
            <AddressAutocompleteField
              id="checkout-ship-address"
              name="addressLine1"
              fieldClassName="field"
              className="field-wide"
              label={t.journey.checkout.shippingAddress}
              placeholder={t.journey.checkout.addressPlaceholder}
              value={draft.shipping.addressLine1}
              onChange={(addressLine1) => updateShipping({ addressLine1 })}
              onChoose={({ address, city }) =>
                updateShipping(city ? { addressLine1: address, city } : { addressLine1: address })
              }
              onPickOnMap={() => setPickingLocation(true)}
            />

            {/*
              What no map knows and the parent always does.

              A resolved address ends at the building. Which entrance, which floor, which flat and
              what to press at the door is the half that decides whether the parcel arrives, and
              until now there was nowhere on this form to say it — though the order has carried a
              `notes` field the whole time.
            */}
            <label className="field field-wide" htmlFor="checkout-ship-notes">
              <span>{t.journey.checkout.addressNotes}</span>
              <textarea
                id="checkout-ship-notes"
                name="notes"
                rows={2}
                placeholder={t.journey.checkout.addressNotesPlaceholder}
                value={draft.shipping.notes ?? ""}
                onChange={(e) => updateShipping({ notes: e.target.value })}
              />
            </label>
          </div>
        ) : null}

        {/*
          Five lari, and only where there is a parcel to put it on.

          Under the address rather than beside the price: it is a thing being done to the thing
          being posted, so it belongs with where it is going. The total it changes is on the
          right, and it changes as soon as this is ticked — the quote is re-fetched, so the
          figure the button carries is the server's, not five lari the browser added itself.
        */}
        {/*
          Minus, the number, plus — the control every delivery app in the country has taught a
          Georgian parent to read, so it needs no label explaining what it does. One by default,
          because one is what almost everybody wants and a stepper that starts anywhere else is
          a trap. The ends stop rather than wrap: at one there is nothing to take away, and five
          is as many copies as this checkout will take.
        */}
        {/*
          Side by side, because they are the same question asked twice.

          Stacked, each in its own box, they read as two more things to get through. On one row
          the pair is plainly "how should the parcel be made up", and the step costs the column
          one row instead of two.
        */}
        {isPrint ? (
          <>
            <p className="ux-checkout-step">{t.journey.checkout.stepParcel}</p>
            <div className="ux-parcel">
              <div className="ux-parcel-card">
                <div>
                  <strong>{t.journey.checkout.copies}</strong>
                  <small>{t.journey.checkout.copiesNote}</small>
                </div>
                <div className="ux-parcel-foot">
                  <span>{formatGel(PRICES.print * copies)}</span>
                  <div className="ux-copies-stepper">
                    <button
                      type="button"
                      aria-label={t.journey.checkout.copiesFewer}
                      disabled={copies <= 1}
                      onClick={() => onChange({ quantity: Math.max(1, copies - 1) })}
                    >
                      <Minus aria-hidden="true" size={16} />
                    </button>
                    <b aria-live="polite">{copies}</b>
                    <button
                      type="button"
                      aria-label={t.journey.checkout.copiesMore}
                      disabled={copies >= MAX_PRINT_QUANTITY}
                      onClick={() =>
                        onChange({ quantity: Math.min(MAX_PRINT_QUANTITY, copies + 1) })
                      }
                    >
                      <Plus aria-hidden="true" size={16} />
                    </button>
                  </div>
                </div>
              </div>

              {/*
                A switch, not a tick — but a real checkbox underneath it.

                The control is drawn as the toggle the design asks for and stays an
                `input[type=checkbox]`, so the keyboard, the screen reader and the label
                association all keep working; only its painting changes.
              */}
              <label className={`ux-parcel-card ux-gift-wrap${draft.giftWrap ? " is-on" : ""}`}>
                <div>
                  <strong>{t.journey.checkout.giftWrap}</strong>
                  <small>{t.journey.checkout.giftWrapNote}</small>
                </div>
                <div className="ux-parcel-foot">
                  <b>+{formatGel(PRICES.giftWrap)}</b>
                  <input
                    type="checkbox"
                    name="giftWrap"
                    checked={draft.giftWrap}
                    onChange={(e) => onChange({ giftWrap: e.target.checked })}
                  />
                  <span className="ux-switch" aria-hidden="true" />
                </div>
              </label>
            </div>
          </>
        ) : null}
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
          {/* The book on its own. Wrapping is inside `subtotalMinor` — the server prices it as
              part of the subtotal so a promo can reach it — so printing the subtotal here and
              the wrapping again below made the lines add up to more than the total under them. */}
          <span>
            {packageLabel}
            {quotedCopies > 1 ? ` × ${quotedCopies}` : ""}
            <strong>{formatGel(subtotalMinor - giftWrapMinor)}</strong>
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
          {giftWrapMinor > 0 ? (
            <span>
              {t.journey.checkout.giftWrap} <strong>{formatGel(giftWrapMinor)}</strong>
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

        {/*
          Back, as asked. It went when this screen was cut to one page, and a code on the draft
          still discounted — but only if something else had already put it there, which left a
          parent holding a code with nowhere to type it.
        */}
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
                  if (promoState === "invalid") {
                    setPromoState("idle");
                    setPromoMessage(null);
                  }
                }}
              />
              {promoState === "applied" ? (
                <button
                  type="button"
                  onClick={() => {
                    setPromoInput("");
                    onChange({ promoCode: "" });
                    setPromoState("idle");
                    setPromoMessage(null);
                    /*
                      The discount goes with the code, in the same tick.

                      Clearing only the code left the discounted quote on screen until the
                      re-quote came back — a window in which the button said one total and
                      `createOrder`, sending no promo, would have charged the other. Dropping
                      the quote falls the summary back to the undiscounted price, which is what
                      is about to be charged.
                    */
                    setQuote(null);
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
              {promoMessage || t.journey.checkout.promoInvalid}
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
          /* Not while the price on it is still the browser's guess — see the quote effect. */
          disabled={busy || pricing}
          aria-busy={busy || pricing}
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
        {/*
          No way back from here.

          The button under the pay button was a link to the preview, and it is the one thing on
          this screen that is not the order: a parent one press from the bank was being offered
          somewhere else to go. The browser's own back button still does it for anyone who wants
          it, and losing the link is what lets the column fit a screen without a scrollbar.
        */}

        <p>
          <Lock aria-hidden="true" size={13} />
          {isPrint ? t.journey.checkout.printReuseNote : t.journey.checkout.payFirstNote}
        </p>
      </aside>
    </section>
  );
}
