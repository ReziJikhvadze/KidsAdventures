import { Camera, Lock, Palette } from "lucide-react";
import type { PersonalizationType } from "@/lib/avatar/config";
import { presetsForTheme } from "@/lib/avatar/presets";
import type { StoryThemeId } from "@/lib/themes";
import { PhotoTransformSlider } from "@/components/avatar/PhotoTransformSlider";

type PersonalizationForkProps = {
  childName: string;
  value: PersonalizationType | null;
  themeId?: StoryThemeId | null;
  onChange: (value: PersonalizationType) => void;
};

/**
 * Trust-first personalization fork:
 * photo path sells the emotional payoff + clear privacy controls;
 * avatar path is the no-upload high-status alternative.
 */
export function PersonalizationFork({
  childName,
  value,
  themeId,
  onChange,
}: PersonalizationForkProps) {
  const name = childName.trim() || "your child";
  const archetypeLabels = presetsForTheme(themeId).map((p) => {
    const parts = p.label.split(" ");
    return parts[parts.length - 1] ?? p.label;
  });

  return (
    <div className="mt-6 rounded-2xl border border-border bg-secondary/30 p-4 sm:p-5">
      <p className="text-sm font-semibold text-center sm:text-left">
        How would you like {name} to appear in the story?
      </p>
      <p className="text-xs text-muted-foreground mt-1 text-center sm:text-left">
        Photo gives the strongest “that’s them” moment. Prefer not to? Build a hero in three taps —
        still beautiful on every page.
      </p>

      <div className="mt-4 grid grid-cols-1 gap-3 lg:grid-cols-2">
        {/* PHOTO — reward first, trust second */}
        <button
          type="button"
          onClick={() => onChange("photo")}
          className={`rounded-2xl border p-4 text-left transition ${
            value === "photo"
              ? "border-primary bg-primary/5 ring-4 ring-primary/10"
              : "border-border bg-card hover:border-foreground/30"
          }`}
        >
          <div className="flex items-start gap-3">
            <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/10">
              <Camera className="h-5 w-5 text-primary" />
            </span>
            <div className="min-w-0 flex-1">
              <div className="text-sm font-semibold">Turn a photo into their story hero</div>
              <div className="mt-0.5 text-xs text-muted-foreground">
                Hair, eyes &amp; smile → Pixar-style character on every page.
              </div>
              <div className="mt-2 inline-flex items-center gap-1.5 text-[11px] font-semibold text-emerald-800 dark:text-emerald-300">
                <Lock className="h-3.5 w-3.5 shrink-0" />
                100% Private
              </div>
            </div>
          </div>

          <div className="mt-3">
            <PhotoTransformSlider />
          </div>
          <p className="mt-2 text-center text-[11px] font-medium text-foreground/70">
            Drag to reveal the Pixar upgrade
          </p>

          <p className="mt-2 text-[11px] leading-snug text-muted-foreground">
            Photos are processed to create the artwork. We never share them — delete anytime from
            your account in one click.
          </p>
        </button>

        {/* AVATAR — no-upload path */}
        <button
          type="button"
          onClick={() => onChange("avatar")}
          className={`rounded-2xl border p-4 text-left transition ${
            value === "avatar"
              ? "border-primary bg-primary/5 ring-4 ring-primary/10"
              : "border-border bg-card hover:border-foreground/30"
          }`}
        >
          <div className="flex items-start gap-3">
            <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/10">
              <Palette className="h-5 w-5 text-primary" />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold">Build a hero — no photo needed</span>
                <span className="inline-flex items-center gap-1 rounded-full bg-secondary px-2 py-0.5 text-[10px] font-semibold text-muted-foreground">
                  <Lock className="h-3 w-3" />
                  Zero upload
                </span>
              </div>
              <div className="mt-0.5 text-xs text-muted-foreground">
                Tap one of three theme-ready looks. Same illustrated quality — without sharing a
                picture.
              </div>
            </div>
          </div>

          <div className="mt-4 grid grid-cols-3 gap-2">
            {archetypeLabels.map((label, i) => (
              <div
                key={`${label}-${i}`}
                className="rounded-xl border border-border/80 px-2 py-3 text-center"
                style={{
                  backgroundImage: [
                    "linear-gradient(165deg, #1e1b4b 0%, #312e81 40%, #1e3a5f 100%)",
                    "linear-gradient(165deg, #0f172a 0%, #1e3a5f 50%, #334155 100%)",
                    "linear-gradient(165deg, #2e1065 0%, #4c1d95 45%, #1e1b4b 100%)",
                  ][i],
                }}
              >
                <div className="mx-auto mb-1.5 h-9 w-9 rounded-full bg-gradient-to-b from-[#f7e4d4] to-[#e8b892] shadow-md ring-2 ring-white/30" />
                <div className="truncate text-[10px] font-semibold text-white/90">{label}</div>
              </div>
            ))}
          </div>
          <p className="mt-2 text-center text-[11px] text-muted-foreground">
            3-tap archetypes · optional fine-tune
          </p>
        </button>
      </div>
    </div>
  );
}
