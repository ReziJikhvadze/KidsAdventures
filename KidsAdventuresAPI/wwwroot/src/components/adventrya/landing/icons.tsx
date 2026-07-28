import type { WorldId } from "@/lib/worlds";

type IconProps = { className?: string };

export function SparkleIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true">
      <path d="M12 2c.8 5.5 4 8.8 10 10-6 1.2-9.2 4.5-10 10-.8-5.5-4-8.8-10-10 6-1.2 9.2-4.5 10-10Z" />
    </svg>
  );
}

export function ArrowIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="M5 12h14M13 6l6 6-6 6" />
    </svg>
  );
}

export function CheckIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="m5 12.5 4.3 4.3L19.5 6.5" />
    </svg>
  );
}

export function BookIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
      <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
    </svg>
  );
}

export function GlobeIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8">
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3c3 3.5 3 14.5 0 18M12 3c-3 3.5-3 14.5 0 18" />
    </svg>
  );
}

export function ChevronDownIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="m6 9 6 6 6-6" />
    </svg>
  );
}

export function DashboardIcon({ className }: IconProps) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8">
      <rect x="3" y="3" width="7" height="9" rx="1.5" />
      <rect x="14" y="3" width="7" height="5" rx="1.5" />
      <rect x="14" y="12" width="7" height="9" rx="1.5" />
      <rect x="3" y="16" width="7" height="5" rx="1.5" />
    </svg>
  );
}

export function WorldIcon({ type }: { type: WorldId }) {
  if (type === "space") {
    return (
      <svg viewBox="0 0 28 28" aria-hidden="true">
        <circle cx="14" cy="14" r="4" />
        <ellipse cx="14" cy="14" rx="11" ry="5.5" transform="rotate(-24 14 14)" fill="none" stroke="currentColor" />
      </svg>
    );
  }
  if (type === "pirates") {
    return (
      <svg viewBox="0 0 28 28" aria-hidden="true">
        <path d="M5 20.5h18l-3 3H8l-3-3ZM14 5v15M15 7l7 9h-7V7Z" />
      </svg>
    );
  }
  if (type === "animals") {
    return (
      <svg viewBox="0 0 28 28" aria-hidden="true">
        <circle cx="14" cy="17" r="5" />
        <circle cx="7" cy="10" r="2.5" />
        <circle cx="13" cy="7.5" r="2.5" />
        <circle cx="20" cy="10" r="2.5" />
      </svg>
    );
  }
  if (type === "airplanes") {
    return (
      <svg viewBox="0 0 28 28" aria-hidden="true">
        <path d="m4 15 20-8-7 16-4-6-9-2Z" />
        <path d="m13 17 4-4" fill="none" stroke="currentColor" />
      </svg>
    );
  }
  if (type === "magic") {
    return (
      <svg viewBox="0 0 28 28" aria-hidden="true">
        <path d="M14 3c.7 5.9 4 9.5 10 11-6 1.5-9.3 5.1-10 11-.7-5.9-4-9.5-10-11 6-1.5 9.3-5.1 10-11Z" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 28 28" aria-hidden="true">
      <path d="M5 18c1.5-7 5.3-11.3 12-13-1.3 2.3-1.3 4.3 0 6 3 1 5 3 6 6-4 4-9 5-15 3l-3-2Z" />
      <circle cx="18.5" cy="8.5" r=".8" />
    </svg>
  );
}
