import { createFileRoute, redirect } from "@tanstack/react-router";

/**
 * Per-theme English SEO pages retired — land on the demo first-map picker.
 */
export const Route = createFileRoute("/themes/$slug")({
  beforeLoad: () => {
    throw redirect({ to: "/themes" });
  },
});
