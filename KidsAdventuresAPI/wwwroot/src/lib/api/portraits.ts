import { apiRequest } from "./client";

/**
 * The reasons a photo can be turned away. They match the codes the server returns, so the
 * wording a parent reads is chosen here, in the language the interface is already in, rather
 * than arriving pre-written from a model that only speaks one.
 */
export type PortraitRejection =
  | "not_a_person"
  | "no_face"
  | "multiple_people"
  | "face_obscured"
  | "face_too_small"
  | "too_dark"
  | "unsuitable"
  | "unreadable"
  | "too_large"
  | "unavailable";

export type PortraitVerdict = { accepted: true } | { accepted: false; reason: PortraitRejection };

type PortraitCheckResponse = {
  accepted: boolean;
  reason: string;
};

const REJECTIONS: readonly string[] = [
  "not_a_person",
  "no_face",
  "multiple_people",
  "face_obscured",
  "face_too_small",
  "too_dark",
  "unsuitable",
  "unreadable",
  "too_large",
  "unavailable",
];

function isRejection(reason: string): reason is PortraitRejection {
  return REJECTIONS.includes(reason);
}

/**
 * Asks the server whether a chosen photo is usable, before it becomes the face of a book.
 *
 * Never rejects the promise, and never blocks on its own failure. A server that is down and a
 * phone that lost signal are not evidence about the photo, and treating them as a refusal put the
 * words "this photo will not do" in front of a parent whose photo was fine. Only an answer from
 * the server can turn a photo away; anything else lets it through.
 */
export async function checkPortrait(photoDataUrl: string): Promise<PortraitVerdict> {
  try {
    const result = await apiRequest<PortraitCheckResponse>("/api/portraits/check", {
      method: "POST",
      auth: false,
      body: JSON.stringify({ photoDataUrl }),
    });

    if (result.accepted) return { accepted: true };

    return {
      accepted: false,
      reason: isRejection(result.reason) ? result.reason : "unsuitable",
    };
  } catch {
    return { accepted: true };
  }
}
