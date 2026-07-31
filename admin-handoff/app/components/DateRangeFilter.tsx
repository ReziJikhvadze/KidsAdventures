"use client";

import { useState } from "react";

export type DateRange = {
  from: string;
  to: string;
  label: string;
};

type DateRangeFilterProps = {
  onApply?: (range: DateRange) => void;
  initialFrom?: string;
  initialTo?: string;
  initialLabel?: string;
  label?: string;
};

const presets = [
  { label: "დღეს", from: "2026-07-28", to: "2026-07-28" },
  { label: "7 დღე", from: "2026-07-22", to: "2026-07-28" },
  { label: "30 დღე", from: "2026-06-29", to: "2026-07-28" },
] as const;

export function DateRangeFilter({
  onApply,
  initialFrom = "2026-06-29",
  initialTo = "2026-07-28",
  initialLabel = "30 დღე",
  label = "პერიოდი",
}: DateRangeFilterProps) {
  const [from, setFrom] = useState(initialFrom);
  const [to, setTo] = useState(initialTo);
  const [active, setActive] = useState(initialLabel);

  const apply = (nextFrom = from, nextTo = to, nextLabel = "არჩეული პერიოდი") => {
    setFrom(nextFrom);
    setTo(nextTo);
    setActive(nextLabel);
    onApply?.({ from: nextFrom, to: nextTo, label: nextLabel });
  };

  return (
    <div className="date-range-filter" aria-label={`${label} ფილტრი`}>
      <span className="date-range-label">{label}</span>
      <div className="date-presets">
        {presets.map((preset) => (
          <button
            className={active === preset.label ? "active" : ""}
            key={preset.label}
            onClick={() => apply(preset.from, preset.to, preset.label)}
            type="button"
          >
            {preset.label}
          </button>
        ))}
      </div>
      <label>
        <span>დან</span>
        <input
          max={to}
          onChange={(event) => {
            setFrom(event.target.value);
            setActive("custom");
          }}
          type="date"
          value={from}
        />
      </label>
      <label>
        <span>მდე</span>
        <input
          min={from}
          onChange={(event) => {
            setTo(event.target.value);
            setActive("custom");
          }}
          type="date"
          value={to}
        />
      </label>
      <button
        className="date-apply"
        disabled={!from || !to || from > to}
        onClick={() => apply()}
        type="button"
      >
        გამოყენება
      </button>
    </div>
  );
}

export function inDateRange(date: string, range: DateRange) {
  return date >= range.from && date <= range.to;
}
