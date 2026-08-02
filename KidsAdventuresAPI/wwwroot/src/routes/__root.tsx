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
import { AuthProvider, useAuth } from "@/lib/auth/AuthContext";
import { GoogleAuthProvider } from "@/lib/auth/GoogleAuthProvider";
import { BRAND_LOGO_URL } from "@/lib/brand";
import { LocalizedDocumentTitle } from "@/components/LocalizedDocumentTitle";
import { LocaleProvider } from "@/lib/i18n";
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

const GA_MEASUREMENT_ID = "G-7ZL6C5SB29";
const GTM_ID = "GTM-K9Q596H3";
const PINTEREST_TAG_ID = "2614019108945";

function RootShell({ children }: { children: ReactNode }) {
  return (
    <html lang="ka">
      <head>
        <HeadContent />
        {/* Google AdSense — site verification + ad serving. */}
        <script
          async
          src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-9730875401500289"
          crossOrigin="anonymous"
        />
        {/* Google Tag Manager — placed as high in <head> as possible. */}
        <script
          // eslint-disable-next-line react/no-danger
          dangerouslySetInnerHTML={{
            __html: `(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':
new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],
j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src=
'https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);
})(window,document,'script','dataLayer','${GTM_ID}');`,
          }}
        />
        {/* End Google Tag Manager */}
        {/* Google tag (gtag.js) — Google Analytics, rendered into the head of every page. */}
        <script
          async
          src={`https://www.googletagmanager.com/gtag/js?id=${GA_MEASUREMENT_ID}`}
        />
        <script
          // eslint-disable-next-line react/no-danger
          dangerouslySetInnerHTML={{
            __html: `window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
gtag('js', new Date());
gtag('config', '${GA_MEASUREMENT_ID}');`,
          }}
        />
        {/* Pinterest Tag — base pixel (enhanced-match email intentionally omitted). */}
        <script
          // eslint-disable-next-line react/no-danger
          dangerouslySetInnerHTML={{
            __html: `!function(e){if(!window.pintrk){window.pintrk = function () {
window.pintrk.queue.push(Array.prototype.slice.call(arguments))};var
  n=window.pintrk;n.queue=[],n.version="3.0";var
  t=document.createElement("script");t.async=!0,t.src=e;var
  r=document.getElementsByTagName("script")[0];
  r.parentNode.insertBefore(t,r)}}("https://s.pinimg.com/ct/core.js");
pintrk('load', '${PINTEREST_TAG_ID}');
pintrk('page');`,
          }}
        />
        {/* End Pinterest Tag */}
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

function RootComponent() {
  const { queryClient } = Route.useRouteContext();

  return (
    <QueryClientProvider client={queryClient}>
      <LocaleProvider>
        <GoogleAuthProvider>
          <AuthProvider>
            <PinterestEnhancedMatch />
            <LocalizedDocumentTitle />
            <Outlet />
            <Toaster />
          </AuthProvider>
        </GoogleAuthProvider>
      </LocaleProvider>
    </QueryClientProvider>
  );
}
