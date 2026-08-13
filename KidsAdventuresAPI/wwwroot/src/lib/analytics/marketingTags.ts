/**
 * Google Tag Manager, Google Analytics, AdSense and the Pinterest pixel.
 *
 * These used to be <script> tags in the shell's <head>, which is the placement every one of
 * these vendors documents. It does not survive hydration. Each snippet runs while the browser
 * is still parsing the document and inserts its own <script> before the first one it finds —
 * Pinterest's does it explicitly, AdSense pulls in a managed bundle — so by the time React
 * hydrated, <head> held five nodes it had never rendered, at the positions where it expected
 * its own. React gave up on the whole tree and re-rendered it on the client, which ran the
 * inline snippets a second time: the Pinterest pixel loaded twice and reported every page view
 * twice.
 *
 * So they are injected here instead, once, after hydration. React never owns these nodes and
 * never compares them. The cost is that measurement starts a few hundred milliseconds later
 * than a parse-time tag would; the gain is that it is not counted twice.
 */

const GA_MEASUREMENT_ID = "G-7ZL6C5SB29";
const GTM_ID = "GTM-K9Q596H3";
const PINTEREST_TAG_ID = "2614019108945";
const ADSENSE_CLIENT = "ca-pub-9730875401500289";

let mounted = false;

function inline(code: string) {
  const el = document.createElement("script");
  el.textContent = code;
  document.head.appendChild(el);
}

function external(src: string, crossOrigin?: string) {
  const el = document.createElement("script");
  el.async = true;
  el.src = src;
  if (crossOrigin) el.crossOrigin = crossOrigin;
  document.head.appendChild(el);
}

export function mountMarketingTags(): void {
  if (typeof document === "undefined" || mounted) return;
  mounted = true;

  // Google Tag Manager.
  inline(`(function(w,d,s,l,i){w[l]=w[l]||[];w[l].push({'gtm.start':
new Date().getTime(),event:'gtm.js'});var f=d.getElementsByTagName(s)[0],
j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src=
'https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);
})(window,document,'script','dataLayer','${GTM_ID}');`);

  // Google Analytics.
  external(`https://www.googletagmanager.com/gtag/js?id=${GA_MEASUREMENT_ID}`);
  inline(`window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
gtag('js', new Date());
gtag('config', '${GA_MEASUREMENT_ID}');`);

  // Pinterest base pixel. Enhanced-match email is set separately, once a visitor signs in.
  inline(`!function(e){if(!window.pintrk){window.pintrk = function () {
window.pintrk.queue.push(Array.prototype.slice.call(arguments))};var
  n=window.pintrk;n.queue=[],n.version="3.0";var
  t=document.createElement("script");t.async=!0,t.src=e;var
  r=document.getElementsByTagName("script")[0];
  r.parentNode.insertBefore(t,r)}}("https://s.pinimg.com/ct/core.js");
pintrk('load', '${PINTEREST_TAG_ID}');
pintrk('page');`);

  // AdSense.
  external(
    `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${ADSENSE_CLIENT}`,
    "anonymous",
  );
}

export { GTM_ID, PINTEREST_TAG_ID };
