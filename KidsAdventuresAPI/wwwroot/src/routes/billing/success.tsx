import { createFileRoute, redirect } from "@tanstack/react-router";

/** Legacy credit-wallet success page → dashboard (orders fulfil into the library). */
export const Route = createFileRoute("/billing/success")({
  beforeLoad: () => {
    throw redirect({ to: "/dashboard" });
  },
});
