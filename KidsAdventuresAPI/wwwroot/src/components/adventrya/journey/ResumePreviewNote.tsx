import { ArrowRight } from "lucide-react";
import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { getGuestPreviewStatus } from "@/lib/api/adventure-packs";
import { resolveApiUrl } from "@/lib/api/client";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import { readyPreviewPatch } from "@/lib/journey/previewRecovery";
import { readJourneyResume } from "@/lib/journey/resume";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft>) => void;
  onResume: () => void;
};

/**
 * The cover a parent already has, said on the screen they came back to.
 *
 * A preview costs a real image. The pointer to it has always been written to this device the
 * moment one finishes - the run's id, the world, the package - and the journey has always known
 * how to restore from it. But it only looked on the three screens after the preview: a parent who
 * pressed back landed on the questions, where nothing was said, and from there the only thing to
 * do was answer them again and buy a second cover. The shop paid twice and the parent lost the
 * one they were shown.
 *
 * So it is said here, where they are standing, before they start over.
 *
 * The button restores first and navigates second, which is the whole reason this is a component
 * rather than a link. Sending them to the preview screen with an empty draft would leave the
 * decision about whether to spend again to a race between two effects; fetching the run here and
 * putting it in the draft means the screen they arrive at already has its book.
 */
export function ResumePreviewNote({ draft, onChange, onResume }: Props) {
  const t = useT();
  const [cover, setCover] = useState<string | null>(null);
  const [title, setTitle] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const mounted = useRef(true);

  const resume = readJourneyResume();
  const runId = resume?.runId ?? null;
  /* Nothing to offer while this journey already has its preview, or has been paid for. */
  const idle = !runId || !!draft.preview || !!draft.orderId;

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    if (idle || !runId) return;

    let cancelled = false;
    void getGuestPreviewStatus(runId)
      .then((status) => {
        /*
          Only a finished one is worth offering. A run still drawing has its own screen with its
          own progress, and a run that failed is not a cover anybody has - saying "come back to
          your book" about either would be a promise this note cannot keep.
        */
        if (cancelled || status.status !== "Ready") return;
        setCover(status.coverImageUrl ? resolveApiUrl(status.coverImageUrl) : null);
        setTitle(status.title || null);
      })
      .catch(() => {
        /* Silent: an unreachable server is not something to explain on a screen about a child's
           name. The note simply does not appear. */
      });

    return () => {
      cancelled = true;
    };
  }, [idle, runId]);

  if (idle || !cover) return null;

  const resumePreview = async () => {
    if (!runId || busy) return;
    setBusy(true);
    try {
      const status = await getGuestPreviewStatus(runId);
      if (status.status !== "Ready") return;

      onChange(
        readyPreviewPatch(
          draft,
          {
            ...status,
            coverImageUrl: status.coverImageUrl ? resolveApiUrl(status.coverImageUrl) : "",
          },
          resume ?? undefined,
        ),
      );
      onResume();
    } catch {
      /* Left where they are, with the note still on screen to press again. */
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  return (
    <div className="ux-resume-preview">
      <img src={cover} alt="" aria-hidden="true" />
      <span>
        <small>{t.journey.preview.resumeHeading}</small>
        <strong>{title ?? t.journey.preview.resumeFallbackTitle}</strong>
      </span>
      <button type="button" onClick={() => void resumePreview()} disabled={busy} aria-busy={busy}>
        {t.journey.preview.resumeAction}
        {busy ? <BekiLoader size={14} /> : <ArrowRight aria-hidden="true" size={15} />}
      </button>
    </div>
  );
}
