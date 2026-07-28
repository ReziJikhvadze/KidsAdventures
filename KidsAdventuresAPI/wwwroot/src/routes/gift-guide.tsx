import { createFileRoute, redirect } from "@tanstack/react-router";

/** Retired English gift-guide SEO page → home. */
export const Route = createFileRoute("/gift-guide")({
  beforeLoad: () => {
    throw redirect({ to: "/" });
  },
});
