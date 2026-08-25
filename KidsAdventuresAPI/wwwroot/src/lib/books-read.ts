/**
 * Which books this browser has already opened in the reader.
 *
 * The shelf ranks a book's three actions by what is left to want: before the story has been
 * read, reading it is the point; afterwards the free things are spent and the printed copy is
 * the only thing the card can still offer. The server does not record reads — and a read is a
 * per-device, no-consequence signal — so it lives in localStorage, and a browser that refuses
 * storage simply never promotes anything.
 */

const KEY = "beki:books-read";

function load(): string[] {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : [];
  } catch {
    // Private mode, a disabled store, or a value someone else wrote: no read history, no error.
    return [];
  }
}

export function readBookIds(): Set<string> {
  return new Set(load());
}

export function markBookRead(bookId: string): void {
  if (!bookId) return;
  try {
    const ids = load();
    if (ids.includes(bookId)) return;
    // Newest last, oldest trimmed: a family with years of books does not need an unbounded key.
    const next = [...ids, bookId].slice(-200);
    window.localStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    /* nothing to do: the shelf just never promotes this book's printed edition */
  }
}
