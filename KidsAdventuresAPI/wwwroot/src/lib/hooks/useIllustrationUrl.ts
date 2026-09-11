import { useEffect, useState } from "react";
import { fetchIllustrationObjectUrl } from "@/lib/api/adventure-packs";

function isPublicIllustrationUrl(url: string): boolean {
  if (url.startsWith("/api/")) return false;
  return (
    url.startsWith("/public/") ||
    url.startsWith("/demo/") ||
    url.startsWith("/adventrya/") ||
    url.startsWith("http://") ||
    url.startsWith("https://") ||
    url.startsWith("data:")
  );
}

/**
 * Illustrations already fetched, kept for as long as the tab lives.
 *
 * This used to be per-component state, and the object URL was revoked the moment the page left
 * the reader. Turning back a page therefore downloaded the picture all over again, so a book of
 * nine illustrations re-fetched one every time a child flipped back and forth — which is most of
 * why a finished book felt slow to read.
 *
 * An illustration cannot change once it has been drawn, so keeping it for the session is simply
 * correct rather than a trade. The in-flight map is what stops the reader and the preloader
 * asking for the same picture at the same time.
 */
const objectUrlCache = new Map<string, string>();
const inFlight = new Map<string, Promise<string>>();

/*
  A picture that could not be fetched is asked for again, a few times, a little later each time.

  The failures worth this are the passing ones: the API restarting under a deploy answers with
  nothing for a minute or two, and a network that dropped for a moment. One such minute used to
  leave the shelf showing a world's stock art in place of the child's painted cover for the rest
  of the visit, because nothing asked again until the page was reloaded. A picture that is
  genuinely gone (a 404) is not asked for again: it will not appear by waiting.

  A failure with no status at all gets one more kind of second try, straight away and past the
  browser's cache. The address of a cover is also what the preview screen shows as a plain
  picture, and the browser kept that answer - fetched without an Origin, so without the CORS
  header - and handed it back to the fetch here, which cannot use it. The server now varies on
  Origin so this stops happening; the copies already sitting in parents' caches are why the
  reload is asked for anyway, and it costs nothing when the cache was fine.
*/
const RETRY_DELAYS_MS = [3000, 8000, 20000];

function isPassing(error: unknown): boolean {
  const status = (error as { status?: number } | null)?.status;
  // A failed fetch throws a TypeError with no status at all: that is the network, or a server
  // that is not there to answer. 5xx is a server that is there and cannot.
  return status === undefined || status >= 500;
}

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

async function fetchWithRetries(path: string): Promise<string> {
  let cache: RequestCache | undefined;
  for (let attempt = 0; ; attempt++) {
    try {
      return await fetchIllustrationObjectUrl(path, cache);
    } catch (error) {
      if (attempt >= RETRY_DELAYS_MS.length || !isPassing(error)) throw error;
      const status = (error as { status?: number } | null)?.status;
      if (status === undefined && cache === undefined) {
        cache = "reload";
        continue;
      }
      await wait(RETRY_DELAYS_MS[attempt]);
    }
  }
}

function load(path: string): Promise<string> {
  const cached = objectUrlCache.get(path);
  if (cached) return Promise.resolve(cached);

  const existing = inFlight.get(path);
  if (existing) return existing;

  const request = fetchWithRetries(path)
    .then((resolved) => {
      // Two callers can pass the cache check before either resolves. The loser's object URL
      // would otherwise leak, since only the cached one is ever handed out again.
      const winner = objectUrlCache.get(path);
      if (winner) {
        URL.revokeObjectURL(resolved);
        return winner;
      }
      objectUrlCache.set(path, resolved);
      return resolved;
    })
    .finally(() => {
      inFlight.delete(path);
    });

  inFlight.set(path, request);
  return request;
}

/**
 * Fetches an illustration before anything asks to display it.
 *
 * The reader calls this for the pages either side of the one being read, so the picture is
 * already in hand by the time the page turns. Failures are swallowed on purpose: a preload that
 * never arrives costs nothing, because the page that needs it will ask again.
 */
export function preloadIllustration(path: string | null | undefined): void {
  if (!path || isPublicIllustrationUrl(path)) return;
  void load(path).catch(() => {});
}

/** Resolves authenticated illustration paths into object URLs for CSS backgrounds. */
export function useIllustrationUrl(path: string | null | undefined): string | null {
  const [url, setUrl] = useState<string | null>(() => {
    if (!path) return null;
    if (isPublicIllustrationUrl(path)) return path;
    return objectUrlCache.get(path) ?? null;
  });

  useEffect(() => {
    if (!path) {
      setUrl(null);
      return;
    }
    if (isPublicIllustrationUrl(path)) {
      setUrl(path);
      return;
    }

    // Straight from the cache, so a page turned back to shows its picture in the same frame
    // instead of going blank and then loading.
    const cached = objectUrlCache.get(path);
    if (cached) {
      setUrl(cached);
      return;
    }

    let cancelled = false;
    setUrl(null);
    void load(path)
      .then((resolved) => {
        if (!cancelled) setUrl(resolved);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      // Deliberately not revoked. The object URL belongs to the cache now rather than to this
      // component, and revoking it would blank out every other page already showing it.
    };
  }, [path]);

  return url;
}
