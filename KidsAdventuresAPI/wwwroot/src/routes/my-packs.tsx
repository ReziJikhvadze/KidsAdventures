import { createFileRoute, redirect } from "@tanstack/react-router";

/**
 * My Books moved into the parent dashboard. Keep this path as a bookmark-friendly
 * redirect so old English-site links and emails still land somewhere useful.
 */
export const Route = createFileRoute("/my-packs")({
  beforeLoad: () => {
    throw redirect({ to: "/dashboard" });
  },
});
