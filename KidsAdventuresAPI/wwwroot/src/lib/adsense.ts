export const ADSENSE_CLIENT_ID = "ca-pub-9730875401500289";

/**
 * Google AdSense loader, scoped to blog pages only. Returned from a route's
 * `head()` as `headScripts` so TanStack renders it inside the document `<head>`
 * (and removes it again when navigating away from the blog).
 */
export const adsenseHeadScripts = [
  {
    src: `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${ADSENSE_CLIENT_ID}`,
    async: true,
    crossOrigin: "anonymous" as const,
  },
];
