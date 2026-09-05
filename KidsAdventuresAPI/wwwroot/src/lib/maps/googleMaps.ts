import { getAuthConfig } from "@/lib/api/auth";

/**
 * The Maps JavaScript API, fetched once per tab and only where it is used.
 *
 * Not in `marketingTags.ts` with the trackers: those load on every page whether anyone needs them
 * or not, and this one costs money per session. It loads when a parent opens the address picker
 * on the checkout screen and nowhere else, so a visitor who never buys a printed book never
 * triggers a billable call.
 *
 * The key arrives from `/api/auth/config` rather than a build-time env var. It is a browser key —
 * it cannot be hidden, and Google's answer to that is HTTP-referrer restriction rather than
 * secrecy — but delivering it at runtime means it can be rotated in the portal without a rebuild,
 * which is how the Google sign-in client id already works.
 */

/** The subset of the Maps API this app touches. Typed here so `any` does not spread from it. */
type PlaceLike = {
  formattedAddress?: string | null;
  location?: { lat(): number; lng(): number } | null;
  addressComponents?: { types: string[]; longText?: string; shortText?: string }[];
  fetchFields(options: { fields: string[] }): Promise<unknown>;
};

export type PlacePrediction = { toPlace(): PlaceLike };

type PlacesLibrary = {
  PlaceAutocompleteElement: new (options?: {
    includedRegionCodes?: string[];
    locationBias?: unknown;
  }) => HTMLElement;
};

type MapsLibrary = {
  Map: new (container: HTMLElement, options: Record<string, unknown>) => unknown;
};

type MarkerLibrary = {
  AdvancedMarkerElement: new (options: Record<string, unknown>) => { position: unknown };
};

declare global {
  interface Window {
    google?: {
      maps?: {
        importLibrary(name: string): Promise<unknown>;
      };
    };
    /** Google calls this when the key is rejected. See `authFailed`. */
    gm_authFailure?: () => void;
  }
}

let loading: Promise<boolean> | null = null;

/*
  A rejected key does not reject anything.

  Google answers a bad or referrer-blocked key by drawing its own grey "this page can't load
  Google Maps" over the map and calling this global — `importLibrary` still resolves. Without it
  the dialog would sit on its loader forever, and a key restricted to beki.ge (which is the
  correct way to hold a browser key) does exactly that everywhere else, this machine included.
*/
let authFailed = false;

/* Long enough for a slow phone, short enough that nobody is left watching a mark turn. */
const LOAD_TIMEOUT_MS = 12000;

/**
 * Google's own bootstrap, inlined.
 *
 * It is published as a snippet to paste rather than a package to install, and it is what defines
 * `google.maps.importLibrary` — the loader every current example calls. Written out here rather
 * than pulled from a CDN wrapper so there is one dependency, from Google, and no build step
 * between the documented API and this file.
 */
function bootstrap(key: string): void {
  const g = {
    key,
    v: "weekly",
    // Georgian first: the picker exists to find Georgian addresses for Georgian couriers.
    language: "ka",
    region: "GE",
  };

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  let api: Promise<unknown>;
  const map = new Map<string, unknown>();
  const params = new URLSearchParams();
  const load = () =>
    api ||
    (api = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      for (const [key_, value] of Object.entries(g)) {
        params.set(
          key_.replace(/[A-Z]/g, (m: string) => "_" + m[0].toLowerCase()),
          String(value),
        );
      }
      params.set("libraries", [...map.keys()].join(","));
      params.set("callback", "__googleMapsCallback");
      script.src = `https://maps.googleapis.com/maps/api/js?${params}`;
      w.__googleMapsCallback = resolve;
      script.onerror = () => reject(new Error("maps-script-failed"));
      document.head.append(script);
    }));

  w.google = w.google || {};
  w.google.maps = w.google.maps || {};
  w.google.maps.importLibrary = (name: string) => {
    map.set(name, true);
    return load().then(() => w.google.maps.importLibrary(name));
  };
}

/**
 * Makes the Maps API available, or reports that it is not.
 *
 * False rather than a throw, because "no picker on this deployment" is an ordinary state — the
 * key is unset until somebody adds it in the portal — and the caller's job in both cases is the
 * same: leave the typed address field alone.
 */
export function loadGoogleMaps(): Promise<boolean> {
  if (authFailed) return Promise.resolve(false);
  if (loading) return loading;

  const attempt = (async () => {
    const config = await getAuthConfig();
    const key = config.googleMapsApiKey?.trim();
    if (!key) return false;

    if (!window.google?.maps?.importLibrary) {
      window.gm_authFailure = () => {
        authFailed = true;
      };
      bootstrap(key);
    }

    // One real call, so a bad key or a blocked referrer fails here rather than inside the
    // dialog, where the parent would be looking at an empty box with no way to read why.
    await Promise.race([
      window.google!.maps!.importLibrary("places"),
      new Promise((_, reject) =>
        setTimeout(() => reject(new Error("maps-load-timeout")), LOAD_TIMEOUT_MS),
      ),
    ]);

    /*
      Asked after the library lands, not before: `gm_authFailure` fires while Google is checking
      the key, which is during this call rather than ahead of it. `importLibrary` resolves either
      way, so the flag is the only thing that can tell a working key from a refused one.
    */
    return !authFailed;
  })();

  loading = attempt.catch(() => false);

  /*
    A failed attempt is forgotten, a successful one is kept.

    Holding on to a resolved `false` would mean one flaky moment — a dropped config request, a
    slow first load — turned the picker off for the rest of the tab, with reopening the dialog
    unable to help. Nothing here is expensive to try again.
  */
  void loading.then((ok) => {
    if (!ok) loading = null;
  });

  return loading;
}

export async function placesLibrary(): Promise<PlacesLibrary> {
  return (await window.google!.maps!.importLibrary("places")) as PlacesLibrary;
}

export async function mapsLibrary(): Promise<MapsLibrary> {
  return (await window.google!.maps!.importLibrary("maps")) as MapsLibrary;
}

export async function markerLibrary(): Promise<MarkerLibrary> {
  return (await window.google!.maps!.importLibrary("marker")) as MarkerLibrary;
}

/** The one component of a Google address this form keeps on its own: the city. */
export function cityOf(place: PlaceLike): string {
  const parts = place.addressComponents ?? [];
  const match = parts.find(
    (part) =>
      part.types.includes("locality") ||
      part.types.includes("postal_town") ||
      part.types.includes("administrative_area_level_2"),
  );
  return match?.longText ?? "";
}
