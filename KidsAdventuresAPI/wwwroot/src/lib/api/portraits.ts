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
 * Never rejects the promise. A photo the model dislikes, a server that is down and a phone that
 * lost signal all mean the same thing on this form — this photo is not ready, try again — and a
 * caller with one thing to do about any of them should not have to catch to find that out.
 *
 * The failure path refuses rather than waving the photo through. Letting uploads past during an
 * outage means a bottle is discovered at the end of a finished, paid-for book; refusing costs the
 * parent one more tap.
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
    return { accepted: false, reason: "unavailable" };
  }
}
