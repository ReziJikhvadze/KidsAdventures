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

export type PlacePrediction = {
  toPlace(): PlaceLike;
  /** What the row says. `text` is the whole address; the two halves are for a two-line row. */
  text?: { toString(): string };
  mainText?: { toString(): string };
  secondaryText?: { toString(): string };
};

type AutocompleteSuggestion = { placePrediction?: PlacePrediction | null };

type PlacesLibrary = {
  PlaceAutocompleteElement: new (options?: {
    includedRegionCodes?: string[];
    locationBias?: unknown;
  }) => HTMLElement;
  /**
   * The same predictions the element above draws for itself, as data.
   *
   * The element renders Google's own input and Google's own list, which is right inside the map
   * dialog — it is Google's box in Google's box — and wrong in a form field standing between a
   * recipient's name and their phone number, where it would be the one control on the page that
   * is not ours. This is what lets the field stay ours.
   */
  AutocompleteSuggestion: {
    fetchAutocompleteSuggestions(request: {
      input: string;
      sessionToken?: unknown;
      includedRegionCodes?: string[];
      language?: string;
      region?: string;
    }): Promise<{ suggestions: AutocompleteSuggestion[] }>;
  };
  /**
   * Google bills a session — every keystroke of typing plus the one place that is chosen — as a
   * single lookup rather than one per letter, provided the same token is passed throughout and
   * a new one is started afterwards.
   */
  AutocompleteSessionToken: new () => unknown;
};

/** A point on the map, as Google hands it back from a click or a drag. */
export type LatLngLike = { lat(): number; lng(): number };

export type MapLike = {
  setCenter(position: unknown): void;
  setZoom(zoom: number): void;
  addListener(event: "click", handler: (event: { latLng?: LatLngLike | null }) => void): unknown;
};

type MapsLibrary = {
  Map: new (container: HTMLElement, options: Record<string, unknown>) => MapLike;
};

/**
 * The classic marker, which Google keeps beside the advanced one. It is the one this picker
 * uses because it is the one that works on a map styled from code: the advanced marker needs a
 * cloud map id, and a map with a cloud id ignores the style array below in favour of whatever
 * was drawn in the console.
 */
export type ClassicMarker = {
  setPosition(position: unknown): void;
  getPosition(): LatLngLike | null | undefined;
  setVisible(visible: boolean): void;
  addListener(event: "dragend", handler: () => void): unknown;
};

type MarkerLibrary = {
  AdvancedMarkerElement: new (options: Record<string, unknown>) => { position: unknown };
  Marker: new (options: Record<string, unknown>) => ClassicMarker;
};

type GeocoderComponent = { long_name: string; short_name: string; types: string[] };
type GeocoderResult = {
  formatted_address: string;
  address_components: GeocoderComponent[];
  types: string[];
  geometry: { location: LatLngLike };
};
type GeocodingLibrary = {
  Geocoder: new () => {
    geocode(request: {
      location?: unknown;
      address?: string;
      region?: string;
    }): Promise<{ results: GeocoderResult[] }>;
  };
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

export async function geocodingLibrary(): Promise<GeocodingLibrary> {
  return (await window.google!.maps!.importLibrary("geocoding")) as GeocodingLibrary;
}

/**
 * The address under a point, or null if Google has none to give.
 *
 * A pin is coordinates, and a courier cannot ring coordinates. Reading the address back is
 * what makes a dropped pin a delivery address: the parent sees the street and number Google
 * puts under their finger, and can move the pin until it says the right building. The first
 * result that names a building or a street is preferred over a district or a city, which is
 * what Google returns first for a pin on open ground.
 */
export async function addressAt(
  location: LatLngLike,
): Promise<{ address: string; city: string } | null> {
  try {
    const { Geocoder } = await geocodingLibrary();
    const { results } = await new Geocoder().geocode({ location });
    const precise = results.find((result) =>
      result.types.some(
        (type) => type === "street_address" || type === "premise" || type === "route",
      ),
    );
    const found = precise ?? results[0];
    const address = found?.formatted_address?.trim();
    if (!found || !address) return null;
    return {
      address,
      city: cityOfComponents(
        found.address_components.map((part) => ({ types: part.types, longText: part.long_name })),
      ),
    };
  } catch {
    return null;
  }
}

/**
 * Where an address is, as Google reads it - the point to put the pin on and the address Google
 * would print for that point. The other direction of `addressAt`, for a street that arrived as
 * words: typed on the form before the map was opened, or chosen from the list above the map.
 */
export async function locateAddress(
  text: string,
): Promise<{ address: string; city: string; location: LatLngLike } | null> {
  try {
    const { Geocoder } = await geocodingLibrary();
    const { results } = await new Geocoder().geocode({ address: text, region: "ge" });
    const found = results[0];
    const address = found?.formatted_address?.trim();
    if (!found || !address || !found.geometry?.location) return null;
    return {
      address,
      city: cityOfComponents(
        found.address_components.map((part) => ({ types: part.types, longText: part.long_name })),
      ),
      location: found.geometry.location,
    };
  } catch {
    return null;
  }
}

/**
 * Georgian addresses matching what has been typed, for a field to draw its own list from.
 *
 * Restricted to Georgia because everything printed here is delivered here, and asking Google for
 * the whole planet puts a street in Georgia, USA above the one in Tbilisi. Errors come back as an
 * empty list rather than a throw: a suggestion that does not arrive leaves an ordinary text field
 * that the parent can simply finish typing, which is the same thing that happens where the key is
 * unset.
 */
export async function suggestAddresses(
  input: string,
  sessionToken?: unknown,
): Promise<PlacePrediction[]> {
  try {
    const places = await placesLibrary();
    const { suggestions } = await places.AutocompleteSuggestion.fetchAutocompleteSuggestions({
      input,
      sessionToken,
      includedRegionCodes: ["ge"],
      language: "ka",
      region: "ge",
    });
    return suggestions
      .map((suggestion) => suggestion.placePrediction)
      .filter((prediction): prediction is PlacePrediction => !!prediction);
  } catch {
    return [];
  }
}

/** Starts a billing session, so a whole search costs one lookup rather than one per keystroke. */
export async function newAddressSession(): Promise<unknown> {
  try {
    const places = await placesLibrary();
    return new places.AutocompleteSessionToken();
  } catch {
    return undefined;
  }
}

/** The address a chosen prediction stands for, or null if Google will not say. */
export async function resolvePrediction(
  prediction: PlacePrediction,
): Promise<{ address: string; city: string } | null> {
  try {
    const place = prediction.toPlace();
    await place.fetchFields({ fields: ["formattedAddress", "addressComponents"] });
    const address = place.formattedAddress?.trim();
    if (!address) return null;
    return { address, city: cityOf(place) };
  } catch {
    return null;
  }
}

/** The one component of a Google address this form keeps on its own: the city. */
export function cityOf(place: PlaceLike): string {
  return cityOfComponents(place.addressComponents ?? []);
}

function cityOfComponents(parts: { types: string[]; longText?: string }[]): string {
  const match = parts.find(
    (part) =>
      part.types.includes("locality") ||
      part.types.includes("postal_town") ||
      part.types.includes("administrative_area_level_2"),
  );
  return match?.longText ?? "";
}

/*
  The map in the book's own colours.

  Google's default map is a blue-and-grey street atlas, and it sat in the middle of a paper
  dialog like a window onto another product. This is the same paper: land the page's ivory,
  roads white with the page's hairline, the main roads the button's gold, water the brand's
  violet lightened to a wash, labels in the page's ink and soft ink. Shops and transit are off -
  a parent is finding their own door, not a cafe - and parks stay, faintly, because they are
  how people orient themselves in a Georgian city.

  A style array rather than a cloud map id, so it lives here with the rest of the palette and
  changes with it; the cost is the classic marker, see `ClassicMarker`.
*/
export const PAPER_MAP_STYLE: Record<string, unknown>[] = [
  { elementType: "geometry", stylers: [{ color: "#f3eee4" }] },
  { elementType: "labels.text.fill", stylers: [{ color: "#655f72" }] },
  { elementType: "labels.text.stroke", stylers: [{ color: "#fffdf8" }] },
  { elementType: "labels.icon", stylers: [{ visibility: "off" }] },
  {
    featureType: "administrative",
    elementType: "geometry.stroke",
    stylers: [{ color: "#d9d1c4" }],
  },
  {
    featureType: "administrative.locality",
    elementType: "labels.text.fill",
    stylers: [{ color: "#29233a" }],
  },
  { featureType: "landscape.natural", elementType: "geometry", stylers: [{ color: "#efe9dc" }] },
  { featureType: "landscape.man_made", elementType: "geometry", stylers: [{ color: "#f6f1e7" }] },
  { featureType: "poi", stylers: [{ visibility: "off" }] },
  {
    featureType: "poi.park",
    elementType: "geometry",
    stylers: [{ color: "#e4ebd7" }, { visibility: "on" }],
  },
  { featureType: "road", elementType: "geometry", stylers: [{ color: "#ffffff" }] },
  { featureType: "road", elementType: "geometry.stroke", stylers: [{ color: "#e2dbcf" }] },
  { featureType: "road", elementType: "labels.text.fill", stylers: [{ color: "#29233a" }] },
  { featureType: "road.arterial", elementType: "geometry", stylers: [{ color: "#fdf6e6" }] },
  { featureType: "road.highway", elementType: "geometry", stylers: [{ color: "#f2dc9e" }] },
  { featureType: "road.highway", elementType: "geometry.stroke", stylers: [{ color: "#dda95c" }] },
  { featureType: "transit", stylers: [{ visibility: "off" }] },
  { featureType: "water", elementType: "geometry", stylers: [{ color: "#dcd5ef" }] },
  { featureType: "water", elementType: "labels.text.fill", stylers: [{ color: "#7a5cc4" }] },
];

/* The pin: the brand's violet with the pendant's gold at its heart, anchored at its point. */
export const PIN_ICON_URL =
  "data:image/svg+xml;charset=UTF-8," +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="34" height="46" viewBox="0 0 34 46">' +
      '<path d="M17 45C17 45 3 28.5 3 16.5A14 14 0 0 1 31 16.5C31 28.5 17 45 17 45Z" fill="#7a5cc4" stroke="#fffdf8" stroke-width="2"/>' +
      '<circle cx="17" cy="16.5" r="5.5" fill="#f2dc9e" stroke="#dda95c" stroke-width="1.5"/>' +
      "</svg>",
  );
