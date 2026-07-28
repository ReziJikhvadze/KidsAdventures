import { createFileRoute, redirect } from "@tanstack/react-router";

/** Retired English blog author page → home. */
export const Route = createFileRoute("/blog/author/$id")({
  beforeLoad: () => {
    throw redirect({ to: "/" });
  },
});
