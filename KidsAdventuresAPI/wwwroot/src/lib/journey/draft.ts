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
 * Builds the starting draft for a visit. Nothing is restored from storage: every visit to
 * /create begins blank, so a parent never opens the form onto the previous child's name,
 * photo or generated preview, and can create as many previews as they like.
 *
 * The URL is the only thing that carries state into the journey. That is deliberate —
 * it survives the Stripe round trip without keeping personal data on the device.
 */
function loadDraft(): JourneyDraft {
  if (typeof window === "undefined") return emptyDraft();
  const draft = emptyDraft();

  // Drafts written by earlier versions are still sitting in visitors' browsers, holding a
  // child's name, birth date, photo and shipping address. Nothing reads them now, so remove
  // them on the next visit rather than leaving that data on the device indefinitely.
  try {
    localStorage.removeItem(DRAFT_KEY);
  } catch {
    /* private mode / quota */
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

/**
 * The draft lives in memory for the length of the visit and is never written anywhere.
 * A reload starts a fresh book, which is the point: the form should not reopen onto the
 * previous child, and a preview should never be the one generated last time.
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

  // Signing out on a shared device must not leave the previous parent's child on screen.
  useEffect(() => {
    const onCleared = () => setDraftState(emptyDraft());
    window.addEventListener(SESSION_CLEARED_EVENT, onCleared);
    return () => window.removeEventListener(SESSION_CLEARED_EVENT, onCleared);
  }, []);

  const setDraft = useCallback(
    (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) =>
      setDraftState((prev) => (typeof patch === "function" ? patch(prev) : { ...prev, ...patch })),
    [],
  );

  const resetDraft = useCallback(() => setDraftState(emptyDraft()), []);

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
