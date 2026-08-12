import { useEffect, useId, useState } from "react";

import { useT } from "@/lib/i18n";

/**
 * A child's date of birth, as day, month and year.
 *
 * It was a bare `<input type="date">`, which on a Georgian page rendered "mm/dd/yyyy" — an
 * American order the parent filling it in does not use — and hid the year behind a calendar that
 * opens on this month. A birth date is the one date nobody wants a calendar for: the year is the
 * hard part, and paging back six years a month at a time is the worst way to reach it.
 *
 * Three selects instead. Each of day, month and year is one tap, the month is written in words
 * so no order can be misread, and on a phone these are the native wheels rather than a grid of
 * numbers drawn at twelve pixels. No library, and the value it stores is unchanged: YYYY-MM-DD.
 */
export function BirthDateField({
  value,
  onChange,
  label,
}: {
  value: string;
  onChange: (value: string) => void;
  label: string;
}) {
  const t = useT();
  const groupId = useId();

  /*
    The three parts are held here, not derived from `value` alone.

    A date is only a date once all three are chosen, so the stored value is empty until then —
    and a field that reads itself back from that value forgets each choice the moment it is made.
    Pick a year and it vanishes. These keep what has been chosen so far and hand the parent
    upward only when the date is whole.
  */
  const [parts, setParts] = useState(() => splitIso(value));

  // Re-sync when the character being edited changes underneath us, but never clobber a
  // half-finished selection with the empty string it necessarily produces.
  useEffect(() => {
    const incoming = splitIso(value);
    if (incoming[0] || incoming[1] || incoming[2]) setParts(incoming);
    else if (!value) setParts((prev) => (prev[0] || prev[1] || prev[2] ? prev : incoming));
  }, [value]);

  const [year, month, day] = parts;
  const today = new Date();
  const thisYear = today.getFullYear();

  // A picture book's audience. Wide enough for a parent buying ahead or for an older sibling,
  // and short enough that the list is still one flick on a phone.
  const years: number[] = [];
  for (let y = thisYear; y >= thisYear - 18; y--) years.push(y);

  const daysInMonth = year && month ? new Date(year, month, 0).getDate() : 31;
  const days: number[] = [];
  for (let d = 1; d <= daysInMonth; d++) days.push(d);

  const emit = (next: { y?: number | null; m?: number | null; d?: number | null }) => {
    const y = next.y === undefined ? year : next.y;
    const m = next.m === undefined ? month : next.m;
    let d = next.d === undefined ? day : next.d;

    // 31 January then February: clamp rather than silently emit an invalid date.
    if (y && m && d) d = Math.min(d, new Date(y, m, 0).getDate());

    setParts([y, m, d]);
    onChange(y && m && d ? `${y}-${pad(m)}-${pad(d)}` : "");
  };

  return (
    <fieldset className="field ux-birthdate" aria-describedby={undefined}>
      <legend id={groupId}>{label}</legend>
      <div className="ux-birthdate-row">
        <select
          aria-label={t.common.date.day}
          value={day ?? ""}
          onChange={(e) => emit({ d: e.target.value ? Number(e.target.value) : null })}
        >
          <option value="">{t.common.date.day}</option>
          {days.map((d) => (
            <option key={d} value={d}>
              {d}
            </option>
          ))}
        </select>

        <select
          aria-label={t.common.date.month}
          value={month ?? ""}
          onChange={(e) => emit({ m: e.target.value ? Number(e.target.value) : null })}
        >
          <option value="">{t.common.date.month}</option>
          {t.common.date.months.map((name, index) => (
            <option key={name} value={index + 1}>
              {name}
            </option>
          ))}
        </select>

        <select
          aria-label={t.common.date.year}
          value={year ?? ""}
          onChange={(e) => emit({ y: e.target.value ? Number(e.target.value) : null })}
        >
          <option value="">{t.common.date.year}</option>
          {years.map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
      </div>
    </fieldset>
  );
}

const pad = (n: number) => String(n).padStart(2, "0");

/** "2019-04-07" to [2019, 4, 7]; anything else to nulls. */
function splitIso(value: string): [number | null, number | null, number | null] {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec((value ?? "").trim());
  if (!match) return [null, null, null];
  return [Number(match[1]), Number(match[2]), Number(match[3])];
}
