import { toast } from "sonner";

import { ApiError } from "@/lib/api/client";

type NotifyOptions = {
  description?: string;
  duration?: number;
};

function show(
  type: "success" | "error" | "info",
  title: string,
  { description, duration = 6000 }: NotifyOptions = {},
) {
  const fn = type === "success" ? toast.success : type === "error" ? toast.error : toast.message;
  fn(title, { description, duration });
}

export function formatApiError(
  err: unknown,
  fallback: string,
): { title: string; description?: string } {
  if (err instanceof ApiError) {
    const message = err.message.trim();
    const lower = message.toLowerCase();

    if (err.status === 401) {
      return {
        title: "Sign in required",
        description:
          message && message !== "Unauthorized"
            ? message
            : "Your session expired. Please sign in again to continue.",
      };
    }

    if (
      err.status === 402 ||
      err.status === 403 ||
      lower.includes("quota") ||
      lower.includes("monthly") ||
      lower.includes("story limit") ||
      lower.includes("no stories")
    ) {
      return {
        title: "Monthly story limit reached",
        description:
          message ||
          "You've used your free illustrated story for this month. Buy book credits to create another full 6-page book.",
      };
    }

    if (err.status === 429 || lower.includes("rate limit")) {
      return {
        title: "Please wait a moment",
        description: "Too many requests. Try again in a minute.",
      };
    }

    if (lower.includes("confirm") && lower.includes("email")) {
      return {
        title: "Confirm your email first",
        description: message,
      };
    }

    return { title: message || fallback };
  }

  if (err instanceof Error && err.message.trim()) {
    return { title: err.message.trim() };
  }

  return { title: fallback };
}

export const notify = {
  success(title: string, options?: NotifyOptions) {
    show("success", title, options);
  },
  error(title: string, options?: NotifyOptions) {
    show("error", title, options);
  },
  info(title: string, options?: NotifyOptions) {
    show("info", title, options);
  },
  fromError(err: unknown, fallback: string) {
    const { title, description } = formatApiError(err, fallback);
    show("error", title, { description });
  },
};
