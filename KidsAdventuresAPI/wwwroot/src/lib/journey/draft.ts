import { useCallback, useEffect, useState } from "react";

import type { BookLanguage } from "@/lib/i18n";
import { DEFAULT_BOOK_LANGUAGE } from "@/lib/i18n";
import type { BookPackage } from "@/lib/pricing";
import { SESSION_CLEARED_EVENT, SESSION_KEYS } from "@/lib/storage/session";
import { isWorldId, type WorldId } from "@/lib/worlds";
import type {
  CharacterGender,
  CharacterType,
  EyeColor,
  ShippingAddressRequest,
} from "@/lib/api/types";

const DRAFT_KEY = SESSION_KEYS.journeyDraft;

export type DraftCharacter = {
  localId: string;
  serverId?: string;
  name: string;
  birthDate: string;
  gender: CharacterGender | null;
  eyeColor: EyeColor | null;
  characterType: CharacterType;
  relationship: string;
  customRelationship: string;
  isPrimary: boolean;
  photoDataUrl: string | null;
  photoReady: boolean;
};

export type PreviewTeaser = {
  guestPreviewId: string;
  storyId: string;
  title: string;
  firstPageTitle: string;
  firstPageText: string;
  coverImageDataUrl: string;
  storyJson?: string;
};

export type JourneyDraft = {
  characters: DraftCharacter[];
  bookLanguage: BookLanguage;
  worldId: WorldId | null;
  storyNotes: string;
  bookPackage: BookPackage;
  preview: PreviewTeaser | null;
  orderId: string | null;
  bookId: string | null;
  /** Prior book when continuing an adventure from map / QR. */
  continuesFromBookId: string | null;
  promoCode: string;
  shipping: ShippingAddressRequest;
};

export function newLocalId(): string {
  return `local-${crypto.randomUUID()}`;
}

export function emptyCharacter(isPrimary: boolean): DraftCharacter {
  return {
    localId: newLocalId(),
    name: "",
    birthDate: "",
    gender: null,
    eyeColor: null,
    // The type picker was removed from the profile form; everyone drafted here is a
    // child. Characters synced back from the server keep whatever type they were saved
    // with, which is why the field stays on the draft rather than being dropped.
    characterType: "child",
    relationship: "",
    customRelationship: "",
    isPrimary,
    photoDataUrl: null,
    photoReady: false,
  };
}

export function emptyDraft(): JourneyDraft {
  return {
    characters: [emptyCharacter(true)],
    bookLanguage: DEFAULT_BOOK_LANGUAGE,
    worldId: null,
    storyNotes: "",
    bookPackage: "digital",
    preview: null,
    orderId: null,
    bookId: null,
    continuesFromBookId: null,
    promoCode: "",
    shipping: {
      recipientName: "",
      recipientPhone: "",
      city: "",
      addressLine1: "",
      saveForLater: true,
    },
  };
}

/**
 * Builds the starting draft for a visit.
 *
 * The draft lives in sessionStorage, not localStorage. That distinction is the whole
 * design: sessionStorage dies with the browser tab, so a new visit still opens a blank
 * form and the next person on a shared device never sees the previous child — while the
 * journey survives the one thing that would otherwise destroy it.
 *
 * That thing is real: choosing a world leaves /create entirely
 * (`window.location.assign("/themes")`) and comes back as a fresh page load. With the
 * draft held only in React state, the name, birth date and photo the parent had just
 * typed were gone by the time they returned, and every later screen fell back to
 * "პატარა გმირი" with no child information at all.
 */
function loadDraft(): JourneyDraft {
  if (typeof window === "undefined") return emptyDraft();
  let draft = emptyDraft();

  // Drafts written by earlier versions sit in localStorage holding a child's name, birth
  // date, photo and shipping address. Nothing reads them now, so clear them out rather
  // than leaving that data on the device indefinitely.
  try {
    localStorage.removeItem(DRAFT_KEY);
  } catch {
    /* private mode / quota */
  }

  try {
    const raw = sessionStorage.getItem(DRAFT_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<JourneyDraft>;
      const base = emptyDraft();
      draft = {
        ...base,
        ...parsed,
        characters:
          Array.isArray(parsed.characters) && parsed.characters.length > 0
            ? parsed.characters
            : base.characters,
        shipping: { ...base.shipping, ...parsed.shipping },
      };
    }
  } catch {
    // A corrupt draft must not strand the parent on a broken form.
    draft = emptyDraft();
  }

  // Deep-link query carries continue / world selection, and the order id Stripe returns with.
  try {
    const params = new URLSearchParams(window.location.search);
    const world = params.get("worldId") || params.get("world");
    if (world && isWorldId(world)) draft.worldId = world;
    const fromBook = params.get("continuesFromBookId");
    if (fromBook) draft.continuesFromBookId = fromBook;

    // Stripe returns to /create?orderId=… . This is what resumes a paid order now that the
    // draft is not persisted: without it a parent would come back from checkout to a blank
    // form with no sign of the book they just paid for.
    const orderId = params.get("orderId");
    if (orderId) draft.orderId = orderId;
    const characterId = params.get("characterId");
    if (characterId) {
      draft.characters = draft.characters.map((c) =>
        c.isPrimary ? { ...c, serverId: c.serverId || characterId } : c,
      );
    }

    // Carry-forward cast from the adventure map: primary first, then up to two
    // supporting server ids. Names are filled later when ProfileStage syncs.
    const characterIdsRaw = params.get("characterIds");
    if (characterIdsRaw) {
      const ids = characterIdsRaw
        .split(",")
        .map((id) => id.trim())
        .filter(Boolean);
      if (ids.length > 0) {
        const primaryId = characterId || ids[0];
        const supportingIds = ids.filter((id) => id !== primaryId).slice(0, 2);
        const next: DraftCharacter[] = [
          {
            ...emptyCharacter(true),
            serverId: primaryId,
            name: draft.characters.find((c) => c.isPrimary)?.name ?? "",
          },
          ...supportingIds.map((id) => ({
            ...emptyCharacter(false),
            serverId: id,
            relationship: "მეგობარი",
          })),
        ];
        draft.characters = next;
      }
    }
  } catch {
    /* ignore */
  }

  return draft;
}

function persistDraft(draft: JourneyDraft) {
  if (typeof window === "undefined") return;
  try {
    sessionStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
  } catch {
    /* quota / private mode — losing persistence is better than breaking the form */
  }
}

/**
 * The draft is scoped to the browser tab: it survives the /themes round trip and a
 * reload, and disappears when the tab closes. A returning visitor therefore always
 * starts a fresh book, and can generate as many previews as they like.
 */
export function useJourneyDraft(): [
  JourneyDraft,
  (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void,
  () => void,
] {
  const [draft, setDraftState] = useState<JourneyDraft>(emptyDraft);

  // Deferred to an effect rather than a useState initialiser because it reads
  // window.location, which does not exist during server rendering.
  useEffect(() => {
    setDraftState(loadDraft());
  }, []);

  // Signing out on a shared device must not leave the previous parent's child on screen,
  // in memory or in the tab's storage.
  useEffect(() => {
    const onCleared = () => {
      try {
        sessionStorage.removeItem(DRAFT_KEY);
      } catch {
        /* ignore */
      }
      setDraftState(emptyDraft());
    };
    window.addEventListener(SESSION_CLEARED_EVENT, onCleared);
    return () => window.removeEventListener(SESSION_CLEARED_EVENT, onCleared);
  }, []);

  const setDraft = useCallback(
    (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) =>
      setDraftState((prev) => {
        const next = typeof patch === "function" ? patch(prev) : { ...prev, ...patch };
        persistDraft(next);
        return next;
      }),
    [],
  );

  const resetDraft = useCallback(() => {
    try {
      sessionStorage.removeItem(DRAFT_KEY);
    } catch {
      /* ignore */
    }
    setDraftState(emptyDraft());
  }, []);

  return [draft, setDraft, resetDraft];
}

export function primaryCharacter(draft: JourneyDraft): DraftCharacter {
  return draft.characters.find((c) => c.isPrimary) ?? draft.characters[0];
}

export function supportingCharacters(draft: JourneyDraft): DraftCharacter[] {
  return draft.characters.filter((c) => !c.isPrimary);
}

export function ageFromBirthDate(birthDate: string): number {
  if (!birthDate) return 5;
  const born = new Date(birthDate);
  if (Number.isNaN(born.getTime())) return 5;
  const today = new Date();
  let age = today.getFullYear() - born.getFullYear();
  const m = today.getMonth() - born.getMonth();
  if (m < 0 || (m === 0 && today.getDate() < born.getDate())) age -= 1;
  return Math.max(1, Math.min(16, age));
}

export function resolvedRelationship(character: DraftCharacter): string {
  if (character.relationship === "სხვა") return character.customRelationship.trim();
  return character.relationship.trim();
}
