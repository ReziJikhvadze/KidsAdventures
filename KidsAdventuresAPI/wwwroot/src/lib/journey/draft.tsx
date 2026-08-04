import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import { useLocation } from "@tanstack/react-router";

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
 * Nothing is ever read back from storage. Every arrival at /create — a new visit, a
 * reload, "add a child" from the dashboard — starts genuinely blank, so a parent never
 * meets the previous child's name, photo or preview, and a new story can always be
 * created from scratch.
 *
 * The draft survives the journey because it is held in a provider above the routes and
 * every step navigates client-side, not because anything is written to the device. A
 * real page load is therefore a real reset, which is exactly the intended behaviour.
 */
function loadDraft(): JourneyDraft {
  if (typeof window === "undefined") return emptyDraft();
  const draft = emptyDraft();

  // Drafts written by earlier versions still sit in localStorage holding a child's name,
  // birth date, photo and shipping address. Nothing reads them now, so clear them out
  // rather than leaving that data on the device indefinitely.
  for (const store of [localStorage, sessionStorage]) {
    try {
      store.removeItem(DRAFT_KEY);
    } catch {
      /* private mode / quota */
    }
  }

  return applyDeepLink(draft, window.location.search);
}

/**
 * Merges the query string into a draft.
 *
 * This has to run on every navigation, not once at mount. The journey now moves
 * client-side, so the provider mounts when the app loads and the world the parent picks
 * on /themes arrives afterwards as ?worldId=… . Reading it only at mount meant the
 * choice was never seen, and the preview stopped with "choose a world first" even
 * though one had been chosen.
 */
function applyDeepLink(base: JourneyDraft, search: string): JourneyDraft {
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(search);
  } catch {
    return base;
  }

  // Entry points that mean "begin a new story" say so in the URL. Without it, arriving
  // from the dashboard is a client-side navigation, the provider is never remounted, and
  // the parent would meet the previous child's details already filled in.
  const draft: JourneyDraft = params.has("new")
    ? emptyDraft()
    : { ...base, characters: [...base.characters] };

  try {
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
    /* a malformed query string must not break the form */
  }

  return draft;
}

export type JourneyDraftValue = [
  JourneyDraft,
  (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void,
  () => void,
  /**
   * False until the URL has been read on the client. Screens that decide something from
   * the draft must wait for this: React runs child effects before parent ones, so a stage
   * mounting inside the provider sees an empty draft on its first pass, before the world
   * from ?world= has been merged in.
   */
  boolean,
];

const JourneyDraftContext = createContext<JourneyDraftValue | null>(null);

/**
 * Holds the draft above the routes.
 *
 * This is what lets the journey keep the parent's answers while they go to /themes and
 * come back, without writing anything to the device: the provider is mounted at the
 * root, so a client-side route change does not unmount it, while a genuine page load
 * builds a fresh one. Storage would have persisted across visits — which is precisely
 * what must not happen.
 */
export function JourneyDraftProvider({ children }: { children: ReactNode }) {
  const [draft, setDraftState] = useState<JourneyDraft>(emptyDraft);
  const [hydrated, setHydrated] = useState(false);

  // Deferred to an effect rather than a useState initialiser because loadDraft reads
  // window.location, which does not exist during server rendering. That delay is why
  // `hydrated` exists: without it a stage would judge an empty draft and, in the case of
  // the preview, refuse to generate a book whose world had in fact been chosen.
  useEffect(() => {
    setDraftState(loadDraft());
    setHydrated(true);
  }, []);

  // Deep-link parameters arrive after mount now that the journey moves client-side: the
  // world the parent picks on /themes comes back as ?worldId=… , and Stripe returns with
  // ?orderId=… . Merging on every location change is what makes those choices land —
  // reading them once at mount silently dropped them.
  const location = useLocation();
  useEffect(() => {
    setDraftState((prev) => applyDeepLink(prev, location.searchStr ?? ""));
  }, [location.searchStr]);

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

  const value = useMemo<JourneyDraftValue>(
    () => [draft, setDraft, resetDraft, hydrated],
    [draft, setDraft, resetDraft, hydrated],
  );

  return <JourneyDraftContext.Provider value={value}>{children}</JourneyDraftContext.Provider>;
}

export function useJourneyDraft(): JourneyDraftValue {
  const value = useContext(JourneyDraftContext);
  if (!value) {
    throw new Error("useJourneyDraft must be used inside JourneyDraftProvider.");
  }

  return value;
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
