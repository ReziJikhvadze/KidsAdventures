import { useEffect, useMemo, useState } from "react";
import { ChevronDown, Sparkles } from "lucide-react";
import type { AvatarConfig, AvatarGender, AvatarOption } from "@/lib/avatar/config";
import {
  EARRING_OPTIONS,
  EYEBROW_OPTIONS,
  EYE_OPTIONS,
  FEATURE_OPTIONS,
  GENDER_OPTIONS,
  GLASSES_OPTIONS,
  HAIR_COLOR_OPTIONS,
  MOUTH_OPTIONS,
  OUTFIT_COLOR_OPTIONS,
  OUTFIT_OPTIONS,
  SKIN_COLOR_OPTIONS,
  normalizeHairForGender,
} from "@/lib/avatar/config";
import {
  applyPreset,
  hairCategoryFromStyle,
  hairForCategory,
  HAIR_CATEGORY_OPTIONS,
  presetsForTheme,
  type HairCategoryId,
} from "@/lib/avatar/presets";
import { STORY_THEMES, type StoryThemeId } from "@/lib/themes";
import { PixarAvatarPreview } from "@/components/avatar/PixarAvatarPreview";

type AvatarBuilderProps = {
  config: AvatarConfig;
  childName: string;
  themeId?: StoryThemeId | null;
  onChange: (config: AvatarConfig) => void;
};

type FineTuneTab = "face" | "hair" | "look" | "body" | "extras";

const FINE_TABS: { id: FineTuneTab; label: string }[] = [
  { id: "face", label: "Face" },
  { id: "hair", label: "Hair" },
  { id: "look", label: "Look" },
  { id: "body", label: "Body" },
  { id: "extras", label: "Extras" },
];

function ChipRow({
  label,
  options,
  value,
  onSelect,
  showHint = false,
}: {
  label: string;
  options: AvatarOption[];
  value: string;
  onSelect: (id: string) => void;
  showHint?: boolean;
}) {
  return (
    <div>
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="flex max-h-40 flex-wrap gap-2 overflow-y-auto pr-1">
        {options.map((opt) => {
          const active = value === opt.id;
          return (
            <button
              key={opt.id}
              type="button"
              title={opt.hint ?? opt.label}
              onClick={() => onSelect(opt.id)}
              className={`inline-flex flex-col items-start gap-0.5 rounded-xl border px-3 py-2 text-left transition ${
                active
                  ? "border-primary bg-primary text-primary-foreground shadow-sm"
                  : "border-border bg-background hover:border-foreground/30"
              }`}
            >
              <span className="text-xs font-medium">{opt.label}</span>
              {showHint && opt.hint && (
                <span
                  className={`text-[10px] ${active ? "text-primary-foreground/80" : "text-muted-foreground"}`}
                >
                  {opt.hint}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function SwatchGrid({
  label,
  options,
  value,
  onSelect,
}: {
  label: string;
  options: AvatarOption[];
  value: string;
  onSelect: (id: string) => void;
}) {
  return (
    <div>
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="flex flex-wrap gap-2.5">
        {options.map((opt) => {
          const active = value === opt.id;
          return (
            <button
              key={opt.id}
              type="button"
              title={opt.label}
              aria-label={opt.label}
              aria-pressed={active}
              onClick={() => onSelect(opt.id)}
              className={`relative h-9 w-9 rounded-full border-2 transition ${
                active
                  ? "scale-110 border-primary ring-4 ring-primary/20"
                  : "border-white shadow-sm hover:scale-105"
              }`}
              style={{ background: opt.swatch }}
            />
          );
        })}
      </div>
    </div>
  );
}

export function AvatarBuilder({ config, childName, themeId, onChange }: AvatarBuilderProps) {
  const [fineTuneOpen, setFineTuneOpen] = useState(false);
  const [tab, setTab] = useState<FineTuneTab>("hair");
  const [activePresetId, setActivePresetId] = useState<string | null>(null);

  const presets = useMemo(() => presetsForTheme(themeId), [themeId]);
  const hairCategory = hairCategoryFromStyle(config.hair);

  // Highlight a preset when the current config matches one (e.g. after auto-apply).
  useEffect(() => {
    const match = presets.find(
      (p) =>
        p.config.hair === config.hair &&
        p.config.outfit === config.outfit &&
        p.config.outfitColor === config.outfitColor &&
        p.config.gender === config.gender,
    );
    setActivePresetId(match?.id ?? null);
  }, [presets, config.hair, config.outfit, config.outfitColor, config.gender]);

  const patch = (partial: Partial<AvatarConfig>) => {
    let next: AvatarConfig = { ...config, ...partial, library: "adventurer" };
    if (partial.gender) {
      next = normalizeHairForGender(next);
    }
    setActivePresetId(null);
    onChange(next);
  };

  const pickPreset = (presetId: string) => {
    const preset = presets.find((p) => p.id === presetId);
    if (!preset) return;
    setActivePresetId(presetId);
    onChange(applyPreset(preset));
  };

  const pickHairCategory = (category: HairCategoryId) => {
    patch({ hair: hairForCategory(category, config.gender) });
  };

  const themeArt = themeId ? STORY_THEMES.find((t) => t.id === themeId)?.image : undefined;
  const cinematicStage = themeId === "space";

  return (
    <div className="mt-4 overflow-hidden rounded-2xl border border-border bg-card shadow-sm animate-rise">
      <div
        className={`relative border-b border-border/70 px-3 pb-4 pt-4 sm:px-5 ${
          cinematicStage ? "avatar-stage-cinematic" : "bg-gradient-to-b from-sky-50 via-violet-50/40 to-amber-50/30"
        }`}
        style={
          cinematicStage && themeArt
            ? {
                backgroundImage: `linear-gradient(180deg, rgb(15 23 42 / 0.55), rgb(15 23 42 / 0.75)), url(${themeArt})`,
                backgroundSize: "cover",
                backgroundPosition: "center",
              }
            : undefined
        }
      >
        <PixarAvatarPreview config={config} childName={childName} size="hero" />
      </div>

      <div className="space-y-5 p-4 sm:p-5">
        <div>
          <div className="mb-1 flex items-center gap-2">
            <Sparkles className="h-3.5 w-3.5 text-primary" />
            <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Pick a starter look
            </div>
          </div>
          <p className="mb-3 text-xs text-muted-foreground">
            Three cinematic heroes
            {themeId ? ` for ${themeId}` : ""} — one tap, then fine-tune if you want.
          </p>
          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-3">
            {presets.map((preset) => {
              const active = activePresetId === preset.id;
              const mini = applyPreset(preset);
              return (
                <button
                  key={preset.id}
                  type="button"
                  onClick={() => pickPreset(preset.id)}
                  className={`avatar-preset-card group overflow-hidden rounded-2xl border text-left transition ${
                    active
                      ? "border-primary ring-4 ring-primary/15"
                      : "border-border hover:border-foreground/25"
                  }`}
                >
                  <div
                    className="avatar-preset-stage relative h-[7.5rem] overflow-hidden"
                    style={
                      themeArt
                        ? {
                            backgroundImage: `linear-gradient(180deg, rgb(15 23 42 / 0.35), rgb(15 23 42 / 0.65)), url(${themeArt})`,
                            backgroundSize: "cover",
                            backgroundPosition: "center",
                          }
                        : { background: `linear-gradient(165deg, ${preset.tint}, #fff 85%)` }
                    }
                  >
                    <div className="absolute inset-x-0 bottom-[-6%] flex justify-center scale-[0.92]">
                      <PixarAvatarPreview
                        config={mini}
                        childName=""
                        size="compact"
                        className="pointer-events-none"
                      />
                    </div>
                    <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-black/25 via-transparent to-white/10" />
                  </div>
                  <div className="bg-card px-3 py-2.5">
                    <div className="text-sm font-semibold">{preset.label}</div>
                    <div className="mt-0.5 text-[11px] text-muted-foreground">{preset.hint}</div>
                  </div>
                </button>
              );
            })}
          </div>
        </div>

        <div>
          <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Character
          </div>
          <div className="grid grid-cols-2 gap-2">
            {GENDER_OPTIONS.map((opt) => {
              const active = config.gender === opt.id;
              return (
                <button
                  key={opt.id}
                  type="button"
                  onClick={() => patch({ gender: opt.id as AvatarGender })}
                  className={`rounded-xl border px-3 py-3 text-left transition ${
                    active
                      ? "border-primary bg-primary/5 ring-4 ring-primary/10"
                      : "border-border bg-background hover:border-foreground/30"
                  }`}
                >
                  <div className="text-sm font-semibold">{opt.label}</div>
                  <div className="text-[11px] text-muted-foreground">
                    {opt.id === "girl" ? "Longer looks first" : "Shorter looks first"}
                  </div>
                </button>
              );
            })}
          </div>
        </div>

        <div>
          <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Hair
          </div>
          <div className="grid grid-cols-4 gap-2">
            {HAIR_CATEGORY_OPTIONS.map((opt) => {
              const active = hairCategory === opt.id;
              return (
                <button
                  key={opt.id}
                  type="button"
                  onClick={() => pickHairCategory(opt.id)}
                  className={`rounded-xl border px-2 py-2.5 text-center transition ${
                    active
                      ? "border-primary bg-primary text-primary-foreground shadow-sm"
                      : "border-border bg-background hover:border-foreground/30"
                  }`}
                >
                  <div className="text-xs font-semibold">{opt.label}</div>
                  <div
                    className={`text-[10px] mt-0.5 ${active ? "text-primary-foreground/80" : "text-muted-foreground"}`}
                  >
                    {opt.hint}
                  </div>
                </button>
              );
            })}
          </div>
          <div className="mt-3">
            <SwatchGrid
              label="Hair color"
              options={HAIR_COLOR_OPTIONS}
              value={config.hairColor}
              onSelect={(hairColor) => patch({ hairColor })}
            />
          </div>
        </div>

        <button
          type="button"
          onClick={() => setFineTuneOpen((v) => !v)}
          className="flex w-full items-center justify-between rounded-xl border border-border bg-secondary/40 px-4 py-3 text-sm font-semibold transition hover:bg-secondary/60"
        >
          Fine-tune details
          <ChevronDown className={`h-4 w-4 transition ${fineTuneOpen ? "rotate-180" : ""}`} />
        </button>

        {fineTuneOpen && (
          <div className="space-y-4 animate-rise">
            <div className="flex gap-1 rounded-xl bg-secondary/60 p-1">
              {FINE_TABS.map((t) => (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setTab(t.id)}
                  className={`flex-1 rounded-lg px-2 py-2 text-xs font-semibold transition sm:text-sm ${
                    tab === t.id
                      ? "bg-card text-foreground shadow-sm"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>

            <div className="min-h-[160px] space-y-4">
              {tab === "face" && (
                <>
                  <SwatchGrid
                    label="Skin tone"
                    options={SKIN_COLOR_OPTIONS}
                    value={config.skinColor}
                    onSelect={(skinColor) => patch({ skinColor })}
                  />
                  <ChipRow
                    label="Face feature"
                    options={FEATURE_OPTIONS}
                    value={config.features}
                    onSelect={(features) => patch({ features })}
                  />
                </>
              )}

              {tab === "hair" && (
                <p className="text-xs text-muted-foreground">
                  Hair style &amp; color are above — use Short / Long / Curly / Tied for a clean look.
                </p>
              )}

              {tab === "look" && (
                <>
                  <ChipRow
                    label="Eyes"
                    options={EYE_OPTIONS}
                    value={config.eyes}
                    onSelect={(eyes) => patch({ eyes })}
                  />
                  <ChipRow
                    label="Eyebrows"
                    options={EYEBROW_OPTIONS}
                    value={config.eyebrows}
                    onSelect={(eyebrows) => patch({ eyebrows })}
                  />
                  <ChipRow
                    label="Mouth / smile"
                    options={MOUTH_OPTIONS}
                    value={config.mouth}
                    onSelect={(mouth) => patch({ mouth })}
                  />
                </>
              )}

              {tab === "body" && (
                <>
                  <ChipRow
                    label="Outfit"
                    options={OUTFIT_OPTIONS}
                    value={config.outfit}
                    onSelect={(outfit) => patch({ outfit })}
                    showHint
                  />
                  <SwatchGrid
                    label="Outfit color"
                    options={OUTFIT_COLOR_OPTIONS}
                    value={config.outfitColor}
                    onSelect={(outfitColor) => patch({ outfitColor })}
                  />
                </>
              )}

              {tab === "extras" && (
                <>
                  <ChipRow
                    label="Glasses"
                    options={GLASSES_OPTIONS}
                    value={config.glasses}
                    onSelect={(glasses) => patch({ glasses })}
                  />
                  <ChipRow
                    label="Earrings"
                    options={EARRING_OPTIONS}
                    value={config.earrings}
                    onSelect={(earrings) => patch({ earrings })}
                  />
                </>
              )}
            </div>
          </div>
        )}

        <p className="text-center text-[11px] text-muted-foreground">
          Your choices become Character DNA for every illustrated page.
        </p>
      </div>
    </div>
  );
}
