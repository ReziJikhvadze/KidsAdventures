import { useT } from "@/lib/i18n";

type Props = {
  /** Pixels. 16 inside a control, 28 beside a line of text, 56+ alone on a page. */
  size?: number;
  /**
   * What is being waited for, announced to a screen reader.
   *
   * Omit it when the loader sits next to text that already says so — two voices reading the
   * same wait is worse than one. Given, it makes this a live region; omitted, the mark is
   * hidden and whatever it stands beside does the talking.
   */
  label?: string;
  className?: string;
};

/**
 * The site's loader: the lit pendant on Beki's chest, turning.
 *
 * One object at every size, so a wait looks like the same product whether it is a button, a
 * dialog or a whole page. It replaced three near-identical rotating rings and, in more places
 * than that, nothing at all.
 *
 * Not for the preview or a book being written. Both of those already show the real thing
 * arriving — spreads as they are drawn — and a mascot spinning on top of that would be a
 * poorer answer than the work it was covering.
 */
export function BekiLoader({ size = 28, label, className }: Props) {
  const t = useT();
  const announced = label === undefined ? undefined : label || t.common.states.loading;

  return (
    <span
      className={className ? `beki-loader ${className}` : "beki-loader"}
      style={{ ["--beki-loader-size" as string]: `${size}px` }}
      {...(announced
        ? { role: "status", "aria-live": "polite" as const }
        : { "aria-hidden": true })}
    >
      <span className="beki-loader-mote" />
      <span className="beki-loader-mote" />
      <span className="beki-loader-mote" />
      <span className="beki-loader-core" />
      {announced ? <span className="sr-only">{announced}</span> : null}
    </span>
  );
}
