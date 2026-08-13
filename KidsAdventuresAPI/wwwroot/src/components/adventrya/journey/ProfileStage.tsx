import { Camera, Check, Lock, Pencil, Plus, Trash2, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import { BirthDateField } from "@/components/adventrya/journey/BirthDateField";
import { SparkleIcon } from "@/components/adventrya/landing/icons";
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
        <div className="ux-book-preferences" aria-label={copy.bookSettings.title}>
          <div>
            <strong>{copy.bookSettings.languageQuestion}</strong>
          </div>
          <div className="ux-language-switcher" role="group">
            {BOOK_LANGUAGES.map((lang) => (
              <button
                key={lang.code}
                type="button"
                className={draft.bookLanguage === lang.code ? "selected" : ""}
                onClick={() => onChange({ bookLanguage: lang.code as BookLanguage })}
              >
                {lang.label}
              </button>
            ))}
          </div>
        </div>

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

        {!editing && remainingSlots > 0 ? (
          <button className="ux-add-character" type="button" onClick={addSupporting}>
            <span>
              <Plus aria-hidden="true" />
            </span>
            <div>
              <strong>{copy.profile.addCharacterTitle}</strong>
              <small>
                {copy.profile.addCharacterHint}
                {remainingSlots} {copy.profile.addCharacterLimit}
              </small>
            </div>
          </button>
        ) : null}

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

  const title = character.isPrimary
    ? copy.profile.primaryCharacter
    : copy.profile.nthCharacter(index + 1);

  const onPhoto = (file: File | null) => {
    if (!file) return;
    // Downscaled before it is stored, never at full camera resolution. A phone photo plus
    // base64 overhead exceeds the upload limit, and an oversized request is rejected before
    // our code runs — so it comes back with no CORS headers and the browser blames CORS.
    void preparePortrait(file)
      .then(({ dataUrl }) => onChange({ photoDataUrl: dataUrl, photoReady: true }))
      .catch(() => onChange({ photoDataUrl: null, photoReady: false }));
  };

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
              <div className="ux-segmented-control">
                {(["girl", "boy"] as CharacterGender[]).map((gender) => (
                  <button
                    key={gender}
                    type="button"
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

        <div className={`ux-photo-upload ${character.photoReady ? "ready" : ""}`}>
          <span aria-hidden="true">{character.photoReady ? <Check /> : <Camera />}</span>
          <small>
            {character.photoReady
              ? copy.characterForm.photoReady
              : copy.characterForm.photoRequired}
          </small>
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
          <button type="button" onClick={() => fileRef.current?.click()}>
            {character.photoReady
              ? copy.characterForm.photoReplace
              : copy.characterForm.photoUpload}
          </button>
          <input
            ref={fileRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            hidden
            onChange={(e) => onPhoto(e.target.files?.[0] ?? null)}
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
      <div className="ux-form-actions">
        <button className="button journey-primary" type="button" onClick={onSave}>
          {character.name.trim() ? copy.profile.saveChanges : copy.profile.saveCharacter}
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
