import { strict as assert } from "node:assert";
import { test } from "node:test";
import { invalidateChangedPreview } from "../src/lib/journey/previewInvalidation.ts";
import type { JourneyDraft } from "../src/lib/journey/draft";
const draft = () =>
  ({
    worldId: "space",
    preview: { storyId: "preview-1", coverRevisionId: "revision-1" },
    characters: [
      {
        isPrimary: true,
        name: "ნინა",
        birthDate: "2021-01-01",
        gender: "girl",
        photoDataUrl: "photo-1",
      },
    ],
  }) as JourneyDraft;
test("name-only edit starts a new revision using the prior cover base", () => {
  const before = draft();
  const next = { ...before, characters: [{ ...before.characters[0], name: "ზუკა" }] };
  const result = invalidateChangedPreview(before, next);
  assert.equal(result.preview, null);
  assert.equal(result.reusePreviewId, "preview-1");
});
for (const patch of [{ birthDate: "2020-01-01" }, { gender: "boy" }, { photoDataUrl: "photo-2" }]) {
  test(`visual input change ${Object.keys(patch)} requires a fresh cover`, () => {
    const before = draft();
    const result = invalidateChangedPreview(before, {
      ...before,
      characters: [{ ...before.characters[0], ...patch }],
    } as JourneyDraft);
    assert.equal(result.preview, null);
    assert.equal(result.reusePreviewId, undefined);
  });
}
test("theme change requires a fresh cover", () => {
  const before = draft();
  const result = invalidateChangedPreview(before, { ...before, worldId: "dinosaurs" });
  assert.equal(result.preview, null);
  assert.equal(result.reusePreviewId, undefined);
});
test("saving a new hero at checkout preserves the displayed preview", () => {
  const before = draft();
  const result = invalidateChangedPreview(before, {
    ...before,
    characters: [{ ...before.characters[0], serverId: "saved" }],
  });
  assert.equal(result.preview, before.preview);
});
test("a photo edit after a pending rename clears base reuse", () => {
  const before = { ...draft(), preview: null, reusePreviewId: "previous" };
  const result = invalidateChangedPreview(before, {
    ...before,
    characters: [{ ...before.characters[0], photoDataUrl: "new" }],
  });
  assert.equal(result.reusePreviewId, undefined);
});
