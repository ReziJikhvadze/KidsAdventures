import { apiRequest } from "./client";
import type { AdventurePackResponse, ThemeType } from "./types";

export type GenerateAdventurePackOptions = {
  optionalStoryNotes?: string;
  storyLanguage?: string;
};

export async function generateAdventurePack(
  childId: string,
  theme: ThemeType,
  options?: GenerateAdventurePackOptions,
): Promise<{ id: string; status: string }> {
  return apiRequest<{ id: string; status: string }>("/api/adventure-packs/generate", {
    method: "POST",
    body: JSON.stringify({
      childId,
      theme,
      optionalStoryNotes: options?.optionalStoryNotes?.trim() || undefined,
      storyLanguage: options?.storyLanguage || "en",
    }),
  });
}

export async function listAdventurePacks(): Promise<AdventurePackResponse[]> {
  return apiRequest<AdventurePackResponse[]>("/api/adventure-packs");
}

export async function getAdventurePack(id: string): Promise<AdventurePackResponse> {
  return apiRequest<AdventurePackResponse>(`/api/adventure-packs/${id}`);
}

export function getDownloadUrl(packId: string): string {
  const base = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? "";
  return `${base}/api/adventure-packs/${packId}/download`;
}

/** Reads ~NN% from server progress messages when present. */
export function parseProgressPercent(message: string | null | undefined): number | null {
  if (!message) return null;
  const match = message.match(/~?(\d{1,3})%/);
  if (!match) return null;
  return Math.min(100, Math.max(0, parseInt(match[1], 10)));
}

export async function pollAdventurePack(
  id: string,
  onProgress?: (pack: AdventurePackResponse) => void,
  intervalMs = 2000,
  maxAttempts = 240,
): Promise<AdventurePackResponse> {
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const pack = await getAdventurePack(id);
    onProgress?.(pack);

    if (pack.status === "Completed") return pack;
    if (pack.status === "Failed") {
      throw new Error(
        pack.progressMessage ?? "Adventure pack generation failed. Please try again.",
      );
    }

    await new Promise((r) => setTimeout(r, intervalMs));
  }

  throw new Error(
    "Still working — your pack is saved in My Packs. Refresh there in a few minutes.",
  );
}
