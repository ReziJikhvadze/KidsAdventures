import { useSyncExternalStore } from "react";

/**
 * Which books are having their PDF built right now, for the whole app rather than one screen.
 *
 * A Beki book keeps no PDF: the file is composed for the click that asks, which takes seconds and
 * happens inside one request. The reader knew that was running because it owned the state, and the
 * shelf did not - so a parent who pressed download and walked back to the dashboard found a button
 * sitting there as though nothing had been asked of it, and pressed it again.
 *
 * The request itself survives the move: this is one document and a client-side route change does
 * not cancel a fetch. So the work really is still going, and the only thing missing was somewhere
 * to say so. Module state rather than a context, because the two screens are never mounted
 * together and a provider around both would be a wrapper with one job.
 */
const inFlight = new Set<string>();
const listeners = new Set<() => void>();

function emit(): void {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** The book's PDF is being built. Pair every call with {@link pdfDownloadFinished}. */
export function pdfDownloadStarted(packId: string): void {
  inFlight.add(packId);
  emit();
}

/** It finished, or it failed. Either way nothing is running any more. */
export function pdfDownloadFinished(packId: string): void {
  inFlight.delete(packId);
  emit();
}

/**
 * Whether this book's PDF is being built, wherever it was asked for.
 *
 * The server snapshot is `false`: nothing is in flight on a page that has just been rendered, and
 * claiming otherwise would hydrate a loader over a button nobody has pressed.
 */
export function usePdfDownloading(packId: string | null | undefined): boolean {
  return useSyncExternalStore(
    subscribe,
    () => (packId ? inFlight.has(packId) : false),
    () => false,
  );
}
