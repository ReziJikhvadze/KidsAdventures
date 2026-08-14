import { useT } from "@/lib/i18n";

/** What Beki is doing, which is only ever a reaction to what the parent just did. */
export type BekiMood = "greeting" | "peek" | "chosen";

/**
 * One file per mood. All three point at the same picture today because only the one
 * canonical Beki has been drawn; the moods are already wired through so that swapping in
 * the pose sheet is an edit to this object and nothing else.
 *
 * See design/world-map-art-brief.md, section 4, for the poses being drawn.
 */
const BEKI_POSE: Record<BekiMood, string> = {
  greeting: "/adventrya/beki-cutout.webp",
  peek: "/adventrya/beki-cutout.webp",
  chosen: "/adventrya/beki-cutout.webp",
};

type Props = {
  mood: BekiMood;
  /** The world under the cursor, when there is one — Beki names it back to you. */
  peekTheme?: string;
};

/**
 * The guide, and the one thing on this screen that talks.
 *
 * A child cannot read the heading and a parent does not want a paragraph, so Beki says one
 * short line at a time and changes it as they move. He is the reason a five-year-old leaning
 * over the parent's shoulder has something to look at that looks back.
 */
export function BekiGuide({ mood, peekTheme }: Props) {
  const copy = useT().journey.firstMap.beki;
  const line =
    mood === "chosen"
      ? copy.chosen
      : mood === "peek" && peekTheme
        ? copy.peek(peekTheme)
        : copy.greeting;

  return (
    <div className={`beki-guide is-${mood}`}>
      {/*
        The bubble is keyed on the line so React swaps the node when the words change,
        which restarts the entrance animation. Without the key the text would change
        underneath a bubble that never moved, and the change would go unnoticed.
      */}
      <p className="beki-bubble" key={line} aria-live="polite">
        {line}
      </p>
      <img className="beki-sprite" src={BEKI_POSE[mood]} alt={copy.alt} width={420} height={512} />
    </div>
  );
}
