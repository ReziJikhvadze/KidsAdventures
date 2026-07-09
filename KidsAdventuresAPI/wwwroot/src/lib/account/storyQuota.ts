type StoryQuotaInput = {
  bookCredits: number;
  storiesRemainingThisMonth: number;
  storiesAllowedThisMonth?: number;
  welcomeStoryRemaining?: number;
  isLoading?: boolean;
};

export function formatNavQuotaLabel({
  bookCredits,
  storiesRemainingThisMonth,
  welcomeStoryRemaining,
  isLoading,
}: StoryQuotaInput): string {
  if (isLoading) return "…";

  if ((welcomeStoryRemaining ?? 0) > 0) {
    return "1 free book";
  }

  if (storiesRemainingThisMonth > 0) {
    return storiesRemainingThisMonth === 1
      ? "1 credit left"
      : `${storiesRemainingThisMonth} credits left`;
  }

  if (bookCredits > 0) {
    return `${bookCredits} credit${bookCredits === 1 ? "" : "s"} · all used`;
  }

  return "New book $4.99";
}

export function formatNavQuotaTitle({
  bookCredits,
  storiesRemainingThisMonth,
  storiesAllowedThisMonth,
  welcomeStoryRemaining,
}: StoryQuotaInput): string {
  const allowed = storiesAllowedThisMonth ?? bookCredits;

  if ((welcomeStoryRemaining ?? 0) > 0) {
    return "Your first illustrated book is free. After that, each new book is a one-time $4.99.";
  }

  if (storiesRemainingThisMonth > 0 && bookCredits > 0) {
    return `${storiesRemainingThisMonth} of ${allowed} purchased credit${allowed === 1 ? "" : "s"} left this month.`;
  }

  if (storiesRemainingThisMonth > 0) {
    return `${storiesRemainingThisMonth} book credit${storiesRemainingThisMonth === 1 ? "" : "s"} available for full 6-page stories.`;
  }

  if (bookCredits > 0) {
    return `All ${bookCredits} purchased credit${bookCredits === 1 ? "" : "s"} used this month. Buy more for another adventure.`;
  }

  return "Buy book credits to unlock full 6-page illustrated storybooks.";
}

export function formatCreditsBadgeLabel({
  bookCredits,
  storiesRemainingThisMonth,
  welcomeStoryRemaining,
}: StoryQuotaInput): string {
  if ((welcomeStoryRemaining ?? 0) > 0) {
    return "1 free book";
  }

  if (storiesRemainingThisMonth > 0) {
    const credits =
      storiesRemainingThisMonth === 1
        ? "1 credit left"
        : `${storiesRemainingThisMonth} credits left`;
    if (bookCredits > 0) {
      return `${credits} · ${bookCredits} total`;
    }
    return credits;
  }

  if (bookCredits > 0) {
    return `0 left · ${bookCredits} credit${bookCredits === 1 ? "" : "s"}`;
  }

  return "New book $4.99";
}
