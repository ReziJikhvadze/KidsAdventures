import { Camera, Check, Loader2, Lock, Pencil, Plus, Trash2, TriangleAlert, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import { BirthDateField } from "@/components/adventrya/journey/BirthDateField";
import { SparkleIcon } from "@/components/adventrya/landing/icons";
import { checkPortrait, type PortraitRejection } from "@/lib/api/portraits";
import { BOOK_LANGUAGES, type BookLanguage, useT } from "@/lib/i18n";
import { preparePortrait } from "@/lib/images/preparePortrait";
import type { CharacterGender, EyeColor } from "@/lib/api/types";
import { emptyCharacter, type DraftCharacter, type JourneyDraft } from "@/lib/journey/draft";

const MAX_CHARACTERS = 3;
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

export function ProfileStage({ draft, onChange, onContinue }: Props) {
  const t = useT();
  const [editingId, setEditingId] = useState<string | null>(() => entryEditingId(draft.characters));
  const [error, setError] = useState<string | null>(null);

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
  const remainingSlots = MAX_CHARACTERS - draft.characters.length;

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

  const saveEditing = () => {
    if (!editing) return;
    const message = validateCharacter(editing);
    if (message) {
      setError(message);
      return;
    }
    setError(null);
    setEditingId(null);
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

  const addSupporting = () => {
    if (remainingSlots <= 0 || editing) {
      if (editing) setError(copy.validation.finishEditing);
      return;
    }
    const next = emptyCharacter(false);
    onChange((prev) => ({ ...prev, characters: [...prev.characters, next] }));
    setEditingId(next.localId);
    setError(null);
  };

  const handleContinue = () => {
    if (editing) {
      setError(copy.validation.finishEditing);
      return;
    }
    const primary = draft.characters.find((c) => c.isPrimary);
    if (!primary) {
      setError(copy.validation.nameRequired);
      return;
    }
    const message = validateCharacter(primary);
    if (message) {
      setError(message);
      setEditingId(primary.localId);
      return;
    }
    for (const supporting of draft.characters.filter((c) => !c.isPrimary)) {
      const supportingError = validateCharacter(supporting);
      if (supportingError) {
        setError(supportingError);
        setEditingId(supporting.localId);
        return;
      }
    }
    setError(null);
    onContinue();
  };

  return (
    <section className="ux-profile-stage">
      <header className="ux-stage-heading">
        <p className="eyebrow">
          <SparkleIcon />
          {copy.profile.eyebrow}
        </p>
        <h1>{copy.profile.title}</h1>
        <p>{copy.profile.lead}</p>
      </header>

      <div className="ux-character-stack">
        {/*
          The book-language switcher went. It was a second language control on a page that
          already has one in the header, asking the same question in different words — and the
          book now simply follows the interface it was ordered in.
        */}

        {draft.characters.map((character, index) => {
          if (editingId === character.localId) {
            return (
              <CharacterEditor
                key={character.localId}
                character={character}
                index={index}
                onChange={(patch) => updateCharacter(character.localId, patch)}
                onSave={saveEditing}
                onCancel={() => {
                  if (!character.name.trim() && !character.isPrimary) {
                    removeCharacter(character.localId);
                  } else {
                    setEditingId(null);
                  }
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
              onEdit={() => {
                if (editing) {
                  setError(copy.validation.finishEditing);
                  return;
                }
                setEditingId(character.localId);
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
          The one special wish. It used to sit on the world picker, where the only question being
          asked was which of six places — a free-text box about the story, next to a map. It is a
          question about the story, so it belongs with the other things we ask about the child.
        */}
        <label className="ux-wish-field">
          <span>{t.journey.firstMap.wishLabel}</span>
          <input
            value={draft.storyNotes}
            placeholder={t.journey.firstMap.wishPlaceholder}
            onChange={(e) => onChange({ storyNotes: e.target.value })}
          />
          <small>{t.journey.firstMap.wishHint}</small>
        </label>

        {error ? <p className="ux-form-error">{error}</p> : null}

        <div className="ux-profile-footer">
          <p className="privacy-inline">
            <Lock aria-hidden="true" />
            {copy.profile.privacyNote}
          </p>
          <button className="button journey-primary" type="button" onClick={handleContinue}>
            {copy.profile.continue}
          </button>
        </div>
      </div>
    </section>
  );
}

function CharacterEditor({
  character,
  index,
  onChange,
  onSave,
  onCancel,
}: {
  character: DraftCharacter;
  index: number;
  onChange: (patch: Partial<DraftCharacter>) => void;
  onSave: () => void;
  onCancel: () => void;
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
    onChange({ photoDataUrl: null, photoReady: false });

    void (async () => {
      try {
        // Downscaled before it is stored, never at full camera resolution. A phone photo plus
        // base64 overhead exceeds the upload limit, and an oversized request is rejected before
        // our code runs — so it comes back with no CORS headers and the browser blames CORS.
        const { dataUrl } = await preparePortrait(file);

        // Asked here rather than at generation: this is the last moment the answer costs one
        // small call, and the only moment a parent can still pick a different photo.
        const verdict = await checkPortrait(dataUrl);
        if (ticket !== checkRef.current) return;

        if (verdict.accepted) {
          onChange({ photoDataUrl: dataUrl, photoReady: true });
        } else {
          setRejection(verdict.reason);
        }
      } catch {
        // preparePortrait only throws when the file cannot be read at all — which used to end
        // here silently, leaving a parent looking at a button that would not go ready and no
        // word about why.
        if (ticket !== checkRef.current) return;
        setRejection("unreadable");
      } finally {
        if (ticket === checkRef.current) setChecking(false);
      }
    })();
  };

  // The upload box says one thing at a time, and the state it is in decides which.
  let photoIcon = <Camera />;
  if (checking) photoIcon = <Loader2 className="ux-photo-spinner" />;
  else if (rejection) photoIcon = <TriangleAlert />;
  else if (character.photoReady) photoIcon = <Check />;

  let photoLabel = copy.characterForm.photoRequired;
  if (checking) photoLabel = copy.characterForm.photoChecking;
  else if (character.photoReady) photoLabel = copy.characterForm.photoReady;

  return (
    <div className={`ux-character-form ${character.isPrimary ? "" : "ux-second-character-form"}`}>
      <div className="ux-form-intro">
        <span className="ux-step-token">{String(index + 1).padStart(2, "0")}</span>
        <div>
          <small>{character.isPrimary ? copy.profile.primaryCharacter : "დამატებითი"}</small>
          <h2>{title}</h2>
        </div>
      </div>

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

          <fieldset className="choice-fieldset">
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
          className={`ux-photo-upload ${character.photoReady ? "ready" : ""} ${
            rejection ? "rejected" : ""
          }`}
        >
          <span aria-hidden="true">{photoIcon}</span>
          <small>{photoLabel}</small>
          {character.photoDataUrl ? (
            <img
              src={character.photoDataUrl}
              alt=""
              style={{
                width: 72,
                height: 72,
                objectFit: "cover",
                borderRadius: 16,
                marginTop: 10,
              }}
            />
          ) : null}

          {/*
            The examples go where the decision is made, and only while it is still open. Once a
            portrait is accepted they are answering a question nobody is asking any more.
          */}
          {/*
            The good/bad photo pair is switched off until the two photographs exist.

            It was written to remove itself when the files are missing, and it does — but only
            after the browser has asked for both and been given two 404s, on every visit to the
            form, for a block nobody ever sees. Rendering nothing is the same result without the
            failed requests. Put good.jpg and bad.jpg in public/adventrya/photo-guide/ and put
            <PhotoGuide /> back.
          */}

          {/*
            role="status" because the refusal arrives a second after the file dialog closed —
            long after focus moved on — so a screen reader has to be told it appeared.
          */}
          {rejection ? (
            <p className="ux-photo-rejection" role="status">
              {copy.characterForm.photoRejected[rejection]}
            </p>
          ) : null}

          <button type="button" disabled={checking} onClick={() => fileRef.current?.click()}>
            {character.photoReady
              ? copy.characterForm.photoReplace
              : copy.characterForm.photoUpload}
          </button>
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
        No "save changes" here any more.

        It closed the editor and nothing else — the draft lives in memory until checkout — so it
        promised a save that never happened and put a second primary-looking button on a form
        whose only real action is at the bottom. Done is the one button that makes the book.
      */}
      <div className="ux-form-actions">
        <button className="button ux-inline-link" type="button" onClick={onSave}>
          {t.common.actions.close}
        </button>
        {character.name.trim() || !character.isPrimary ? (
          <button className="ux-inline-link" type="button" onClick={onCancel}>
            <X aria-hidden="true" />
            {t.common.actions.cancel}
          </button>
        ) : null}
      </div>
    </div>
  );
}

/**
 * Two real photographs, side by side, of what is wanted and what is not.
 *
 * The upload box used to describe a good portrait in words, which is the one thing a photo is
 * better at than a sentence: "the face fills the frame" is abstract until it is shown next to a
 * child standing across a dark room.
 *
 * The files live in `public/adventrya/photo-guide/`. Until both are in place the block removes
 * itself rather than showing a parent two broken frames on the form that asks for their photo.
 */
const PHOTO_GUIDE_SHOTS = {
  good: "/adventrya/photo-guide/good.jpg",
  bad: "/adventrya/photo-guide/bad.jpg",
};

function PhotoGuide() {
  const guide = useT().journey.characterForm.photoGuide;
  const [missing, setMissing] = useState(false);

  if (missing) return null;

  return (
    <figure className="ux-photo-guide">
      <figcaption>{guide.title}</figcaption>
      <div className="ux-photo-guide-pair">
        <div className="ux-photo-guide-shot good">
          {/* The label and the reason beside it carry the meaning, so the image is decorative. */}
          <img src={PHOTO_GUIDE_SHOTS.good} alt="" onError={() => setMissing(true)} />
          <span>
            <Check aria-hidden="true" />
            {guide.goodLabel}
          </span>
          <small>{guide.goodReason}</small>
        </div>
        <div className="ux-photo-guide-shot bad">
          <img src={PHOTO_GUIDE_SHOTS.bad} alt="" onError={() => setMissing(true)} />
          <span>
            <X aria-hidden="true" />
            {guide.badLabel}
          </span>
          <small>{guide.badReason}</small>
        </div>
      </div>
    </figure>
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
      <span className="ux-summary-avatar">{character.name.trim().slice(0, 1) || "A"}</span>
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
