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

/** Resolves authenticated illustration paths into object URLs for CSS backgrounds. */
export function useIllustrationUrl(path: string | null | undefined): string | null {
  const [url, setUrl] = useState<string | null>(() => {
    if (!path) return null;
    return isPublicIllustrationUrl(path) ? path : null;
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

    let cancelled = false;
    let objectUrl: string | null = null;
    void fetchIllustrationObjectUrl(path)
      .then((resolved) => {
        if (cancelled) {
          URL.revokeObjectURL(resolved);
          return;
        }
        objectUrl = resolved;
        setUrl(resolved);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [path]);

  return url;
}
