import { THEME_ID_TO_API, type MasterStoryRunStatus } from "../api/types.ts";
import type { JourneyDraft } from "./draft";
import type { WorldId } from "../worlds";

/** Restore the book the server actually wrote; never silently substitute a default world. */
export function readyPreviewPatch(
  draft: JourneyDraft,
  status: MasterStoryRunStatus,
  pointer: { worldId?: string | null; characterId?: string } = {},
): Pick<JourneyDraft, "worldId" | "characters" | "preview"> {
  if (status.status !== "Ready") throw new Error("The preview is not ready.");
  const world = [status.worldId, pointer.worldId, draft.worldId].find(
    (value): value is WorldId => typeof value === "string" && Object.hasOwn(THEME_ID_TO_API, value),
  );
  if (!world) throw new Error("The saved preview has no valid world.");

  return {
    worldId: world,
    preview: {
      guestPreviewId: status.runId,
      storyId: status.runId,
      worldId: world,
      title: status.title || "",
      firstPageTitle: status.firstPageTitle || "",
      firstPageText: status.firstPageText || "",
      coverImageDataUrl: status.coverImageUrl || "",
      pageCount: status.pageCount,
    },
    characters: draft.characters.map((child) => {
      if (!child.isPrimary) return child;
      const serverId = child.serverId || pointer.characterId;
      return {
        ...child,
        serverId,
        name: child.name.trim() ? child.name : status.childName || "",
        birthDate: child.birthDate || status.birthDate || "",
        gender:
          child.gender ||
          (status.gender === "girl" || status.gender === "boy" ? status.gender : null),
        photoReady: child.photoReady || status.hasPortrait === true,
        // Existing local photos stay local; a resumed blank tab copies the parked portrait
        // server-side at character creation instead of asking for another upload or generation.
        portraitRunId:
          child.portraitRunId ||
          (!serverId && !child.photoDataUrl && status.hasPortrait ? status.runId : undefined),
      };
    }),
  };
}
