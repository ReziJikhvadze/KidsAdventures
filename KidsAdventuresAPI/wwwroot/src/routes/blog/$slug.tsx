import { createFileRoute, redirect } from "@tanstack/react-router";

/** Retired English blog post → home. */
export const Route = createFileRoute("/blog/$slug")({
  beforeLoad: () => {
    throw redirect({ to: "/" });
  },
});
