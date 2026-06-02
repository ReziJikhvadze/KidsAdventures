import { apiRequest } from "./client";
import type { AdventurePackResponse, ThemeType } from "./types";

export async function generateAdventurePack(
  childId: string,
  theme: ThemeType,
): Promise<{ id: string; status: string }> {
  return apiRequest<{ id: string; status: string }>("/api/adventure-packs/generate", {
    method: "POST",
    body: JSON.stringify({ childId, theme }),
  });
}

export async function getAdventurePack(id: string): Promise<AdventurePackResponse> {
  return apiRequest<AdventurePackResponse>(`/api/adventure-packs/${id}`);
}

export function getDownloadUrl(packId: string): string {
  const base = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") ?? "";
  return `${base}/api/adventure-packs/${packId}/download`;
}

export async function pollAdventurePack(
  id: string,
  onProgress?: (pack: AdventurePackResponse) => void,
  intervalMs = 2000,
  maxAttempts = 90,
): Promise<AdventurePackResponse> {
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const pack = await getAdventurePack(id);
    onProgress?.(pack);

    if (pack.status === "Completed") return pack;
    if (pack.status === "Failed") {
      throw new Error("Adventure pack generation failed. Please try again.");
    }

    await new Promise((r) => setTimeout(r, intervalMs));
  }

  throw new Error("Generation is taking longer than expected. Check back in My Packs.");
}
