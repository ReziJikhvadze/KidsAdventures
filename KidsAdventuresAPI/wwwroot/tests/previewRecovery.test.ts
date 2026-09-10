import { strict as assert } from "node:assert";
import { test } from "node:test";
import { hasRenderedPreview, readyPreviewPatch } from "../src/lib/journey/previewRecovery.ts";
import type { JourneyDraft } from "../src/lib/journey/draft";
import type { MasterStoryRunStatus } from "../src/lib/api/types";

const blank = (): JourneyDraft => ({
  worldId: null,
  preview: null,
  bookPackage: "print",
  storyNotes: "",
  characters: [
    {
      localId: "local-test",
      isPrimary: true,
      name: "",
      birthDate: "",
      gender: null,
      eyeColor: null,
      characterType: "child",
      relationship: "",
      customRelationship: "",
      photoDataUrl: null,
      photoReady: false,
      photoStored: false,
    },
  ],
  shipping: {
    recipientName: "",
    recipientPhone: "",
    city: "",
    addressLine1: "",
    saveForLater: false,
  },
  orderId: null,
  bookId: null,
  continuesFromBookId: null,
  cameFrom: null,
  pickerHref: null,
  promoCode: "",
});
const ready = (): MasterStoryRunStatus => ({
  runId: "stored-preview",
  status: "Ready",
  worldId: "space",
  title: "Test book",
  childName: "Test child",
  birthDate: "2021-01-01",
  gender: "girl",
  hasPortrait: true,
});

test("reload while preview is pending restores its world before checkout", () => {
  const result = readyPreviewPatch(blank(), ready(), { worldId: "space" });
  assert.equal(result.worldId, "space");
  assert.equal(result.preview?.worldId, "space");
  assert.equal(result.preview?.storyId, "stored-preview");
  assert.equal(result.characters[0].name, "Test child");
  assert.equal(result.characters[0].portraitRunId, "stored-preview");
});

test("an old resume with a null world recovers from the server, not a default world", () => {
  assert.equal(readyPreviewPatch(blank(), ready(), { worldId: null }).worldId, "space");
});

test("authoritative preview world outranks a stale pointer", () => {
  assert.equal(readyPreviewPatch(blank(), ready(), { worldId: "dinosaurs" }).worldId, "space");
});

test("older API responses can use the pending run's matching world", () => {
  assert.equal(
    readyPreviewPatch(blank(), { ...ready(), worldId: undefined }, { worldId: "magic" }).worldId,
    "magic",
  );
});

test("no known world cannot quietly order a dinosaur book", () => {
  assert.throws(() => readyPreviewPatch(blank(), { ...ready(), worldId: null }), /no valid world/);
});

test("restoring a saved hero preserves their server identity without copying another photo", () => {
  const result = readyPreviewPatch(blank(), ready(), { characterId: "saved-child" });
  assert.equal(result.characters[0].serverId, "saved-child");
  assert.equal(result.characters[0].portraitRunId, undefined);
});

test("same-tab recovery does not discard the uploaded portrait or edited child details", () => {
  const draft = blank();
  draft.characters[0] = {
    ...draft.characters[0],
    name: "Edited child",
    photoDataUrl: "data:image/png;base64,test",
    photoReady: true,
  };
  const result = readyPreviewPatch(draft, ready());
  assert.equal(result.characters[0].name, "Edited child");
  assert.equal(result.characters[0].photoDataUrl, draft.characters[0].photoDataUrl);
  assert.equal(result.characters[0].portraitRunId, undefined);
  assert.equal(draft.worldId, null); // no mutation of the caller's in-flight draft
});

test("an unfinished preview cannot be accepted for checkout", () => {
  assert.throws(() => readyPreviewPatch(blank(), { ...ready(), status: "Writing" }), /not ready/);
});

test("fast preview recovery preserves the exact checkout revision and rendered intro", () => {
  const result = readyPreviewPatch(blank(), {
    ...ready(),
    previewVersion: "preview-v1",
    coverRevisionId: "revision-7",
    introImageUrl: "/preview/intro",
    coverImageUrl: "/preview/cover",
  });
  assert.equal(result.preview?.coverRevisionId, "revision-7");
  assert.equal(result.preview?.introImageUrl, "/preview/intro");
  assert.equal(result.preview?.previewVersion, "preview-v1");
  assert.equal(hasRenderedPreview(result.preview!), true);
});

test("rendered covers never receive browser typography when their prompt version changes", () => {
  for (const previewVersion of ["preview-v1", "fast-preview-v1", "preview-v2", undefined]) {
    const result = readyPreviewPatch(blank(), {
      ...ready(),
      previewVersion,
      coverRevisionId: "revision-7",
      introImageUrl: "/preview/intro",
      coverImageUrl: "/preview/cover",
    });
    assert.equal(hasRenderedPreview(result.preview!), true);
  }
});

test("legacy previews keep their browser-composed book layout", () => {
  assert.equal(hasRenderedPreview(null), false);
  assert.equal(hasRenderedPreview(readyPreviewPatch(blank(), ready()).preview!), false);
});
