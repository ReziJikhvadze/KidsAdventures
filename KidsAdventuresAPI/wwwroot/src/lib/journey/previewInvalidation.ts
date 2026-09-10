import type { JourneyDraft } from "./draft";

export function invalidateChangedPreview(prev: JourneyDraft, next: JourneyDraft): JourneyDraft {
  if (next.preview !== prev.preview) return next;
  const before = prev.characters.find(c => c.isPrimary)!, after = next.characters.find(c => c.isPrimary)!;
  const sameVisual = prev.worldId === next.worldId && before.birthDate === after.birthDate
    && before.gender === after.gender && before.photoDataUrl === after.photoDataUrl
    && (!before.serverId || !after.serverId || before.serverId === after.serverId)
    && (!before.portraitRunId || !after.portraitRunId || before.portraitRunId === after.portraitRunId);
  if (sameVisual && before.name === after.name) return next;
  if (!prev.preview) return sameVisual ? next : { ...next, reusePreviewId: undefined };
  return { ...next, preview: null,
    reusePreviewId: sameVisual && prev.preview.coverRevisionId ? prev.preview.storyId : undefined };
}
