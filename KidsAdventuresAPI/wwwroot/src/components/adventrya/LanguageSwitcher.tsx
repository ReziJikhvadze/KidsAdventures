import { useEffect, useRef, useState, type ReactNode } from "react";

import { UI_LOCALES, useLocale, useT, type UiLocale } from "@/lib/i18n";

/**
 * The interface language control.
 *
 * Both headers previously rendered a globe, a label and a chevron as a plain button
 * with no handler — it looked like a dropdown and did nothing. This keeps each
 * header's own classes and icons so the approved styling is unchanged, and adds the
 * behaviour behind it.
 */
export function LanguageSwitcher({
  className,
  globe,
  chevron,
  /** `short` renders "KA"/"EN" for the compact landing header. */
  labelStyle = "full",
}: {
  className: string;
  globe: ReactNode;
  chevron: ReactNode;
  labelStyle?: "full" | "short";
}) {
  const t = useT();
  const { locale, setLocale } = useLocale();
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (!wrapRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const label = (code: UiLocale) =>
    labelStyle === "short"
      ? code.toUpperCase()
      : (UI_LOCALES.find((l) => l.code === code)?.label ?? code);

  const choose = (code: UiLocale) => {
    setLocale(code);
    setOpen(false);
  };

  return (
    <div className="language-menu" ref={wrapRef}>
      <button
        type="button"
        className={className}
        aria-label={t.common.nav.changeLanguage}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        {globe}
        {label(locale)}
        {chevron}
      </button>

      {open ? (
        <ul className="language-menu-list" role="listbox" aria-label={t.common.nav.changeLanguage}>
          {UI_LOCALES.map((option) => (
            <li key={option.code}>
              <button
                type="button"
                role="option"
                aria-selected={option.code === locale}
                className={option.code === locale ? "selected" : ""}
                onClick={() => choose(option.code)}
              >
                {option.label}
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
