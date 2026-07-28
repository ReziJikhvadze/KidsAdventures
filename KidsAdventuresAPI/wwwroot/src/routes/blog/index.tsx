import { createFileRoute, redirect } from "@tanstack/react-router";

/** Retired English blog index → home. */
export const Route = createFileRoute("/blog/")({
  beforeLoad: () => {
    throw redirect({ to: "/" });
  },
});
