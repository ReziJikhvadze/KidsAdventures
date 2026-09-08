import { BEKI_MARK_COLOR_URL, BEKI_MARK_WHITE_URL, BRAND_NAME } from "@/lib/brand";

/**
 * Which finish of the lockup a bar wants — decided by what is behind it, not by which page it
 * is. Every header on the site is dark except one: the parent's space, which is a white bar
 * over cream cards, and which the white mark would disappear into.
 */
export type BekiMarkTone = "white" | "color";

type BekiMarkProps = {
  tone?: BekiMarkTone;
  className?: string;
  /**
   * The mark stands beside something that already names the page — a link with its own
   * aria-label, a heading — so it is announced once rather than twice.
   */
  decorative?: boolean;
};

/**
 * The Beki lockup: the chameleon and the word, as one image.
 *
 * Sized by height alone. The intrinsic width/height attributes are the trimmed file's own
 * 720x256, so the box is reserved before the PNG arrives and nothing under the bar jumps when
 * it does; `width: auto` in CSS keeps the ratio whatever height a slot asks for.
 */
export function BekiMark({ tone = "white", className = "", decorative = false }: BekiMarkProps) {
  return (
    <img
      className={`beki-mark ${className}`.trim()}
      src={tone === "color" ? BEKI_MARK_COLOR_URL : BEKI_MARK_WHITE_URL}
      width={720}
      height={256}
      draggable={false}
      {...(decorative ? { alt: "", "aria-hidden": true as const } : { alt: BRAND_NAME })}
    />
  );
}
