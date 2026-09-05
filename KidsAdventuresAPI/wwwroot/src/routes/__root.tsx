import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  Outlet,
  Link,
  createRootRouteWithContext,
  useRouter,
  HeadContent,
  Scripts,
} from "@tanstack/react-router";
import { useEffect, type ReactNode } from "react";

import appCss from "../styles.css?url";
import { reportLovableError } from "../lib/lovable-error-reporting";
import { Toaster } from "@/components/ui/sonner";
import { setPinterestEnhancedMatch } from "@/lib/analytics/pinterest";
import {
  GTM_ID,
  META_PIXEL_ID,
  PINTEREST_TAG_ID,
  mountMarketingTags,
} from "@/lib/analytics/marketingTags";
import { AuthProvider, useAuth } from "@/lib/auth/AuthContext";
import { GoogleAuthProvider } from "@/lib/auth/GoogleAuthProvider";
import { BRAND_LOGO_URL } from "@/lib/brand";
import { LocalizedDocumentTitle } from "@/components/LocalizedDocumentTitle";
import { LocaleProvider } from "@/lib/i18n";
import { JourneyDraftProvider } from "@/lib/journey/draft";
import { buildRootMeta } from "@/lib/seo";

function NotFoundComponent() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-7xl font-bold text-foreground">404</h1>
        <h2 className="mt-4 text-xl font-semibold text-foreground">გვერდი ვერ მოიძებნა</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          ეს გვერდი აღარ არსებობს ან სხვა მისამართზე გადავიდა.
        </p>
        <div className="mt-6">
          <Link
            to="/"
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            მთავარ გვერდზე
          </Link>
        </div>
      </div>
    </div>
  );
}

function ErrorComponent({ error, reset }: { error: Error; reset: () => void }) {
  console.error(error);
  const router = useRouter();
  useEffect(() => {
    reportLovableError(error, { boundary: "tanstack_root_error_component" });
  }, [error]);

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">
          გვერდი ვერ ჩაიტვირთა
        </h1>
        <p className="mt-2 text-sm text-muted-foreground">
          რაღაც შეფერხდა ჩვენს მხარეს. სცადე განახლება ან დაბრუნდი მთავარ გვერდზე.
        </p>
        <div className="mt-6 flex flex-wrap justify-center gap-2">
          <button
            onClick={() => {
              router.invalidate();
              reset();
            }}
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            ხელახლა ცდა
          </button>
          <a
            href="/"
            className="inline-flex items-center justify-center rounded-md border border-input bg-background px-4 py-2 text-sm font-medium text-foreground transition-colors hover:bg-accent"
          >
            მთავარ გვერდზე
          </a>
        </div>
      </div>
    </div>
  );
}

const rootMeta = buildRootMeta();

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: "width=device-width, initial-scale=1" },
      { name: "p:domain_verify", content: "f6d26a06a1d147d9babf0aa9a9d2ceb2" },
      ...rootMeta.meta,
    ],
    links: [
      { rel: "icon", type: "image/png", href: BRAND_LOGO_URL },
      { rel: "apple-touch-icon", href: BRAND_LOGO_URL },
      // Brand faces are self-hosted; the Georgian subset carries nearly all copy,
      // so it is preloaded while the Latin subsets are left to lazy @font-face.
      {
        rel: "preload",
        href: "/fonts/adventrya-sans-georgian.woff2",
        as: "font",
        type: "font/woff2",
        crossOrigin: "anonymous",
      },
      {
        rel: "preload",
        href: "/fonts/adventrya-serif-georgian.woff2",
        as: "font",
        type: "font/woff2",
        crossOrigin: "anonymous",
      },
      {
        rel: "stylesheet",
        href: appCss,
      },
      ...rootMeta.links,
    ],
  }),
  shellComponent: RootShell,
  component: RootComponent,
  notFoundComponent: NotFoundComponent,
  errorComponent: ErrorComponent,
});

function RootShell({ children }: { children: ReactNode }) {
  return (
    <html lang="ka">
      {/*
        Nothing but head management here. The marketing tags that used to sit in this <head>
        are injected after hydration instead — see lib/analytics/marketingTags.ts for why.
      */}
      <head>
        <HeadContent />
      </head>
      <body>
        {/* Google Tag Manager (noscript) — immediately after the opening <body> tag. */}
        <noscript>
          <iframe
            title="gtm"
            src={`https://www.googletagmanager.com/ns.html?id=${GTM_ID}`}
            height="0"
            width="0"
            style={{ display: "none", visibility: "hidden" }}
          />
        </noscript>
        {/* End Google Tag Manager (noscript) */}
        {/* Pinterest Tag (noscript) */}
        <noscript>
          <img
            height="1"
            width="1"
            style={{ display: "none" }}
            alt=""
            src={`https://ct.pinterest.com/v3/?event=init&tid=${PINTEREST_TAG_ID}&noscript=1`}
          />
        </noscript>
        {/* End Pinterest Tag (noscript) */}
        {/* Meta Pixel (noscript) */}
        <noscript>
          <img
            height="1"
            width="1"
            style={{ display: "none" }}
            alt=""
            src={`https://www.facebook.com/tr?id=${META_PIXEL_ID}&ev=PageView&noscript=1`}
          />
        </noscript>
        {/* End Meta Pixel (noscript) */}
        {children}
        <Scripts />
      </body>
    </html>
  );
}

function PinterestEnhancedMatch() {
  const { user } = useAuth();
  useEffect(() => {
    void setPinterestEnhancedMatch(user?.email);
  }, [user?.email]);
  return null;
}

/** Loads GTM, Analytics, AdSense and the Pinterest pixel once the page is hydrated. */
function MarketingTags() {
  useEffect(() => {
    mountMarketingTags();
  }, []);
  return null;
}

function RootComponent() {
  const { queryClient } = Route.useRouteContext();

  return (
    <QueryClientProvider client={queryClient}>
      <LocaleProvider>
        <GoogleAuthProvider>
          <AuthProvider>
            {/*
              Above the routes on purpose: the create journey steps out to /themes and
              back, and this is what carries the parent's answers across that move
              without writing anything to the device. A real page load remounts it, so
              every visit still starts a new story from scratch.
            */}
            <JourneyDraftProvider>
              <MarketingTags />
              <PinterestEnhancedMatch />
              <LocalizedDocumentTitle />
              <Outlet />
              <Toaster />
            </JourneyDraftProvider>
          </AuthProvider>
        </GoogleAuthProvider>
      </LocaleProvider>
    </QueryClientProvider>
  );
}
