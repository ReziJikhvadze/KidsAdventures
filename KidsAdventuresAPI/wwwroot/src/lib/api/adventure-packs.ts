import { ApiError, apiRequest, getApiBaseUrl, getToken } from "./client";
import type {
  AdventurePackDetailResponse,
  AdventurePackResponse,
  AdventurePackStatus,
  MasterStoryRunStatus,
  ThemeType,
} from "./types";

export type GenerateAdventurePackOptions = {
  optionalStoryNotes?: string;
  storyLanguage?: string;
};

export type GuestPreviewResult = {
  title: string;
  childName: string;
  firstPageTitle: string;
  firstPageText: string;
  coverImageDataUrl: string;
  theme: ThemeType;
  /** Server-side id of this teaser; replayed at sign-up so the welcome gift is trustable. */
  guestPreviewId: string;
  /** Identity of the generated story; fallback entitlement link. */
  storyId: string;
  /** Full story content, replayed into the account after sign-in. */
  storyJson: string;
};

export type GuestPreviewInput = {
  name: string;
  age: number;
  gender?: string;
  theme: ThemeType;
  storyLanguage?: string;
  optionalStoryNotes?: string;
  photo?: File | null;
};

/** Free, no-login teaser. Writes the full story + a cover image inline (~40-80s). */
export async function generateGuestPreview(input: GuestPreviewInput): Promise<GuestPreviewResult> {
  const body = new FormData();
  body.append("name", input.name);
  body.append("age", String(input.age));
  body.append("theme", input.theme);
  if (input.storyLanguage) body.append("storyLanguage", input.storyLanguage);
  if (input.optionalStoryNotes?.trim())
    body.append("optionalStoryNotes", input.optionalStoryNotes.trim());
  if (input.photo) body.append("photo", input.photo);

  const response = await fetch(`${getApiBaseUrl()}/api/adventure-packs/guest-preview`, {
    method: "POST",
    body,
  });

  if (!response.ok) {
    let message = "We couldn't create your preview. Please try again.";
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      /* ignore */
    }
    throw new ApiError(message, response.status);
  }

  return (await response.json()) as GuestPreviewResult;
}

export type StartGuestPreviewInput = {
  name: string;
  age: number;
  gender?: string;
  eyeColor?: string;
  /** As entered, yyyy-mm-dd. The server derives the age from it rather than trusting ours. */
  birthDate?: string;
  theme: ThemeType;
  storyLanguage?: string;
  optionalStoryNotes?: string;
  photo?: File | null;
};

/**
 * Starts a whole sixteen-page book and returns an id to watch.
 *
 * The book is written by a background job rather than inside this request: it takes minutes,
 * and Azure closes an inbound request at 230 seconds, so waiting on it here could only ever
 * have timed out.
 */
export async function startGuestPreview(input: StartGuestPreviewInput): Promise<{ runId: string }> {
  const body = new FormData();
  body.append("name", input.name);
  body.append("age", String(input.age));
  body.append("theme", input.theme);
  // Both of these are chosen on the profile screen and neither used to reach the story. Gender
  // was accepted by the old caller and then dropped before the request was built, which is why
  // picking a girl could still produce a book about a boy.
  if (input.gender) body.append("gender", input.gender);
  if (input.eyeColor) body.append("eyeColor", input.eyeColor);
  if (input.birthDate) body.append("birthDate", input.birthDate);
  if (input.storyLanguage) body.append("storyLanguage", input.storyLanguage);
  if (input.optionalStoryNotes?.trim())
    body.append("optionalStoryNotes", input.optionalStoryNotes.trim());
  if (input.photo) body.append("photo", input.photo);

  const response = await fetch(`${getApiBaseUrl()}/api/adventure-packs/guest-preview/start`, {
    method: "POST",
    body,
  });

  if (!response.ok) {
    let message = "We couldn't start your story. Please try again.";
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      /* ignore */
    }
    throw new ApiError(message, response.status);
  }

  return (await response.json()) as { runId: string };
}

/** One poll. Cheap enough to run every few seconds while a book is being written. */
export async function getGuestPreviewStatus(runId: string): Promise<MasterStoryRunStatus> {
  const response = await fetch(`${getApiBaseUrl()}/api/adventure-packs/guest-preview/${runId}`);

  if (!response.ok) {
    let message = "We lost track of your story. Please try again.";
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      /* ignore */
    }
    throw new ApiError(message, response.status);
  }

  return (await response.json()) as MasterStoryRunStatus;
}

/** Saves a teaser story (created while logged out) to the now signed-in parent's account. */
export async function importGuestStory(input: {
  childId: string;
  theme: ThemeType;
  storyJson: string;
  storyLanguage?: string;
  optionalStoryNotes?: string;
}): Promise<{ id: string }> {
  return apiRequest<{ id: string }>("/api/adventure-packs/import-guest", {
    method: "POST",
    body: JSON.stringify({
      childId: input.childId,
      theme: input.theme,
      storyJson: input.storyJson,
      storyLanguage: input.storyLanguage || "en",
      optionalStoryNotes: input.optionalStoryNotes?.trim() || undefined,
    }),
  });
}

export async function generateAdventurePack(
  childId: string,
  theme: ThemeType,
  options?: GenerateAdventurePackOptions,
): Promise<{ id: string; status: string; welcomeStoryRemaining?: number }> {
  return apiRequest<{ id: string; status: string; welcomeStoryRemaining?: number }>(
    "/api/adventure-packs/generate",
    {
      method: "POST",
      body: JSON.stringify({
        childId,
        theme,
        optionalStoryNotes: options?.optionalStoryNotes?.trim() || undefined,
        storyLanguage: options?.storyLanguage || "en",
      }),
    },
  );
}

/** Spends one $4.99 book credit and starts illustrating an existing, text-ready pack. */
export async function illustrateAdventurePack(packId: string): Promise<{
  id: string;
  status: string;
  previewIllustrationStatus?: string;
  bookCredits?: number;
}> {
  return apiRequest<{
    id: string;
    status: string;
    previewIllustrationStatus?: string;
    bookCredits?: number;
  }>(`/api/adventure-packs/${packId}/illustrate`, {
    method: "POST",
  });
}

export async function generatePackPdf(
  packId: string,
): Promise<{ id: string; status: string; bookCredits?: number; usesSlideshowImages?: boolean }> {
  return apiRequest<{
    id: string;
    status: string;
    bookCredits?: number;
    usesSlideshowImages?: boolean;
  }>(`/api/adventure-packs/${packId}/generate-pdf`, {
    method: "POST",
  });
}

/**
 * The parent opened this book. Recorded on the book rather than in this browser, so a book read
 * on a laptop is not offered as unread on a phone.
 */
export async function markPackRead(packId: string): Promise<void> {
  await apiRequest<void>(`/api/adventure-packs/${packId}/read`, { method: "POST" });
}

export async function listAdventurePacks(): Promise<AdventurePackResponse[]> {
  return apiRequest<AdventurePackResponse[]>("/api/adventure-packs");
}

export async function getAdventurePack(id: string): Promise<AdventurePackDetailResponse> {
  return apiRequest<AdventurePackDetailResponse>(`/api/adventure-packs/${id}`);
}

/**
 * The poll window ran out while the book was still healthy. Not a failure: the caller
 * shows "still preparing", never the failed-book copy.
 */
export class PackStillWorkingError extends Error {
  constructor() {
    super("PDF ჯერ მზადდება.");
    this.name = "PackStillWorkingError";
  }
}

export function getDownloadUrl(packId: string): string {
  return `${getApiBaseUrl()}/api/adventure-packs/${packId}/download`;
}

/** Which spreads of a book being generated already exist, with the job's own progress. */
export async function getMakingOf(packId: string): Promise<{
  progressMessage: string | null;
  progressPercent: number | null;
  spreads: number[];
  status: string;
}> {
  return apiRequest(`/api/adventure-packs/${packId}/making-of`);
}

/** The path fetchIllustrationObjectUrl expects for a making-of spread. */
export function makingOfImagePath(packId: string, spread: number): string {
  return `/api/adventure-packs/${packId}/making-of/${spread}`;
}

export async function fetchIllustrationObjectUrl(illustrationPath: string): Promise<string> {
  const token = getToken();
  const response = await fetch(`${getApiBaseUrl()}${illustrationPath}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError("Could not load illustration.", response.status);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export async function downloadAdventurePack(
  packId: string,
  fileName = "storybook.pdf",
): Promise<void> {
  const token = getToken();
  const response = await fetch(getDownloadUrl(packId), {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    let message = "Could not download PDF.";
    try {
      const text = await response.text();
      if (text) message = text;
    } catch {
      /* ignore */
    }
    throw new ApiError(message, response.status);
  }

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement("a");
    anchor.href = objectUrl;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

/** Reads ~NN% from server progress messages when present. */
export function parseProgressPercent(message: string | null | undefined): number | null {
  if (!message) return null;
  const match = message.match(/~?(\d{1,3})%/);
  if (!match) return null;
  return Math.min(100, Math.max(0, parseInt(match[1], 10)));
}

function parseIllustrationPageProgress(message: string | null | undefined): number | null {
  if (!message) return null;
  const match = message.match(/page\s+(\d+)\s+of\s+(\d+)/i);
  if (!match) return null;
  const current = parseInt(match[1], 10);
  const total = parseInt(match[2], 10);
  if (!total) return null;
  return Math.min(95, Math.round(35 + (current / total) * 60));
}

/** True once the story TEXT exists (free preview), regardless of whether illustrations are unlocked yet. */
export function isStoryTextReady(pack: AdventurePackDetailResponse): boolean {
  if (
    pack.status !== "StoryReady" &&
    pack.status !== "GeneratingPdf" &&
    pack.status !== "Completed"
  ) {
    return false;
  }
  return (pack.storyPages?.length ?? 0) > 0;
}

/**
 * True once every page that is meant to carry art has it.
 *
 * Half of a spread book is prose facing an illustration and never gets one of its own, so
 * `every(isIllustrated)` was permanently false there — which disabled the PDF button on books
 * that were completely finished.
 */
export function isPackFullyIllustrated(pack: AdventurePackDetailResponse): boolean {
  const pages = pack.storyPages ?? [];
  const illustratable = pages.filter((p) => !p.isTextOnlyPage);
  return illustratable.length > 0 && illustratable.every((p) => p.isIllustrated);
}

/** Welcome-gift stories can export a preview PDF after the free illustrated page; full books need every page illustrated. */
export function canExportPackPdf(pack: AdventurePackDetailResponse): boolean {
  if (pack.status !== "StoryReady" && pack.status !== "Completed") return false;
  const illustrated = countIllustratedPages(pack);
  if (pack.isWelcomeGiftStory) {
    return illustrated >= 1;
  }
  return isPackFullyIllustrated(pack);
}

/** Number of pages that already have an illustration. */
export function countIllustratedPages(pack: AdventurePackDetailResponse): number {
  return (pack.storyPages ?? []).filter((p) => p.isIllustrated).length;
}

export function isPackReadable(pack: AdventurePackDetailResponse): boolean {
  if (pack.status === "Failed") return false;
  if (pack.status === "Completed") {
    return (pack.storyPages?.length ?? 0) > 0;
  }
  if (pack.status !== "StoryReady") return false;
  const pages = pack.storyPages ?? [];
  return pages.length > 0 && pages.every((p) => p.isIllustrated);
}

/** Actively painting pages after a paid unlock (not just text-ready awaiting unlock). */
export function isPackIllustrating(pack: AdventurePackDetailResponse): boolean {
  return (
    pack.status === "StoryReady" &&
    pack.previewIllustrationStatus === "Generating" &&
    !isPackReadable(pack)
  );
}

/** Text is ready but illustrations have not been unlocked/paid for yet. */
export function isAwaitingIllustrationUnlock(pack: AdventurePackDetailResponse): boolean {
  return (
    pack.status === "StoryReady" &&
    !isPackReadable(pack) &&
    pack.previewIllustrationStatus !== "Generating"
  );
}

export function computePackProgressPercent(pack: AdventurePackDetailResponse): number {
  if (pack.status === "Completed") return 100;
  if (pack.status === "Failed") return 0;

  const parsed = parseProgressPercent(pack.progressMessage);
  if (parsed !== null) return parsed;

  const illustrationProgress = parseIllustrationPageProgress(pack.progressMessage);
  if (illustrationProgress !== null) return illustrationProgress;

  if (pack.status === "StoryReady") {
    const pages = pack.storyPages ?? [];
    const total = pack.storyPageCount ?? (pages.length || 6);
    const done = pages.filter((p) => p.isIllustrated).length;
    if (total > 0) return Math.min(95, Math.round(35 + (done / total) * 60));
    return 40;
  }

  if (pack.status === "GeneratingPdf") return 92;
  if (pack.status === "GeneratingStory" || pack.status === "Generating") return 22;
  if (pack.status === "Pending") return 8;
  return 15;
}

const IN_PROGRESS_STATUSES: AdventurePackStatus[] = [
  "Pending",
  "Generating",
  "GeneratingStory",
  "GeneratingPdf",
];

export function isPackInProgress(status: AdventurePackStatus): boolean {
  return IN_PROGRESS_STATUSES.includes(status);
}

export function isPackGenerating(pack: AdventurePackDetailResponse): boolean {
  return isPackInProgress(pack.status) || isPackIllustrating(pack);
}

export async function pollAdventurePack(
  id: string,
  onProgress?: (pack: AdventurePackDetailResponse) => void,
  options?: {
    intervalMs?: number;
    maxAttempts?: number;
    untilStatus?: AdventurePackStatus;
    untilReadable?: boolean;
    untilStoryText?: boolean;
    untilPagesIllustrated?: number;
    /**
     * Wait for the PDF file itself. A pack can sit at "Completed" with no pdfUrl, so waiting on
     * the status returns instantly and the download that follows 404s.
     */
    untilPdfReady?: boolean;
  },
): Promise<AdventurePackDetailResponse> {
  const intervalMs = options?.intervalMs ?? 2000;
  const maxAttempts = options?.maxAttempts ?? 240;
  const untilStatus = options?.untilStatus ?? "Completed";
  const untilReadable = options?.untilReadable ?? false;
  const untilStoryText = options?.untilStoryText ?? false;
  const untilPagesIllustrated = options?.untilPagesIllustrated ?? 0;
  const untilPdfReady = options?.untilPdfReady ?? false;

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const pack = await getAdventurePack(id);
    onProgress?.(pack);

    if (untilPdfReady) {
      if (pack.pdfUrl) return pack;
      // A failed PDF puts the book back to StoryReady and records why, so the "Failed" check
      // below never fires. Without this the caller waits out every attempt in silence.
      if (pack.status === "StoryReady" && pack.errorMessage) {
        throw new Error(pack.errorMessage);
      }
    } else if (untilPagesIllustrated > 0) {
      if (isPackReadable(pack) || countIllustratedPages(pack) >= untilPagesIllustrated) return pack;
    } else if (untilStoryText) {
      if (isStoryTextReady(pack)) return pack;
    } else if (untilReadable) {
      if (isPackReadable(pack)) return pack;
    } else if (pack.status === untilStatus) {
      return pack;
    }

    if (pack.status === "Failed") {
      throw new Error("წიგნი ვერ შეიქმნა.");
    }

    await new Promise((r) => setTimeout(r, intervalMs));
  }

  throw new PackStillWorkingError();
}
