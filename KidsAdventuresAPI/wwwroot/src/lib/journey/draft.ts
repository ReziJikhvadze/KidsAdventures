import { useCallback, useEffect, useState } from "react";

import type { BookLanguage } from "@/lib/i18n";
import { DEFAULT_BOOK_LANGUAGE } from "@/lib/i18n";
import type { BookPackage } from "@/lib/pricing";
import { isWorldId, type WorldId } from "@/lib/worlds";
import type {
  CharacterGender,
  CharacterType,
  EyeColor,
  ShippingAddressRequest,
} from "@/lib/api/types";

const DRAFT_KEY = "adventrya-create-draft-v1";

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
    characterType: isPrimary ? "child" : "child",
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

function loadDraft(): JourneyDraft {
  if (typeof window === "undefined") return emptyDraft();
  let draft = emptyDraft();
  try {
    const raw = localStorage.getItem(DRAFT_KEY);
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
        continuesFromBookId: parsed.continuesFromBookId ?? base.continuesFromBookId,
      };
    }
  } catch {
    draft = emptyDraft();
  }

  // Deep-link query wins for continue / world selection when present.
  try {
    const params = new URLSearchParams(window.location.search);
    const world = params.get("worldId") || params.get("world");
    if (world && isWorldId(world)) draft.worldId = world;
    const fromBook = params.get("continuesFromBookId");
    if (fromBook) draft.continuesFromBookId = fromBook;
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
    localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
  } catch {
    /* quota / private mode */
  }
}

export function useJourneyDraft(): [
  JourneyDraft,
  (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void,
  () => void,
] {
  const [draft, setDraftState] = useState<JourneyDraft>(emptyDraft);

  useEffect(() => {
    setDraftState(loadDraft());
  }, []);

  const setDraft = useCallback(
    (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => {
      setDraftState((prev) => {
        const next = typeof patch === "function" ? patch(prev) : { ...prev, ...patch };
        persistDraft(next);
        return next;
      });
    },
    [],
  );

  const resetDraft = useCallback(() => {
    const next = emptyDraft();
    persistDraft(next);
    setDraftState(next);
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
