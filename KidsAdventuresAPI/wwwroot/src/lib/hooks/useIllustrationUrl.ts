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

function load(path: string): Promise<string> {
  const cached = objectUrlCache.get(path);
  if (cached) return Promise.resolve(cached);

  const existing = inFlight.get(path);
  if (existing) return existing;

  const request = fetchIllustrationObjectUrl(path)
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
