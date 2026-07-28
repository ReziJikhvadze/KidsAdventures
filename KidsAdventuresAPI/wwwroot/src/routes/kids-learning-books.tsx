import { createFileRoute, redirect } from "@tanstack/react-router";

/** Retired English SEO landing → create journey. */
export const Route = createFileRoute("/kids-learning-books")({
  beforeLoad: () => {
    throw redirect({ to: "/create" });
  },
});
