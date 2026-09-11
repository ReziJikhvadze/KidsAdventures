import { ArrowRight, Check, Gift, MapPin, Minus, Pencil, Plus, Sparkles } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { AddressAutocompleteField } from "@/components/adventrya/journey/AddressAutocompleteField";
import { LocationPickerDialog } from "@/components/adventrya/journey/LocationPickerDialog";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError, resolveApiUrl } from "@/lib/api/client";
import * as ordersApi from "@/lib/api/orders";
import { listAddresses, saveAddress } from "@/lib/api/print-orders";
import type {
  AddressResponse,
  OrderPackage,
  QuoteResponse,
  SaveAddressRequest,
  ShippingAddressRequest,
} from "@/lib/api/types";
import {
  formatGel,
  formatGelAmount,
  formatGeorgianPhone,
  normalizeGeorgianPhone,
  useLocale,
  useT,
} from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { clearJourneyResume } from "@/lib/journey/resume";
import { readyPreviewPatch } from "@/lib/journey/previewRecovery";
import { getGuestPreviewStatus } from "@/lib/api/adventure-packs";
import { ensureServerCharacters } from "@/lib/journey/syncCharacters";
import {
  DELIVERY,
  MAX_PRINT_QUANTITY,
  PRICES,
  deliveryOptionsForDisplay,
  isTbilisiAddress,
  resolveDeliveryOption,
  type DeliveryOptionId,
} from "@/lib/pricing";
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
  /*
    Where the parcel is going, as far as this form knows.

    The city field is filled by the address autocomplete when there is a Maps key; without one
    the parent types the whole address into the one line, so both are read. Neither is trusted:
    the server resolves the same way before charging, and this is what lets the screen show the
    right options while it waits for the quote.
  */
  const addressHint = `${draft.shipping.city ?? ""} ${draft.shipping.addressLine1 ?? ""}`;
  const inTbilisi = isPrint && isTbilisiAddress(addressHint);
  /*
    Nothing typed yet is quoted as a region, and so is anywhere that is not Tbilisi.

    That is the higher price, deliberately: recognising Tbilisi later makes the total go down,
    where the reverse would raise it under a parent who had already read it.
  */
  const addressStarted = draft.shipping.addressLine1.trim().length > 2;
  /*
    Read soonest first - three days, then five - which is not the order the pricing rule returns
    them in. That array leads with the default, and the default is the free one; sorting it would
    have moved every Tbilisi order onto the paid window. See `deliveryOptionsForDisplay`.
  */
  const deliveryChoices: DeliveryOptionId[] = isPrint ? deliveryOptionsForDisplay(addressHint) : [];
  const deliveryOption = isPrint
    ? resolveDeliveryOption(
        draft.shipping.deliveryOption as DeliveryOptionId | undefined,
        addressHint,
      )
    : undefined;
  const [pickingLocation, setPickingLocation] = useState(false);
  // The book is written in whatever language the parent is reading the site in — there is no
  // separate choice to make, so there is nothing to remember and nothing to get out of step.
  const { locale } = useLocale();

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
  /** Whether the form has been submitted once, which is when it starts marking its own fields. */
  const [showErrors, setShowErrors] = useState(false);
  /*
    The addresses this parent already has, offered as a list to pick from.

    Every print order is saved with SaveForLater, so a second book has one waiting and a fourth
    has three - and until now the parent retyped one anyway, because nothing on this screen
    asked. This is the pattern every Georgian checkout uses: the saved ones as a radio list with
    the default already chosen, and one button for the address that is not on it.

    Picking one only writes into `draft.shipping`, which is the same place the form writes. So
    validation, the delivery zone and the order payload each keep a single path through them,
    whether the address was chosen or typed.
  */
  const [savedAddresses, setSavedAddresses] = useState<AddressResponse[]>([]);
  const [chosenAddressId, setChosenAddressId] = useState<string | null>(null);
  /** True while the fields are showing: a new address, or an account with none saved. */
  const [addressOpen, setAddressOpen] = useState(true);
  /** The saved row the open fields came from, when they came from one. Null for a new address. */
  const [editingAddressId, setEditingAddressId] = useState<string | null>(null);
  const [savingAddress, setSavingAddress] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /** Whether this screen is still the one in front of the parent. See placeOrder. */
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  /*
    Moving city corrects the choice rather than leaving it to fail at the till.

    A parent who picked the free Tbilisi delivery and then changed the address to Batumi has a
    selection that address cannot have. The server would price it as regional anyway, so the
    draft is brought into line here and the screen never shows a selected option that the total
    underneath it disagrees with.
  */
  useEffect(() => {
    if (!isPrint || !deliveryOption) return;
    if (draft.shipping.deliveryOption === deliveryOption) return;
    onChange((prev) => ({
      ...prev,
      shipping: { ...prev.shipping, deliveryOption },
    }));
  }, [isPrint, deliveryOption, draft.shipping.deliveryOption, onChange]);

  /*
    Zero until there is somewhere to send it.

    The screen used to carry the regional price from the moment the package was chosen, which put
    8 GEL and a five-to-seven-day window against a parcel whose destination nobody had typed. The
    quote agrees (see OrderService): no address, no delivery, and the total is the book.
  */
  const deliveryPriceMinor =
    isPrint && addressStarted && deliveryOption ? DELIVERY[deliveryOption].priceMinor : 0;
  /*
    Fetched once, and only allowed to fill a form the parent has not started.

    A draft that already carries an address is one they typed on this visit; overwriting it with
    the one on file would be the site correcting them.
  */
  const addressLoaded = useRef(false);
  /** What was selected before "a new address" cleared the choice, so cancelling puts it back. */
  const lastChosenAddressId = useRef<string | null>(null);
  useEffect(() => {
    if (!isPrint || addressLoaded.current) return;
    addressLoaded.current = true;
    void (async () => {
      try {
        const addresses = await listAddresses();
        if (!mounted.current || addresses.length === 0) return;
        setSavedAddresses(addresses);
        /*
          The list opens on the default one, and only over a form the parent has not started.

          A draft that already carries an address is one they typed on this visit; replacing it
          with the one on file would be the site correcting them.
        */
        if (draft.shipping.addressLine1.trim() || draft.shipping.recipientName.trim()) return;
        const first = addresses.find((address) => address.isDefault) ?? addresses[0];
        setChosenAddressId(first.id);
        setAddressOpen(false);
        applyAddress(first);
      } catch {
        /* No saved addresses, or no session to ask with. The form opens empty, as before. */
      }
    })();
    /* eslint-disable-next-line react-hooks/exhaustive-deps -- runs once per package, by the ref */
  }, [isPrint]);

  function applyAddress(address: AddressResponse) {
    onChange((prev) => ({
      ...prev,
      shipping: {
        ...prev.shipping,
        recipientName: address.recipientName,
        recipientPhone: address.recipientPhone,
        city: address.city,
        addressLine1: address.addressLine1,
        addressLine2: address.addressLine2 ?? undefined,
        postalCode: address.postalCode ?? undefined,
      },
    }));
  }

  const chooseAddress = (address: AddressResponse) => {
    setChosenAddressId(address.id);
    setShowErrors(false);
    applyAddress(address);
  };

  /* An empty form, because "a new address" that opens holding the last one is the same trap as
     not asking at all. */
  const startNewAddress = () => {
    lastChosenAddressId.current = chosenAddressId;
    setChosenAddressId(null);
    setEditingAddressId(null);
    setAddressOpen(true);
    setShowErrors(false);
    updateShipping({
      recipientName: "",
      recipientPhone: "",
      city: "",
      addressLine1: "",
      addressLine2: undefined,
      postalCode: undefined,
      notes: "",
    });
  };

  const backToSavedAddresses = () => {
    const first =
      savedAddresses.find((address) => address.id === chosenAddressId) ??
      savedAddresses.find((address) => address.id === lastChosenAddressId.current) ??
      savedAddresses.find((address) => address.isDefault) ??
      savedAddresses[0];
    if (!first) return;
    setAddressOpen(false);
    setEditingAddressId(null);
    setShowErrors(false);
    chooseAddress(first);
  };

  /*
    Correcting an address rather than adding one beside it.

    The list could be chosen from and added to, and not changed: a parent who had moved one
    house down, or mistyped a flat number the year before, could only type the whole address
    again - and the book then held the wrong one and the right one side by side, forever. Edit
    opens the same fields, filled from the card, and remembers which row they came from: save
    writes back under that id, so the corrected street replaces the wrong one instead of joining
    it. The fields as the book keeps them use the order's own mapping, so the two never disagree
    about what the city is.
  */
  const savedAddressRequest = (id: string): SaveAddressRequest => {
    const s = draft.shipping;
    const current = savedAddresses.find((address) => address.id === id);
    return {
      id,
      recipientName: s.recipientName.trim(),
      recipientPhone: s.recipientPhone.trim(),
      city: s.city.trim() || s.addressLine1.trim(),
      addressLine1: s.addressLine1.trim(),
      addressLine2: s.addressLine2 || undefined,
      postalCode: s.postalCode || undefined,
      isDefault: current?.isDefault ?? false,
    };
  };

  const startEditAddress = (address: AddressResponse) => {
    lastChosenAddressId.current = chosenAddressId;
    setChosenAddressId(address.id);
    setEditingAddressId(address.id);
    setAddressOpen(true);
    setShowErrors(false);
    setError(null);
    applyAddress(address);
  };

  const saveEditedAddress = async () => {
    if (!editingAddressId || savingAddress) return;
    if (Object.keys(shippingErrors()).length > 0) {
      setShowErrors(true);
      return;
    }
    setSavingAddress(true);
    try {
      const saved = await saveAddress(savedAddressRequest(editingAddressId));
      if (!mounted.current) return;
      setSavedAddresses((list) =>
        list.map((address) => (address.id === saved.id ? saved : address)),
      );
      setEditingAddressId(null);
      setAddressOpen(false);
      chooseAddress(saved);
    } catch (e) {
      if (mounted.current) {
        setError(e instanceof ApiError ? e.message : t.journey.checkout.saveAddressFailed);
      }
    } finally {
      if (mounted.current) setSavingAddress(false);
    }
  };

  const baseMinor =
    (isPrint ? PRICES.print * copies : PRICES.digital) +
    (wantsGiftWrap ? PRICES.giftWrap : 0) +
    deliveryPriceMinor;
  const subtotalMinor = quote?.subtotalMinor ?? baseMinor;
  const discountMinor = quote?.discountMinor ?? 0;
  /*
    The server's figure while it has one, and the local price only until the first quote lands,
    so the summary never shows a wrapping line the order will not carry.
  */
  const giftWrapMinor = quote?.giftWrapMinor ?? (wantsGiftWrap ? PRICES.giftWrap : 0);
  /* The same rule as wrapping: the server's number while there is one, the local one until then. */
  const deliveryMinor = quote?.deliveryMinor ?? deliveryPriceMinor;
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
          deliveryOption,
          city: addressHint.trim() || undefined,
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
    /* The address is in here because it decides the delivery price, and the delivery price is
       part of the total the button carries. Only the resolved option and the zone matter, so
       typing the rest of a street does not re-quote on every keystroke. */
  }, [orderPackage, draft.promoCode, wantsGiftWrap, copies, deliveryOption, inTbilisi]);

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

  /*
    What is wrong, field by field.

    It used to be one message for three fields, and the phone rule only caught an empty box: a
    number typed with a digit missing passed the checkout and failed at the courier, which is
    the most expensive place to find out. Each field now answers for itself, the message sits
    under the field rather than at the foot of the panel, and nothing is marked until the parent
    has pressed the button once - being told a form is wrong before touching it is not help.
  */
  const shippingErrors = (): Partial<Record<keyof ShippingAddressRequest, string>> => {
    if (!isPrint) return {};
    const s = draft.shipping;
    const errors: Partial<Record<keyof ShippingAddressRequest, string>> = {};
    if (!s.recipientName.trim()) {
      errors.recipientName = t.journey.checkout.requiredRecipient;
    }
    if (!s.recipientPhone.trim()) {
      errors.recipientPhone = t.journey.checkout.requiredPhone;
    } else if (!normalizeGeorgianPhone(s.recipientPhone)) {
      errors.recipientPhone = t.journey.checkout.invalidPhone;
    }
    if (s.addressLine1.trim().length < 6) {
      errors.addressLine1 = t.journey.checkout.requiredAddress;
    }
    return errors;
  };

  /* Live once the button has been pressed, so a corrected field clears as it is corrected. */
  const fieldErrors = showErrors ? shippingErrors() : {};

  const placeOrder = async () => {
    // A second click on a slow connection is a second order and a second payment page, so the
    // guard is here rather than only on the button's disabled attribute, which is a rendering
    // detail rather than a promise.
    if (busy) return;

    /*
      An address that was being corrected is written back under its own id as the order goes,
      so the book ends up holding the corrected row and not the wrong one plus a copy. Not
      awaited: the order must not wait on the address book, and a failure here is not a failure
      of the order - the parcel still goes where the fields say.
    */
    if (editingAddressId && Object.keys(shippingErrors()).length === 0) {
      void saveAddress(savedAddressRequest(editingAddressId)).catch(() => undefined);
    }

    const problems = shippingErrors();
    const firstProblem = Object.keys(problems)[0];
    if (firstProblem) {
      setShowErrors(true);
      setError(t.journey.checkout.fixFields);
      /* Taken to the first one rather than left to find it: on a phone the panel and the field
         it is complaining about can be a screen apart. */
      const field = document.querySelector<HTMLElement>(`[name="${firstProblem}"]`);
      field?.scrollIntoView({ behavior: "smooth", block: "center" });
      field?.focus({ preventScroll: true });
      return;
    }
    setShowErrors(false);
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
                and GeorgianDelivery reads it to decide which deliveries this address may have.
                Sending the whole line keeps all three working — the Tbilisi check is a
                substring match, so "თბილისი, გროზნოს 11ა" still resolves to the city, and
                anything unrecognised falls to the regional option, which is the safe direction
                to be wrong in.
              */
              city: draft.shipping.addressLine1.trim(),
              /*
                The option as this screen resolved it, not as the draft happens to hold it.

                They agree — an effect keeps the draft in line with the address — but the money
                is decided here, and sending the same value the total on the button was built
                from means a stale draft cannot quietly buy a different delivery. The server
                resolves it once more against the address regardless.
              */
              deliveryOption,
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
        onChoose={({ address, city }) => updateShipping({ addressLine1: address, city })}
      />
      <div className="checkout-form">
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.checkout.secure}
        </p>
        <h1>{isPrint ? t.journey.checkout.printTitle : t.journey.checkout.title}</h1>

        {/*
          Beki at the head of the form, saying one useful thing.

          He stood in the empty room under the fields; the owner wanted him above them. Above
          them a full figure would push the address under the fold on a laptop, which is the one
          thing a checkout must not do to its form - so he is small, and he earns the row: the
          line beside him points at the field most parents skip and most couriers need. One line,
          no greeting, and (by the stylesheet) none of it on a phone, where the first screen is
          the order. The picture is decoration; the sentence is not, and reads to everyone.
        */}
        {isPrint ? (
          <div className="ux-checkout-guide">
            <img src="/adventrya/beki-canonical.webp" alt="" width={480} height={685} />
            <p>{t.journey.checkout.guideTip}</p>
          </div>
        ) : null}

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
        {isPrint ? (
          <p className="ux-checkout-step">
            {editingAddressId ? t.journey.checkout.editingAddress : t.journey.checkout.stepAddress}
          </p>
        ) : null}
        {/*
          Folded, once there is something to fold.

          A parent buying a second book meets their own address rather than an empty form, and a
          parent who has just typed one does not keep looking at what they typed. Two ways out,
          and they are different things: change this address, or post it somewhere else. The
          second clears the fields, because "a different address" that opens pre-filled with the
          last one is the same trap as not asking at all.
        */}
        {/*
          The ones on file, as a list, with the default already chosen.

          Radios rather than a dropdown: two or three addresses are worth seeing side by side -
          which name, which street, which number - and a select box hides exactly that. The row
          is the target, so the whole card is one tap.
        */}
        {isPrint && !addressOpen ? (
          <div className="ux-address-list">
            {savedAddresses.map((address) => {
              const chosen = address.id === chosenAddressId;
              return (
                <div key={address.id} className={`ux-address-choice${chosen ? " is-on" : ""}`}>
                  <label className="ux-address-pick">
                    <input
                      type="radio"
                      name="savedAddress"
                      checked={chosen}
                      onChange={() => chooseAddress(address)}
                    />
                    <span className="ux-radio" aria-hidden="true" />
                    <span>
                      <strong>{address.addressLine1}</strong>
                      <small>
                        {address.recipientName} · {address.recipientPhone}
                      </small>
                    </span>
                  </label>
                  {/* Outside the label, so pressing it edits the row rather than picking it. */}
                  <button
                    className="ux-address-edit"
                    type="button"
                    aria-label={`${t.journey.checkout.editAddress}: ${address.addressLine1}`}
                    onClick={() => startEditAddress(address)}
                  >
                    <Pencil aria-hidden="true" size={13} />
                    {t.journey.checkout.editAddress}
                  </button>
                </div>
              );
            })}
            <button className="ux-address-add" type="button" onClick={startNewAddress}>
              <Plus aria-hidden="true" size={15} />
              {t.journey.checkout.addNewAddress}
            </button>
          </div>
        ) : null}
        {isPrint && addressOpen ? (
          <div className="ux-ship-fields">
            <label className="field" htmlFor="checkout-ship-recipient">
              <span>{t.journey.checkout.recipient}</span>
              <input
                id="checkout-ship-recipient"
                name="recipientName"
                autoComplete="name"
                aria-invalid={fieldErrors.recipientName ? true : undefined}
                aria-describedby={
                  fieldErrors.recipientName ? "checkout-ship-recipient-error" : undefined
                }
                value={draft.shipping.recipientName}
                onChange={(e) => updateShipping({ recipientName: e.target.value })}
              />
              {fieldErrors.recipientName ? (
                <small id="checkout-ship-recipient-error" className="ux-field-error" role="alert">
                  {fieldErrors.recipientName}
                </small>
              ) : null}
            </label>
            <label className="field" htmlFor="checkout-ship-phone">
              <span>{t.common.labels.phone}</span>
              {/*
                The country code is already in the field, not something to remember to type.

                Every Georgian mobile starts +995 and no parent should be spelling it out - and
                when they did, half of them typed it and half did not, which is how the same
                house ended up on the saved list under two different numbers. The box now holds
                the nine digits it wants, grouped as a Georgian number is read.
              */}
              <span className="ux-tel">
                <b aria-hidden="true">+995</b>
                <input
                  id="checkout-ship-phone"
                  name="recipientPhone"
                  type="tel"
                  inputMode="tel"
                  autoComplete="tel"
                  placeholder="5XX XX XX XX"
                  maxLength={13}
                  aria-invalid={fieldErrors.recipientPhone ? true : undefined}
                  aria-describedby={
                    fieldErrors.recipientPhone ? "checkout-ship-phone-error" : undefined
                  }
                  value={formatGeorgianPhone(draft.shipping.recipientPhone)}
                  onChange={(e) => updateShipping({ recipientPhone: e.target.value })}
                />
              </span>
              {fieldErrors.recipientPhone ? (
                <small id="checkout-ship-phone-error" className="ux-field-error" role="alert">
                  {fieldErrors.recipientPhone}
                </small>
              ) : null}
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
              onChange={(addressLine1) => updateShipping({ addressLine1, city: "" })}
              onChoose={({ address, city }) => updateShipping({ addressLine1: address, city })}
              onPickOnMap={() => setPickingLocation(true)}
              invalid={Boolean(fieldErrors.addressLine1)}
              describedBy={fieldErrors.addressLine1 ? "checkout-ship-address-error" : undefined}
            />
            {fieldErrors.addressLine1 ? (
              <small
                id="checkout-ship-address-error"
                className="ux-field-error field-wide"
                role="alert"
              >
                {fieldErrors.addressLine1}
              </small>
            ) : null}

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
            {/*
              Two ways to close the fields, and they are different things: an address being
              corrected is saved back to its row, or the correction is dropped; a new one is
              simply left, back to the list. The pay button still works over open fields either
              way - the order goes where the fields say.
            */}
            {editingAddressId ? (
              <div className="ux-address-actions field-wide">
                <button
                  className="button button-primary ux-address-save"
                  type="button"
                  disabled={savingAddress}
                  aria-busy={savingAddress}
                  onClick={() => void saveEditedAddress()}
                >
                  {savingAddress ? <BekiLoader size={14} /> : null}
                  {t.journey.checkout.saveAddress}
                </button>
                <button className="ux-inline-link" type="button" onClick={backToSavedAddresses}>
                  {t.journey.checkout.cancelEdit}
                </button>
              </div>
            ) : savedAddresses.length > 0 ? (
              <button
                className="ux-inline-link field-wide"
                type="button"
                onClick={backToSavedAddresses}
              >
                {t.journey.checkout.backToSavedAddresses}
              </button>
            ) : null}
          </div>
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
          {/*
            The two things about the parcel a parent may still change, on the two lines whose
            prices they move.

            They were a pair of cards at the foot of the form — a column away from the total they
            change, and each repeating a line the breakdown already carried. Moving them across as
            cards does not work: measured, the pair asks the rail for about 229px it has not got,
            and the rail is the one column here that may not gain a scrollbar. So they are not
            moved but dissolved — each becomes the row it was duplicating, carrying its control
            where every other row carries its price, at a cost of 58px rather than 229.

            The count on the row is the local one, never the quote's: the stepper is what the
            parent just pressed, and the quote effect drops the old price the moment it moves, so
            the figure beside it is always for the number shown.

            The book on its own. Wrapping and delivery are both inside `subtotalMinor` — the server prices it as part
            of the subtotal so a promo can reach it — so printing the subtotal here and the
            wrapping and the courier again below made the lines add up to more than the total under them.
          */}
          {isPrint ? (
            <span className="ux-summary-option">
              <span>
                <strong>{packageLabel}</strong>
              </span>
              <span className="ux-summary-option-end">
                {/*
                  Minus, the number, plus — the control every delivery app in the country has
                  taught a Georgian parent to read, so it needs no label explaining what it does.
                  The ends stop rather than wrap: at one there is nothing to take away, and five
                  is as many copies as this checkout will take.
                */}
                <span className="ux-copies-stepper">
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
                    onClick={() => onChange({ quantity: Math.min(MAX_PRINT_QUANTITY, copies + 1) })}
                  >
                    <Plus aria-hidden="true" size={16} />
                  </button>
                </span>
                <strong>{formatGel(subtotalMinor - giftWrapMinor - deliveryMinor)}</strong>
              </span>
            </span>
          ) : (
            <span>
              {packageLabel}
              <strong>{formatGel(subtotalMinor - giftWrapMinor - deliveryMinor)}</strong>
            </span>
          )}
          {/*
            Delivery, where the parent can see what it costs them to wait.

            One line when there is nothing to decide - everywhere outside Tbilisi is one price
            in one window - and two selectable ones when there is. The radio is on the left
            because these are alternatives rather than extras: the eye reads down the column of
            circles to compare them, which is not how the switch above works and should not look
            like it.

            Before an address says otherwise this is the regional price. That is the honest
            direction to guess in: recognising Tbilisi drops the total, where guessing free and
            correcting upwards would raise a figure the parent had already accepted.
          */}
          {isPrint && addressStarted && deliveryChoices.length > 1 ? (
            <>
              <p className="ux-delivery-label">{t.journey.checkout.deliveryHeading}</p>
              {deliveryChoices.map((option) => {
                const window = DELIVERY[option];
                const chosen = option === deliveryOption;
                return (
                  <label key={option} className={`ux-delivery-option${chosen ? " is-on" : ""}`}>
                    <input
                      type="radio"
                      name="deliveryOption"
                      value={option}
                      checked={chosen}
                      onChange={() =>
                        onChange((prev) => ({
                          ...prev,
                          shipping: { ...prev.shipping, deliveryOption: option },
                        }))
                      }
                    />
                    <span className="ux-radio" aria-hidden="true" />
                    <span>{t.journey.checkout.deliveryDays(window.minDays)}</span>
                    <strong>
                      {window.priceMinor === 0
                        ? t.journey.checkout.deliveryFree
                        : formatGel(window.priceMinor)}
                    </strong>
                  </label>
                );
              })}
            </>
          ) : null}
          {isPrint && addressStarted && deliveryChoices.length === 1 ? (
            <span>
              {t.journey.checkout.deliveryHeading} ·{" "}
              {t.journey.checkout.deliveryDaysRange(
                DELIVERY.Regional.minDays,
                DELIVERY.Regional.maxDays,
              )}
              <strong>{formatGel(deliveryMinor)}</strong>
            </span>
          ) : null}
          {/*
            A switch, not a tick — but a real checkbox underneath it.

            The control is drawn as the toggle the design asks for and stays an
            `input[type=checkbox]` inside its label, so the keyboard, the screen reader and the
            label association all keep working; only its painting changes.

            The row is here whether or not wrapping is on, because it is now the offer as well
            as the charge: unticked it reads `+5 ₾` and stays out of the total, ticked it is
            the price the server itself returned, on the same line as every other price.
          */}
          {isPrint ? (
            <label className={`ux-summary-option ux-gift-wrap${draft.giftWrap ? " is-on" : ""}`}>
              <span>
                {/* The one row on the card that is an offer rather than a fact, so it is the one
                    drawn as a thing to press: a bordered card with the parcel's own icon. */}
                <span className="ux-wrap-icon" aria-hidden="true">
                  <Gift />
                </span>
                <strong>{t.journey.checkout.giftWrap}</strong>
              </span>
              <span className="ux-summary-option-end">
                <input
                  type="checkbox"
                  name="giftWrap"
                  checked={draft.giftWrap}
                  onChange={(e) => onChange({ giftWrap: e.target.checked })}
                />
                <span className="ux-switch" aria-hidden="true" />
                <strong>
                  {giftWrapMinor > 0 ? formatGel(giftWrapMinor) : `+${formatGel(PRICES.giftWrap)}`}
                </strong>
              </span>
            </label>
          ) : null}
          {discountMinor > 0 ? (
            <span className="ux-discount-line">
              {t.journey.checkout.discountLine}
              {draft.promoCode} <strong>−{formatGel(discountMinor)}</strong>
            </span>
          ) : null}
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
      </aside>

      {error ? <p className="ux-form-error">{error}</p> : null}

      {/*
        The button says what it is doing. Behind it a portrait is uploaded and an order is
        created, which is seconds on a phone connection — and it used to keep its ordinary
        label throughout, so the only sign anything had happened was that the press did
        nothing. aria-busy so it is not only the sighted parent who is told.
      */}
      {/*
        The answer and the action, on one line at the foot of the column.

        They were the last two rows of a rail down the right-hand side: the total set in gold
        at 30px, the button under it. With the rail gone the column has to end somewhere, and
        it ends the way a till does - what it comes to, and the way to pay it, side by side and
        in reach. Sticky, so a long address never scrolls the price out of sight.
      */}
      <div className="ux-checkout-bar">
        <span className="ux-checkout-bar-total">
          <small>{t.journey.checkout.total}</small>
          <strong>{formatGel(totalMinor)}</strong>
        </span>
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
      </div>
      {/*
        No way back from here.

        The button under the pay button was a link to the preview, and it is the one thing on
        this screen that is not the order: a parent one press from the bank was being offered
        somewhere else to go. The browser's own back button still does it for anyone who wants
        it, and losing the link is what lets the column fit a screen without a scrollbar.
      */}
    </section>
  );
}
