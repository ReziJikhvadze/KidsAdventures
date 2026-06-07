type StoryQuotaInput = {
  bookCredits: number;
  storiesRemainingThisMonth: number;
  storiesAllowedThisMonth?: number;
  isLoading?: boolean;
};

export function formatNavQuotaLabel({
  bookCredits,
  storiesRemainingThisMonth,
  isLoading,
}: StoryQuotaInput): string {
  if (isLoading) return "…";

  if (storiesRemainingThisMonth > 0) {
    return storiesRemainingThisMonth === 1
      ? "1 story left"
      : `${storiesRemainingThisMonth} stories left`;
  }

  if (bookCredits > 0) {
    return `${bookCredits} credit${bookCredits === 1 ? "" : "s"} · limit reached`;
  }

  return "Buy credits for more";
}

export function formatNavQuotaTitle({
  bookCredits,
  storiesRemainingThisMonth,
  storiesAllowedThisMonth,
}: StoryQuotaInput): string {
  const allowed = storiesAllowedThisMonth ?? 1 + bookCredits;

  if (storiesRemainingThisMonth > 0 && bookCredits > 0) {
    return `${storiesRemainingThisMonth} of ${allowed} stories this month — 1 free every month plus ${bookCredits} purchased credit${bookCredits === 1 ? "" : "s"}.`;
  }

  if (storiesRemainingThisMonth > 0) {
    return `${storiesRemainingThisMonth} of ${allowed} free stories this month.`;
  }

  if (bookCredits > 0) {
    return `Monthly story limit used. You still have ${bookCredits} credit${bookCredits === 1 ? "" : "s"} for next month (plus 1 free).`;
  }

  return "Buy book credits to create more illustrated stories.";
}

export function formatCreditsBadgeLabel({
  bookCredits,
  storiesRemainingThisMonth,
}: StoryQuotaInput): string {
  if (bookCredits > 0) {
    return `${bookCredits} story credit${bookCredits === 1 ? "" : "s"}`;
  }

  if (storiesRemainingThisMonth > 0) {
    return storiesRemainingThisMonth === 1 ? "1 free story" : `${storiesRemainingThisMonth} free stories`;
  }

  return "0 story credits";
}
