export const ADSENSE_CLIENT_ID = "ca-pub-9730875401500289";

/**
 * Google AdSense loader, scoped to blog pages only. Returned from a route's
 * `head()` under `scripts`; TanStack maps that to the match's head scripts so it
 * renders inside the document `<head>` (and only while a blog route is active).
 */
export const adsenseHeadScripts = [
  {
    src: `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${ADSENSE_CLIENT_ID}`,
    async: true,
    crossOrigin: "anonymous" as const,
  },
];
