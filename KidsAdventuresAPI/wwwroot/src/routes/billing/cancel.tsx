import { createFileRoute, redirect } from "@tanstack/react-router";

export const Route = createFileRoute("/billing/cancel")({
  beforeLoad: () => {
    throw redirect({ to: "/create", hash: "checkout" });
  },
});
