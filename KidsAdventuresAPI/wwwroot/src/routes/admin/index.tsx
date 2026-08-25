import { createFileRoute, redirect } from "@tanstack/react-router";

/**
 * The console has one screen people open it to use, so /admin is that screen.
 *
 * A redirect rather than a second copy of the orders page: the alert emails deep-link to
 * /admin/orders?q={id}, and two routes rendering the same list would eventually stop being
 * the same list.
 */
export const Route = createFileRoute("/admin/")({
  beforeLoad: () => {
    throw redirect({ to: "/admin/orders" });
  },
});
