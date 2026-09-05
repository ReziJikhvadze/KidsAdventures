import { Camera, Check, Loader2, Pencil, Plus, Trash2, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import photoGuide from "@/assets/photo-guide.webp";
import { BirthDateField } from "@/components/adventrya/journey/BirthDateField";
import { WorldArtPanel } from "@/components/adventrya/journey/WorldArtPanel";
import { SparkleIcon } from "@/components/adventrya/landing/icons";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { listCharacters } from "@/lib/api/characters";
import { checkPortrait, type PortraitRejection } from "@/lib/api/portraits";
import { useAuth } from "@/lib/auth/AuthContext";
import { BOOK_LANGUAGES, type BookLanguage, useT } from "@/lib/i18n";
import { preparePortrait } from "@/lib/images/preparePortrait";
import type { CharacterGender, CharacterResponse, EyeColor } from "@/lib/api/types";
import { emptyCharacter, type DraftCharacter, type JourneyDraft } from "@/lib/journey/draft";

// The keys are the stored values, not display copy, so they stay literal rather than
// being derived from a catalogue that now changes with the interface language.
const EYE_COLORS: EyeColor[] = ["brown", "blue", "green", "grey"];

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  onContinue: () => void;
};

/**
 * Demo: `useState(additional.length ? null : "primary")` — with only the main
 * hero, open `ux-character-form` on entry. Summaries appear after save (or when
 * supporting characters already exist).
 */
function entryEditingId(characters: DraftCharacter[]): string | null {
  if (characters.some((c) => !c.isPrimary)) return null;
  return characters.find((c) => c.isPrimary)?.localId ?? null;
}

/** A saved hero whose details have all arrived from the account: nothing left to type. */
function isHydratedHero(character: DraftCharacter): boolean {
  return !!character.serverId && character.name.trim() !== "" && character.photoReady;
}

/** A slot nobody has touched: no saved child behind it and nothing typed into it. */
function isBlankHero(character: DraftCharacter): boolean {
  return (
    !character.serverId &&
    character.name.trim() === "" &&
    !character.birthDate &&
    !character.photoDataUrl &&
    !character.photoReady
  );
}

/** The children an account can make a book about, newest first. */
function heroesOf(characters: CharacterResponse[]): CharacterResponse[] {
  return characters
    .filter((c) => c.isPrimary || c.characterType === "child")
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
}

export function ProfileStage({ draft, onChange, onContinue }: Props) {
  const t = useT();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [editingId, setEditingId] = useState<string | null>(() => entryEditingId(draft.characters));
  const [error, setError] = useState<string | null>(null);
  /*
    Which cards the parent opened themselves, as opposed to the one the entry rule opens.

    The hero's form opens on arrival so a blank page is a form and not a summary of nothing. A
    saved hero fills in a moment later, and the form that was opened for typing is then a form
    with nothing to type in — it closes to the summary card. A card the parent clicked "change" on
    is theirs, and stays open however complete it is.
  */
  const userOpened = useRef<Set<string>>(new Set());

  /*
    The account's saved children, for the picker above the form.

    Loaded once the session is known. `null` while unknown, so the auto-selection below can tell
    "no heroes" from "not loaded yet" — selecting nothing on the first render and then nothing
    again when the list arrives is how a parent ends up retyping a child the account already has.
  */
  const [heroes, setHeroes] = useState<CharacterResponse[] | null>(null);
  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setHeroes([]);
      return;
    }
    let cancelled = false;
    listCharacters()
      .then((all) => {
        if (!cancelled) setHeroes(heroesOf(all));
      })
      .catch(() => {
        if (!cancelled) setHeroes([]);
      });
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  const primary = draft.characters.find((c) => c.isPrimary) ?? null;

  /**
   * Puts a saved child in the hero slot. Only the id is written here: JourneyScreen sees a slot
   * with an id and no name and fills in the rest from the account, portrait included.
   */
  const chooseHero = (hero: CharacterResponse) => {
    const fresh: DraftCharacter = { ...emptyCharacter(true), serverId: hero.id };
    onChange((prev) => ({
      ...prev,
      characters: prev.characters.map((c) => (c.isPrimary ? fresh : c)),
    }));
    userOpened.current.delete(fresh.localId);
    setEditingId(fresh.localId);
    setError(null);
  };

  /*
    A signed-in parent with saved children starts from one of them, not from an empty form.

    Once per visit, and only into a slot that is genuinely untouched: a parent who arrived with a
    child in the URL, who has started typing, or who asked for a new child (`?new`) is left alone.
    With one hero there is nothing to choose; with several the newest is the likeliest, and the
    picker above the form is one tap from the rest.
  */
  const autoSelected = useRef(false);
  useEffect(() => {
    if (autoSelected.current || !heroes || heroes.length === 0 || !primary) return;
    autoSelected.current = true;
    if (typeof window !== "undefined" && new URLSearchParams(window.location.search).has("new")) {
      return;
    }
    if (!isBlankHero(primary)) return;
    chooseHero(heroes[0]);
    // chooseHero closes over onChange, which is stable; primary is read once when the list lands.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [heroes, primary]);

  // The form opened for a saved hero closes to a summary card once the account has filled it.
  useEffect(() => {
    if (!primary || editingId !== primary.localId) return;
    if (userOpened.current.has(primary.localId)) return;
    if (isHydratedHero(primary)) setEditingId(null);
  }, [primary, editingId]);
  /*
    Consent to the terms, which is asked for once and here.

    Here because this is the last press before a book is written: the story, the pictures and the
    money all follow from it. Deliberately not remembered in the draft — a tick that survives a
    reload is a tick nobody gave.
  */
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  /*
    Whether the dialog is standing in for the consent, rather than only reporting on it.

    Every other thing this form can refuse over is a field on the page: the dialog names it, the
    parent closes the dialog and fixes it where it lives. The consent is the exception — it is a
    single tick, and making somebody dismiss a box in order to reach it is three actions for one
    omission, on the one control most likely to be below the fold.
  */
  const [blockedOnTerms, setBlockedOnTerms] = useState(false);

  /** Refuse, and say whether the consent is what did it. */
  const fail = (message: string, onConsent = false) => {
    setBlockedOnTerms(onConsent);
    setError(message);
  };

  const dismiss = () => {
    setBlockedOnTerms(false);
    setError(null);
  };

  // The draft is only read from localStorage after the first render (SSR safety),
  // and that swap hands every character a brand-new localId. Without re-applying
  // the entry rule the id above goes stale and the hero renders as an empty
  // "ready" summary instead of an open form.
  useEffect(() => {
    setEditingId((current) => {
      if (current === null) return null;
      if (draft.characters.some((c) => c.localId === current)) return current;
      return entryEditingId(draft.characters);
    });
  }, [draft.characters]);
  const copy = t.journey;

  const editing = draft.characters.find((c) => c.localId === editingId) ?? null;

  const updateCharacter = (localId: string, patch: Partial<DraftCharacter>) => {
    onChange((prev) => ({
      ...prev,
      characters: prev.characters.map((c) => (c.localId === localId ? { ...c, ...patch } : c)),
    }));
  };

  const validateCharacter = (character: DraftCharacter): string | null => {
    if (!character.name.trim()) return copy.validation.nameRequired;
    if (character.isPrimary && !character.birthDate) return copy.validation.birthDateRequired;
    const needsGender = character.characterType === "child" || character.characterType === "adult";
    if (needsGender && !character.gender) return copy.validation.genderRequired;
    if (!character.isPrimary) {
      if (!character.relationship) return copy.validation.relationshipRequired;
      if (character.relationship === "სხვა" && !character.customRelationship.trim()) {
        return copy.validation.relationshipTextRequired;
      }
    }
    if (!character.photoReady) return copy.validation.photoRequired;
    return null;
  };

  const removeCharacter = (localId: string) => {
    onChange((prev) => ({
      ...prev,
      characters: prev.characters.filter((c) => c.localId !== localId),
    }));
    if (editingId === localId) setEditingId(null);
  };

  /**
   * The main hero cannot simply be dropped — the rest of the journey reads
   * `primaryCharacter(draft)` and the slot must never be empty. Deleting the hero
   * therefore swaps in a blank one and reopens the form, so "remove" clears the
   * child's details and lets a different child be entered.
   */
  const clearPrimary = (localId: string) => {
    const fresh = emptyCharacter(true);
    onChange((prev) => ({
      ...prev,
      characters: prev.characters.map((c) => (c.localId === localId ? fresh : c)),
    }));
    setEditingId(fresh.localId);
    setError(null);
  };

  /** Supporting characters are removed outright; the hero is cleared in place. */
  const removeAction = (character: DraftCharacter) => () => {
    if (character.isPrimary) {
      clearPrimary(character.localId);
      return;
    }
    removeCharacter(character.localId);
    setError(null);
  };

  const handleContinue = () => {
    /*
      An open form used to stop this with "finish editing first", which was the only thing the
      removed "remember" button did — so with it gone that check would have been a dead end.
      It was never needed: the editor writes straight into the draft, so what is on screen is
      what is validated below, open or closed.
    */
    const primary = draft.characters.find((c) => c.isPrimary);
    if (!primary) {
      fail(copy.validation.nameRequired);
      return;
    }
    const message = validateCharacter(primary);
    if (message) {
      fail(message);
      setEditingId(primary.localId);
      return;
    }
    for (const supporting of draft.characters.filter((c) => !c.isPrimary)) {
      const supportingError = validateCharacter(supporting);
      if (supportingError) {
        fail(supportingError);
        setEditingId(supporting.localId);
        return;
      }
    }
    if (!acceptedTerms) {
      fail(copy.validation.termsRequired, true);
      return;
    }
    setError(null);
    onContinue();
  };

  const consent = (
    <label className="ux-terms-consent">
      <input
        type="checkbox"
        checked={acceptedTerms}
        onChange={(event) => {
          setAcceptedTerms(event.target.checked);
          /*
            Ticking clears the complaint — unless the complaint is the thing holding the tick.

            The dialog is open exactly while `error` is set, so clearing it here shut the dialog
            the instant the box was ticked, half a second before the parent could reach the button
            beside it. They were left back on the form, consented, with nothing having happened
            and the same press to make again. In the dialog the tick is half the answer; the
            button is the other half, and it clears the error itself.
          */
          if (event.target.checked && !blockedOnTerms) setError(null);
        }}
      />
      <span>
        {copy.profile.termsPrefix}
        <a href="/terms" target="_blank" rel="noreferrer">
          {copy.profile.termsLink}
        </a>
      </span>
    </label>
  );

  return (
    <section className="ux-profile-stage">
      {/*
        The title of the step, across the top of it.

        It used to be the first thing in the left column, which made it the caption over a
        painting rather than the name of the screen — ranged left, half the page wide, with the
        form starting level with it on the right. Spanning both columns and centred, it reads as
        the heading it is, and everything the step actually asks for begins underneath.

        The row below then has the page's height minus this, which is the point: the form was
        being stretched to a height it did not need and was spending the surplus on gaps between
        the inputs.
      */}
      <header className="ux-stage-heading">
        <p className="eyebrow">
          <SparkleIcon />
          {copy.profile.eyebrow}
        </p>
        <h1>{copy.profile.title}</h1>
        {/* The paragraph that stood here explained which details we ask for. The form below it
            is four labelled questions long and answers that by being read. */}
      </header>

      {/*
        The left column is the world the parent just chose.

        It held a heading and then nothing — a third of the page of empty space beside the form,
        and no sign of the island that had been picked one screen earlier. The painting is the
        same one the map is made of, framed on that island.
      */}
      <div className="ux-profile-aside">
        {draft.worldId ? <WorldArtPanel worldId={draft.worldId} /> : null}
      </div>

      <div className="ux-character-stack">
        {/*
          The book-language switcher went. It was a second language control on a page that
          already has one in the header, asking the same question in different words — and the
          book now simply follows the interface it was ordered in.
        */}

        {heroes && heroes.length > 0 && primary ? (
          <HeroPicker
            heroes={heroes}
            selectedId={primary.serverId ?? null}
            onPick={chooseHero}
            onNew={() => clearPrimary(primary.localId)}
          />
        ) : null}

        {draft.characters.map((character, index) => {
          if (editingId === character.localId) {
            return (
              <CharacterEditor
                key={character.localId}
                character={character}
                index={index}
                onChange={(patch) => updateCharacter(character.localId, patch)}
                onCancel={() => {
                  if (!character.name.trim() && !character.isPrimary) {
                    removeCharacter(character.localId);
                  } else {
                    setEditingId(null);
                  }
                  setError(null);
                }}
                onClearToNew={() => {
                  const fresh = emptyCharacter(character.isPrimary);
                  onChange((prev) => ({
                    ...prev,
                    characters: prev.characters.map((c) =>
                      c.localId === character.localId ? fresh : c,
                    ),
                  }));
                  setEditingId(fresh.localId);
                  setError(null);
                }}
              />
            );
          }

          return (
            <CharacterSummary
              key={character.localId}
              character={character}
              index={index}
              /*
                Switch, rather than refuse.

                This used to answer "finish editing first", which was fair while an open form had
                a Save button to finish it with. The hero's form has no buttons of its own any
                more, so on a draft that carries a supporting character the refusal became a dead
                end: the hero opens, nothing closes it, and every other card declines to open.
                Nothing needs finishing — the editor writes into the draft as it is typed — so
                opening one card simply closes the other.
              */
              onEdit={() => {
                userOpened.current.add(character.localId);
                setEditingId(character.localId);
                setError(null);
              }}
              onRemove={removeAction(character)}
            />
          );
        })}

        {/*
          "Add a supporting character" went: one book, one child, and the shortest form that
          gets there. The machinery stays — a continuation still carries a sister or a dog
          forward through ?characterIds — but nobody is asked to build a cast up front.
        */}

        {/*
          What is missing is said in the middle of the screen, not in a red line under the form.

          The line lived between the last field and the closing row, which is the one part of this
          page most likely to be below the fold — so pressing "create the book" and having nothing
          happen was the entire feedback a parent got. A dialog interrupts, which is the correct
          amount of interruption for "this cannot go on without an answer", and it can say which
          answer without competing for space the form needs.
        */}
        <Dialog open={error !== null} onOpenChange={(open) => (open ? null : dismiss())}>
          <DialogContent className="ux-form-error-dialog">
            <DialogTitle>{copy.profile.missingTitle}</DialogTitle>
            <p>{error}</p>
            {blockedOnTerms ? (
              <>
                {consent}
                {/*
                  Continue, not close. The parent came here by pressing "create the book" and
                  nothing about that intention changed; handing back a dismissed dialog and
                  asking them to press it again is a step that exists only because the tick was
                  somewhere else. It re-runs the same checks, so nothing is skipped by taking
                  the short way.
                */}
                <button
                  className="button journey-primary"
                  type="button"
                  disabled={!acceptedTerms}
                  onClick={() => {
                    dismiss();
                    handleContinue();
                  }}
                >
                  {copy.profile.continue}
                </button>
              </>
            ) : (
              <button className="button journey-primary" type="button" onClick={dismiss}>
                {t.common.actions.close}
              </button>
            )}
          </DialogContent>
        </Dialog>
      </div>

      {/*
        The consent and the button are a row of the page, not the last item in the card column.

        Inside the column they counted toward its height, so the painting beside it — stretched
        to the same column — finished level with the button rather than with the card, and stood
        79px taller than the thing it is paired with. Out here it has a grid row of its own under
        the right-hand column, which leaves the card and the picture sharing one row and one
        height.
      */}
      <div className="ux-profile-footer">
        {/* The padlock line saying the data is only used to make the book has gone. The consent
            beside it links the terms, which is where that promise is actually made and kept. */}
        {consent}

        <button className="button journey-primary" type="button" onClick={handleContinue}>
          {copy.profile.continue}
        </button>
      </div>
    </section>
  );
}

// გოგა → გოგასთვის, დავით → დავითისთვის: the benefactive attaches to the genitive stem,
// never with a hyphen — the same rule the book interior enforces for the child's name.
function benefactive(name: string): string {
  const n = name.trim();
  return /[აეიოუ]$/.test(n) ? `${n}სთვის` : `${n}ისთვის`;
}

/**
 * The account's saved children, one tap each, and a way to start a new one.
 *
 * Chips rather than a dropdown: a family has a handful of children, and the names are the whole
 * of what a parent needs to see to choose. The selected one is the child in the hero slot below;
 * "a new child" empties that slot for someone the account has not met.
 */
function HeroPicker({
  heroes,
  selectedId,
  onPick,
  onNew,
}: {
  heroes: CharacterResponse[];
  selectedId: string | null;
  onPick: (hero: CharacterResponse) => void;
  onNew: () => void;
}) {
  const copy = useT().journey.profile.heroPicker;
  const isNew = selectedId === null;

  return (
    <fieldset className="choice-fieldset ux-hero-picker">
      <legend>{copy.title}</legend>
      <div className="ux-choice-chips">
        {heroes.map((hero) => (
          <button
            key={hero.id}
            type="button"
            className={hero.id === selectedId ? "selected" : ""}
            aria-pressed={hero.id === selectedId}
            onClick={() => {
              if (hero.id !== selectedId) onPick(hero);
            }}
          >
            {hero.name}
          </button>
        ))}
        <button
          type="button"
          className={isNew ? "selected" : ""}
          aria-pressed={isNew}
          onClick={() => {
            if (!isNew) onNew();
          }}
        >
          <Plus aria-hidden="true" size={14} />
          {copy.newChild}
        </button>
      </div>
      <p className="ux-hero-picker-hint">{copy.hint}</p>
    </fieldset>
  );
}

function CharacterEditor({
  character,
  index,
  onChange,
  onCancel,
  onClearToNew,
}: {
  character: DraftCharacter;
  index: number;
  onChange: (patch: Partial<DraftCharacter>) => void;
  onCancel: () => void;
  onClearToNew: () => void;
}) {
  const t = useT();
  const fileRef = useRef<HTMLInputElement>(null);
  const copy = t.journey;
  const needsGender = character.characterType === "child" || character.characterType === "adult";

  const [checking, setChecking] = useState(false);
  const [rejection, setRejection] = useState<PortraitRejection | null>(null);
  /*
    Which check the answer belongs to. A parent who picks a second photo while the first is
    still being judged would otherwise see the older verdict land on the newer photo — and the
    older one is as likely to be an acceptance as a refusal.
  */
  const checkRef = useRef(0);

  const title = character.isPrimary
    ? copy.profile.primaryCharacter
    : copy.profile.nthCharacter(index + 1);

  const onPhoto = (file: File | null) => {
    if (!file) return;

    const ticket = ++checkRef.current;
    setRejection(null);
    setChecking(true);
    // The photo being replaced stops counting the moment a new one is chosen. Leaving the old
    // one ready would let the form continue on a portrait the parent has already moved on from.
    onChange({ photoDataUrl: null, photoReady: false, photoStored: false });

    void (async () => {
      try {
        // Downscaled before it is stored, never at full camera resolution. A phone photo plus
        // base64 overhead exceeds the upload limit, and an oversized request is rejected before
        // our code runs — so it comes back with no CORS headers and the browser blames CORS.
        const { dataUrl } = await preparePortrait(file);
        if (ticket !== checkRef.current) return;

        /*
          Ask whether there is a person in the picture, before it becomes the face of a book.

          This call was removed once, for a good reason: the gate it asked used to judge the
          photograph — lighting, framing, how many people were in it — and refused pictures that
          were perfectly usable, so turning it off was better than keeping it. The gate no longer
          does that. It asks one question, and a car, a plate or a drawing is the only thing it
          says no to, which is the case nothing else in the pipeline catches: the identity
          analyzer is told to extract a face, so shown a car it describes a car, and the book is
          written, illustrated and paid for around it.

          `Beki:PortraitGateEnabled` still decides on the server. With it off every photo passes
          here too, so this stays a single path rather than two that have to agree.

          `preparePortrait` is not the check. It downscales, because a phone photo plus base64
          overhead exceeds the upload limit and an oversized request is refused by the host before
          any of our code runs — which reaches the browser as an unexplained CORS error.
        */
        const verdict = await checkPortrait(dataUrl);
        if (ticket !== checkRef.current) return;

        if (!verdict.accepted) {
          // photoReady was already cleared above, so the form simply does not advance.
          setRejection(verdict.reason);
          return;
        }

        onChange({ photoDataUrl: dataUrl, photoReady: true, photoStored: false });
      } catch {
        // preparePortrait only throws when the file cannot be read at all — not a judgement
        // about the photograph, a broken file. It used to end here silently, leaving a parent
        // looking at a button that would not go ready and no word about why.
        if (ticket !== checkRef.current) return;
        setRejection("unreadable");
      } finally {
        if (ticket === checkRef.current) setChecking(false);
      }
    })();
  };

  /*
    The panel says one thing, and it is written on the button.

    It used to carry an icon in a gold chip, a status line, a prompt, a two-line list of tips and
    a pair of example photographs — five pieces of chrome around one square and one control. The
    square shows what has been chosen and the button says what to do; the only words left are the
    ones that cannot be inferred from those two: that a check is running, or why a photo was
    refused.
  */
  let photoAction = copy.characterForm.photoUpload;
  if (checking) photoAction = copy.characterForm.photoChecking;
  else if (character.photoReady) photoAction = copy.characterForm.photoReplace;

  return (
    <div className={`ux-character-form ${character.isPrimary ? "" : "ux-second-character-form"}`}>
      {/*
        One line, and it is the heading.

        This was a numbered token, a small gold label and a display-face title — and for the main
        hero the label and the title were the same three words, printed twice, one above the
        other. The number counted cards in a stack that is usually one card long. What is left is
        the label, promoted to the heading it always was in the reading order.
      */}
      <div className="ux-form-intro">
        <h2>{character.isPrimary ? copy.profile.primaryCharacter : title}</h2>
      </div>

      {character.serverId && character.originalName ? (
        <div
          className="ux-known-character-banner"
          style={{
            display: "flex",
            flexWrap: "wrap",
            gap: 12,
            justifyContent: "space-between",
            alignItems: "baseline",
            backgroundColor: "var(--surface-sunken, #f7f7f7)",
            padding: "12px 16px",
            borderRadius: 12,
            marginBottom: 24,
          }}
        >
          <strong style={{ fontSize: "0.9rem", color: "var(--text-main, #111)" }}>
            {character.name.trim() !== character.originalName.trim() && character.name.trim() !== ""
              ? copy.profile.knownHero.newHero(character.name.trim())
              : copy.profile.knownHero.newBook(character.originalName.trim())}
          </strong>
          <button
            type="button"
            className="ux-inline-link"
            style={{ fontSize: "0.85rem" }}
            onClick={onClearToNew}
          >
            {copy.profile.knownHero.otherChild}
          </button>
        </div>
      ) : null}

      <div className="ux-character-fields">
        <div className="ux-character-inputs">
          <div className="form-grid">
            <label className="field">
              <span>{copy.characterForm.nameLabel}</span>
              <input
                value={character.name}
                onChange={(e) => onChange({ name: e.target.value })}
                autoComplete="off"
              />
            </label>

            <BirthDateField
              label={copy.characterForm.birthDateLabel}
              value={character.birthDate}
              onChange={(birthDate) => onChange({ birthDate })}
            />
          </div>

          {/*
            One question to a row, each with the column's full width.

            Gender and eye colour used to share a row, which gave the question "girl or boy?" a
            163px column its own label could not fit on — it wrapped to two lines and left the
            toggle sitting half a line below the swatches beside it. On a row of its own the
            label is one line and the four colours have room to be two clear pairs.
          */}
          {needsGender ? (
            <fieldset className="choice-fieldset gender-fieldset">
              <legend>{copy.characterForm.genderLegend}</legend>
              {/*
                A toggle with a side each, rather than two buttons that happen to be adjacent.
                `data-picked` slides the indicator; aria-pressed says out loud which side is
                chosen, which these had never done while the package options next door did.
              */}
              <div
                className="ux-segmented-control ux-gender-toggle"
                data-picked={character.gender ?? "none"}
              >
                {(["girl", "boy"] as CharacterGender[]).map((gender) => (
                  <button
                    key={gender}
                    type="button"
                    aria-pressed={character.gender === gender}
                    className={character.gender === gender ? "selected" : ""}
                    onClick={() => onChange({ gender })}
                  >
                    {t.common.genders[gender]}
                  </button>
                ))}
              </div>
            </fieldset>
          ) : null}

          <fieldset className="choice-fieldset eye-fieldset">
            <legend>{copy.characterForm.eyeColorLegend}</legend>
            <div className="eye-options">
              {EYE_COLORS.map((color, i) => (
                <button
                  key={color}
                  type="button"
                  className={character.eyeColor === color ? "selected" : ""}
                  onClick={() => onChange({ eyeColor: color })}
                >
                  <i className={`eye eye-${i}`} aria-hidden="true" />
                  {t.common.eyeColors[color]}
                </button>
              ))}
            </div>
          </fieldset>

          {!character.isPrimary ? (
            <fieldset className="choice-fieldset">
              <legend>{copy.characterForm.relationshipLegend}</legend>
              <div className="ux-choice-chips">
                {t.common.relationships.map((rel) => (
                  <button
                    key={rel}
                    type="button"
                    className={character.relationship === rel ? "selected" : ""}
                    onClick={() => onChange({ relationship: rel })}
                  >
                    {rel}
                  </button>
                ))}
              </div>
              {character.relationship === "სხვა" ? (
                <label className="field" style={{ marginTop: 12 }}>
                  <span>{copy.characterForm.relationshipCustom}</span>
                  <input
                    value={character.customRelationship}
                    placeholder={copy.characterForm.relationshipPlaceholder}
                    onChange={(e) => onChange({ customRelationship: e.target.value })}
                  />
                </label>
              ) : null}
            </fieldset>
          ) : null}
        </div>

        <div
          className={`ux-photo-upload ${character.photoDataUrl ? "" : "guided"} ${
            character.photoReady ? "ready" : ""
          } ${rejection ? "rejected" : ""}`}
        >
          {/*
            What a usable photo looks like, shown rather than described.

            The paragraph that used to stand here listed the rules in words — face the camera,
            come close, good light — which is three sentences to read before a parent can do the
            one thing this panel is for. Two pictures and a tick say it at a glance, and the
            panel stays as quiet as it was asked to be.

            Only while there is no photo. Once one is chosen it has been judged already, and
            advice about a decision already made is just something else in the way.
          */}
          {!character.photoDataUrl ? (
            <img
              className="ux-photo-guide"
              src={photoGuide}
              alt={copy.characterForm.photoGuideAlt}
            />
          ) : null}
          {/*
            Two bands: the picture, and what to do about it.

            The frame takes the slack, so the box has the same shape however tall the column
            beside it grows — square whatever it is given, so the photo is never stretched and
            the empty state is never a hole: the same square, waiting.
          */}
          {/*
            The square is the target, not a picture of one.

            It is the largest thing in the panel and it is exactly where a parent points when
            they mean "put the photo here", so it opens the file dialog like the button below it
            does. The button stays: it is what says out loud what the square is for.
          */}
          <button
            type="button"
            className="ux-photo-frame"
            disabled={checking}
            aria-label={photoAction}
            onClick={() => fileRef.current?.click()}
          >
            {character.photoDataUrl ? (
              <img src={character.photoDataUrl} alt="" />
            ) : (
              <span className="ux-photo-frame-empty" aria-hidden="true">
                <Camera />
              </span>
            )}
          </button>

          <div className="ux-photo-foot">
            {/*
              role="status" because the refusal arrives a second after the file dialog closed —
              long after focus moved on — so a screen reader has to be told it appeared. It is
              the one message this panel keeps: a photo can be refused for a reason the parent
              cannot guess from looking at the square.
            */}
            {rejection ? (
              <p className="ux-photo-rejection" role="status">
                {copy.characterForm.photoRejected[rejection]}
              </p>
            ) : null}

            <button type="button" disabled={checking} onClick={() => fileRef.current?.click()}>
              {checking ? <Loader2 className="ux-photo-spinner" aria-hidden="true" /> : null}
              {photoAction}
            </button>
          </div>
          <input
            ref={fileRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            hidden
            onChange={(e) => {
              onPhoto(e.target.files?.[0] ?? null);
              // Cleared so that choosing the same file again still fires a change — otherwise a
              // parent who retries the photo they just had refused gets no reaction at all.
              e.target.value = "";
            }}
          />
        </div>
      </div>

      {/*
        One way forward and at most one way back.

        This offered save, cancel and delete at once, while the card behind it offered change and
        delete again — four words for what a parent experiences as two decisions, and cancel and
        delete did nearly the same thing to a character that had never been saved. Deleting now
        belongs to the card, which is where the character being deleted is actually shown. And a
        hero who has never been filled in gets no way back, because there is nothing behind them
        to go back to.
      */}
      {/*
        The hero's form has no buttons of its own at all.

        "Remember" was the last of them, and it closed the editor without saving anything — the
        draft has lived in memory since the first keystroke — so it was a button that appeared to
        do the thing the button below it actually does, sitting where the eye looks for the end of
        the form. What ends the form is "create the book", and there is now nothing between the
        last question and it.

        A supporting character keeps a way back, because there is a card behind them to go back
        to; the hero has nothing behind them, which is why they never had one.
      */}
      {!character.isPrimary ? (
        <div className="ux-form-actions">
          <button className="ux-inline-link" type="button" onClick={onCancel}>
            <X aria-hidden="true" />
            {t.common.actions.cancel}
          </button>
        </div>
      ) : null}
    </div>
  );
}

function CharacterSummary({
  character,
  index,
  onEdit,
  onRemove,
}: {
  character: DraftCharacter;
  index: number;
  onEdit: () => void;
  onRemove?: () => void;
}) {
  const t = useT();
  const relationshipLabel = useMemo(() => {
    if (character.isPrimary) return null;
    if (character.relationship === "სხვა") {
      return character.customRelationship.trim() || character.relationship;
    }
    return character.relationship || null;
  }, [character]);

  const label = character.isPrimary
    ? t.journey.profile.primaryCharacter
    : t.journey.profile.nthCharacter(index + 1);

  return (
    <article className="ux-character-summary">
      <span className="ux-ready-check" aria-hidden="true">
        <Check />
      </span>
      <span className="ux-summary-avatar">
        {character.photoDataUrl ? (
          <img
            src={character.photoDataUrl}
            alt=""
            style={{ width: "100%", height: "100%", objectFit: "cover", borderRadius: "inherit" }}
          />
        ) : (
          character.name.trim().slice(0, 1) || "A"
        )}
      </span>
      <div>
        <small>{label}</small>
        <h2>{character.name}</h2>
        <p>
          {relationshipLabel ? `${relationshipLabel} · ` : ""}
          {t.journey.profile.ready}
        </p>
      </div>
      <div className="ux-summary-actions">
        <button type="button" onClick={onEdit}>
          <Pencil aria-hidden="true" />
          {t.common.actions.change}
        </button>
        {onRemove ? (
          <button type="button" className="danger" onClick={onRemove}>
            <Trash2 aria-hidden="true" />
            {t.common.actions.remove}
          </button>
        ) : null}
      </div>
    </article>
  );
}
