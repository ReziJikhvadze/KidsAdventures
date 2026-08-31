import { useCallback, useEffect, useMemo, useRef } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { AuthStage } from "@/components/adventrya/journey/AuthStage";
import { CheckoutStage } from "@/components/adventrya/journey/CheckoutStage";
import { GeneratingStage } from "@/components/adventrya/journey/GeneratingStage";
import { PreviewStage } from "@/components/adventrya/journey/PreviewStage";
import { ProfileStage } from "@/components/adventrya/journey/ProfileStage";
import { getCharacter } from "@/lib/api/characters";
import { getToken } from "@/lib/api/client";
import type { CharacterGender, CharacterType, EyeColor } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import { useJourneyDraft, type DraftCharacter } from "@/lib/journey/draft";
import {
  STAGE_PROGRESS,
  backHrefForStage,
  progressLabelForStage,
  useJourneyStage,
  type JourneyStage,
} from "@/lib/journey/stages";

export function JourneyScreen() {
  const [stage, goToStage] = useJourneyStage();
  const t = useT();
  const [draft, setDraft] = useJourneyDraft();
  const { isAuthenticated, isLoading } = useAuth();

  /*
    The children this form is waiting to learn about, as a plain string.

    This is the effect's dependency, and it is a string rather than the array it is derived from
    on purpose. Keyed on `draft.characters`, the effect re-ran on every unrelated change to the
    draft — and the draft changes twice on the way into this screen, because the provider merges
    the query string in an effect of its own after the first render. Each re-run cancelled the
    request the previous one had in flight, while a ref guard stopped the new run from asking
    again: the answer arrived and was thrown away, and the parent met an empty form for a child
    the cabinet had just named. A string changes only when the set of unknown children does, so
    the request is started once and nothing interrupts it.
  */
  const hydrationKey = useMemo(
    () =>
      draft.characters
        .filter((c) => c.serverId && !(c.name ?? "").trim())
        .map((c) => c.serverId)
        .join(","),
    [draft.characters],
  );

  // Read inside the effect without being a dependency of it: the slots are looked up when the
  // request is made, and re-reading them must not restart the request.
  const charactersRef = useRef(draft.characters);
  charactersRef.current = draft.characters;

  // When continuing from the map — or starting a second book from the cabinet — draft slots may
  // only have serverIds. Fill in names, dates and photos.
  useEffect(() => {
    if (isLoading || !isAuthenticated || !hydrationKey) return;
    const needsHydration = charactersRef.current.filter(
      (c) => c.serverId && !(c.name ?? "").trim(),
    );
    if (needsHydration.length === 0) return;

    let cancelled = false;
    void (async () => {
      const updates = await Promise.all(
        needsHydration.map(async (slot) => {
          try {
            const remote = await getCharacter(slot.serverId!);
            const birthDate = remote.birthDate ? remote.birthDate.slice(0, 10) : "";
            const patch: Partial<DraftCharacter> = {
              /*
                Coerced, not trusted.

                Everything downstream calls `.trim()` on this — including the key that decides
                whether the slot still needs filling — so a response without a name did not
                degrade, it threw, and the whole creation screen came back blank behind the
                error boundary. A character with no name is a character we know nothing about,
                which is exactly the empty string.
              */
              name: remote.name ?? "",
              birthDate,
              gender: (remote.gender as CharacterGender | null) ?? null,
              eyeColor: (remote.eyeColor as EyeColor | null) ?? null,
              characterType: (remote.characterType as CharacterType) || slot.characterType,
              relationship: remote.relationship?.trim() || slot.relationship,
              // Existing portraits satisfy the photo gate for continuation.
              photoReady: !!remote.photoUrl,
            };
            return { localId: slot.localId, patch };
          } catch {
            return null;
          }
        }),
      );
      if (cancelled) return;
      setDraft((prev) => ({
        ...prev,
        characters: prev.characters.map((c) => {
          const hit = updates.find((u) => u?.localId === c.localId);
          return hit ? { ...c, ...hit.patch } : c;
        }),
      }));
    })();

    return () => {
      cancelled = true;
    };
  }, [hydrationKey, isAuthenticated, isLoading, setDraft]);

  const goAfterProfile = useCallback(() => {
    // The world is chosen before the details now, so this normally goes straight to the preview.
    // The fallback stays for anyone who arrives at /create#profile directly — a bookmark, a
    // stale link — with no world picked yet; they are sent to pick one rather than into a
    // preview that has no world to be set in.
    //
    // Routed through goToStage so the navigation stays client-side; a hard assign would unmount
    // the draft provider and throw away everything the parent just entered.
    if (draft.worldId) {
      goToStage("preview");
      return;
    }
    goToStage("world");
  }, [draft.worldId, goToStage]);

  const goAfterPreview = useCallback(() => {
    // Once signed in (session token present), never send the parent through #auth again.
    if (getToken() || (!isLoading && isAuthenticated)) {
      goToStage("checkout");
      return;
    }
    goToStage("auth");
  }, [isAuthenticated, isLoading, goToStage]);

  // Signed-in parents should never sit on #auth — jump straight to checkout.
  useEffect(() => {
    if (stage !== "auth") return;
    if (getToken() || (!isLoading && isAuthenticated)) {
      goToStage("checkout");
    }
  }, [stage, isAuthenticated, isLoading, goToStage]);

  // Returning from Stripe. The order id comes back in the query string, and the draft is no
  // longer persisted, so this is what reconnects a parent to the book they just paid for —
  // even if the stage hash was lost on the way back. Guarded by a ref so that finishing a
  // book and starting another does not drag them back to the generating stage.
  const resumedPaidOrder = useRef(false);
  useEffect(() => {
    if (resumedPaidOrder.current || stage === "generating") return;
    if (!new URLSearchParams(window.location.search).get("orderId")) return;
    resumedPaidOrder.current = true;
    goToStage("generating");
  }, [stage, goToStage]);

  const onPaid = useCallback(
    (orderId: string, bookId?: string | null) => {
      setDraft({ orderId, bookId: bookId ?? null });
      goToStage("generating");
    },
    [goToStage, setDraft],
  );

  const backHref = backHrefForStage(stage, {
    mode: draft.continuesFromBookId ? "continue" : "first",
    isPrintUpgrade: false,
    hasWorld: !!draft.worldId,
    cameFrom: draft.cameFrom,
  });

  const header = (
    <AppHeader
      backHref={backHref}
      // The journey's stages share one history entry, so its own step-by-step back target is
      // the only one that can walk the parent back through the questions they answered.
      explicitBack
      progressLabel={progressLabelForStage(stage)}
      progressValue={STAGE_PROGRESS[stage]}
    />
  );

  // Legacy hash from older rebuild — send users to the real demo /themes route.
  if (stage === "world") {
    if (typeof window !== "undefined") {
      goToStage("world");
    }
    return null;
  }

  return (
    <div className={`screen journey-shell journey-${stage} ux1-shell`}>
      <div className="journey-art" aria-hidden="true" />
      <div className="journey-shade" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      {header}

      {renderStage(stage, {
        draft,
        setDraft,
        goToStage,
        goAfterProfile,
        goAfterPreview,
        onPaid,
        t,
      })}
    </div>
  );
}

function renderStage(
  stage: JourneyStage,
  ctx: {
    draft: ReturnType<typeof useJourneyDraft>[0];
    setDraft: ReturnType<typeof useJourneyDraft>[1];
    goToStage: (next: JourneyStage) => void;
    goAfterProfile: () => void;
    goAfterPreview: () => void;
    onPaid: (orderId: string, bookId?: string | null) => void;
    // Read in the component, not here: renderStage is a plain function, and a hook
    // called from one only works while every branch happens to run in the same order.
    t: ReturnType<typeof useT>;
  },
) {
  const { t } = ctx;
  switch (stage) {
    case "profile":
      return (
        <ProfileStage draft={ctx.draft} onChange={ctx.setDraft} onContinue={ctx.goAfterProfile} />
      );
    case "preview":
      return (
        <PreviewStage
          draft={ctx.draft}
          onChange={ctx.setDraft}
          onContinue={ctx.goAfterPreview}
          onStopWaiting={() => ctx.goToStage("profile")}
        />
      );
    case "auth":
      return <AuthStage draft={ctx.draft} onAuthenticated={() => ctx.goToStage("checkout")} />;
    case "checkout":
      return <CheckoutStage draft={ctx.draft} onChange={ctx.setDraft} onPaid={ctx.onPaid} />;
    case "generating":
    case "generated":
      return <GeneratingStage draft={ctx.draft} onChange={ctx.setDraft} />;
    default:
      return <p>{t.journey.profile.title}</p>;
  }
}
