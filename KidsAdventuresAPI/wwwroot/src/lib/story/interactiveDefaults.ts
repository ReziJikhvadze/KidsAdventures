import type { CSSProperties } from "react";
import type { StoryPageContent } from "@/lib/api/types";

export type HotspotRegion = {
  x: number;
  y: number;
  w: number;
  h: number;
};

export type PageInteractive = {
  avatarTap?: { region?: HotspotRegion };
  findIt?: { prompt: string; objectLabel: string; region: HotspotRegion };
  counting?: {
    prompt: string;
    target: number;
    label: string;
    regions?: HotspotRegion[];
  };
  revealItem?: {
    prompt: string;
    coverLabel: string;
    revealLabel: string;
    funFact?: string | null;
    region: HotspotRegion;
  };
};

export type ResolvedPageInteractive = PageInteractive;

const DEFAULT_HERO_REGION: HotspotRegion = { x: 12, y: 35, w: 28, h: 45 };

const FIND_IT_PATTERN =
  /\b(find|spot|hidden|look for|search for|where is|can you see)\b/i;
const COUNT_PATTERN =
  /\bcount\b|(\b(one|two|three|four|five|six|seven|eight|nine|ten)\b.*\b(egg|eggs|star|stars|dino|dinosaur|friend|friends|flower|flowers|treasure|gem|gems)\b)/i;

function clampRegion(region: HotspotRegion): HotspotRegion {
  return {
    x: Math.min(100, Math.max(0, region.x)),
    y: Math.min(100, Math.max(0, region.y)),
    w: Math.min(60, Math.max(8, region.w)),
    h: Math.min(60, Math.max(8, region.h)),
  };
}

/** Spread tap targets across the upper illustration for diegetic counting. */
export function generateCountingRegions(target: number): HotspotRegion[] {
  const count = Math.min(10, Math.max(1, target));
  const usableWidth = 76;
  const startX = 12;
  const step = count === 1 ? 0 : usableWidth / (count - 1);

  return Array.from({ length: count }, (_, i) =>
    clampRegion({
      x: count === 1 ? 44 : startX + step * i - 6,
      y: 22 + (i % 2) * 10,
      w: 12,
      h: 12,
    }),
  );
}

function inferFindIt(
  page: StoryPageContent,
  childName?: string,
): PageInteractive["findIt"] | undefined {
  const text = `${page.title} ${page.caption ?? ""} ${page.content}`;
  if (!FIND_IT_PATTERN.test(text)) return undefined;

  const objectMatch = text.match(/\b(key|treasure|map|clue|gem|egg|feather|shell|star|lantern)\b/i);
  const objectLabel = objectMatch?.[1]?.toLowerCase() ?? "treasure";
  const name = childName ?? "you";

  return {
    prompt: `Can ${name} spot the ${objectLabel}?`,
    objectLabel,
    region: clampRegion({ x: 58, y: 28, w: 18, h: 18 }),
  };
}

function inferCounting(
  page: StoryPageContent,
  childName?: string,
): PageInteractive["counting"] | undefined {
  const text = `${page.title} ${page.caption ?? ""} ${page.content}`;
  if (!COUNT_PATTERN.test(text)) return undefined;

  const numberWords: Record<string, number> = {
    one: 1,
    two: 2,
    three: 3,
    four: 4,
    five: 5,
    six: 6,
    seven: 7,
    eight: 8,
    nine: 9,
    ten: 10,
  };

  let target = 3;
  const digitMatch = text.match(/\bcount\s+(?:the\s+)?(\d+)\b/i);
  if (digitMatch) {
    target = Math.min(10, Math.max(1, parseInt(digitMatch[1], 10)));
  } else {
    for (const [word, value] of Object.entries(numberWords)) {
      if (new RegExp(`\\b${word}\\b`, "i").test(text)) {
        target = value;
        break;
      }
    }
  }

  const labelMatch = text.match(/\b(eggs?|stars?|dinosaurs?|friends?|flowers?|gems?)\b/i);
  const label = labelMatch?.[1]?.toLowerCase() ?? "things";
  const name = childName ?? "your hero";

  return {
    prompt: `Help ${name} count the ${label} — tap each one.`,
    target,
    label,
    regions: generateCountingRegions(target),
  };
}

type FallbackPlan = {
  findItPageIndex: number | null;
  countingPageIndex: number | null;
};

function planBookFallbacks(
  pages: StoryPageContent[],
  childName?: string,
): FallbackPlan {
  let findItPageIndex: number | null = null;
  let countingPageIndex: number | null = null;

  for (let i = 0; i < pages.length; i++) {
    const page = pages[i];
    if (page.interactive?.findIt || page.interactive?.counting || page.interactive?.revealItem) {
      continue;
    }
    if (findItPageIndex === null && i >= 1 && inferFindIt(page, childName)) {
      findItPageIndex = i;
    }
  }

  for (let i = 0; i < pages.length; i++) {
    const page = pages[i];
    if (page.interactive?.findIt || page.interactive?.counting || page.interactive?.revealItem) {
      continue;
    }
    if (i === findItPageIndex) continue;
    if (countingPageIndex === null && i >= 2 && inferCounting(page, childName)) {
      countingPageIndex = i;
      break;
    }
  }

  if (findItPageIndex !== null && countingPageIndex !== null) {
    countingPageIndex = null;
  }

  return { findItPageIndex, countingPageIndex };
}

/** Merge server metadata with restrained client fallbacks (max one find-it OR counting per book). */
export function resolvePageInteractive(
  page: StoryPageContent,
  pageIndex: number,
  options: {
    childName?: string;
    hasHeroPhoto?: boolean;
    allPages?: StoryPageContent[];
  },
): ResolvedPageInteractive | null {
  const fromServer = page.interactive;
  const resolved: ResolvedPageInteractive = { ...(fromServer ?? {}) };

  const pages = options.allPages ?? [page];
  const plan = planBookFallbacks(pages, options.childName);

  if (resolved.revealItem?.region) {
    resolved.revealItem = {
      ...resolved.revealItem,
      region: clampRegion(resolved.revealItem.region),
    };
  }

  const showAvatar = !resolved.revealItem && (fromServer?.avatarTap || pageIndex % 2 === 0 || pageIndex === 0);

  if (showAvatar && !resolved.avatarTap) {
    resolved.avatarTap = { region: DEFAULT_HERO_REGION };
  } else if (resolved.avatarTap?.region) {
    resolved.avatarTap = { region: clampRegion(resolved.avatarTap.region) };
  }

  if (!resolved.findIt && plan.findItPageIndex === pageIndex) {
    resolved.findIt = inferFindIt(page, options.childName);
  } else if (resolved.findIt?.region) {
    resolved.findIt = {
      ...resolved.findIt,
      region: clampRegion(resolved.findIt.region),
    };
  }

  if (!resolved.counting && plan.countingPageIndex === pageIndex) {
    resolved.counting = inferCounting(page, options.childName);
  } else if (resolved.counting) {
    const target = Math.min(10, Math.max(1, resolved.counting.target));
    resolved.counting = {
      ...resolved.counting,
      target,
      regions:
        resolved.counting.regions && resolved.counting.regions.length >= target
          ? resolved.counting.regions.map(clampRegion)
          : generateCountingRegions(target),
    };
  }

  const hasAny = resolved.avatarTap || resolved.findIt || resolved.counting || resolved.revealItem;
  return hasAny ? resolved : null;
}

export function regionToStyle(region: HotspotRegion): CSSProperties {
  return {
    left: `${region.x}%`,
    top: `${region.y}%`,
    width: `${region.w}%`,
    height: `${region.h}%`,
  };
}
