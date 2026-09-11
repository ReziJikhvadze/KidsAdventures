import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { AddressAutocompleteField } from "@/components/adventrya/journey/AddressAutocompleteField";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { useT } from "@/lib/i18n";
import {
  PAPER_MAP_STYLE,
  PIN_ICON_URL,
  addressAt,
  loadGoogleMaps,
  locateAddress,
  mapsLibrary,
  markerLibrary,
  type ClassicMarker,
  type LatLngLike,
  type MapLike,
} from "@/lib/maps/googleMaps";

type Chosen = { address: string; city: string };

/** An answer with, when it came from a search, the point to take the map to. */
type Located = Chosen & { location?: LatLngLike | null };

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChoose: (chosen: Chosen) => void;
  /**
   * The address the form already holds, so the map opens on it rather than on the city centre.
   *
   * A parent who chose a street from the list and then opened the map to check the building was
   * met by an empty search box and a pin on nothing - the address they had just given was gone.
   * It is found and pinned as the map opens, and the box carries it, so the map is a look at what
   * they said rather than a fresh start.
   */
  initialAddress?: string;
};

/* Tbilisi, so the map opens near where most parcels go rather than wherever Google guesses. */
const TBILISI = { lat: 41.7151, lng: 44.8271 };

/**
 * Find the address on a map: search, drop or drag the pin, read what it says, confirm.
 *
 * A typed Georgian street address is the hardest thing on the form to get right and the most
 * expensive to get wrong - a printed book goes to a courier, and a courier with a misspelt street
 * either rings or gives up. Searching resolves it against Google's own record; tapping the map or
 * dragging the pin resolves a point, and Google reads the address back from under it. Either way
 * what the parent sees and confirms is an address, never coordinates: a pin on a roof is worth
 * nothing to a courier, a pin that says "ჭავჭავაძის 12" is an address like any other.
 *
 * The search box is the form's own field rather than Google's element, so it looks like the rest
 * of the page, can be opened holding the address the form already has, and shows the address the
 * pin lands on - the box and the pin always agree.
 *
 * Everything below the street - entrance, floor, flat, door code - is deliberately NOT here. No
 * map knows it and the parent always does, so it is a note on the form behind this dialog.
 */
export function LocationPickerDialog({ open, onOpenChange, onChoose, initialAddress }: Props) {
  const t = useT();
  const [state, setState] = useState<"loading" | "ready" | "unavailable">("loading");
  const [chosen, setChosen] = useState<Chosen | null>(null);
  /** What the search box says. Set by typing, by a choice off the list, and by the pin. */
  const [query, setQuery] = useState("");
  const mapHost = useRef<HTMLDivElement | null>(null);
  /** The map and its one pin, for the search box's choices to move. Null until the map stands. */
  const scene = useRef<{ map: MapLike; pin: ClassicMarker } | null>(null);
  /** Whether the dialog is still open: answers that arrive after a close write nothing. */
  const alive = useRef(false);
  /*
    Which selection is the live one.

    Looking a place up is a round trip, so two quick picks are two requests that can land in
    either order - and between the pick and its answer the previous address is still sitting in
    state, still confirmable. A parent who chooses B and presses confirm straight away would have
    sent A. The counter makes every pick invalidate the one before it: only the newest answer is
    allowed to write, and nothing is confirmable until it arrives.
  */
  const selection = useRef(0);

  /*
    Every way of choosing goes through here: what is on screen is dropped synchronously, the
    answer is fetched, and only the newest answer may write. A found address also goes into the
    search box and, when it came with a point, takes the map there.
  */
  const hold = (resolve: () => Promise<Located | null>) => {
    const ticket = ++selection.current;
    setChosen(null);
    void (async () => {
      let found: Located | null = null;
      try {
        found = await resolve();
      } catch {
        /* Nothing is held, so nothing can be confirmed by mistake; the hint asks again. */
        found = null;
      }
      if (!alive.current || ticket !== selection.current) return;
      setChosen(found ? { address: found.address, city: found.city } : null);
      if (!found) return;
      setQuery(found.address);
      const current = scene.current;
      if (current && found.location) {
        current.pin.setPosition(found.location);
        current.pin.setVisible(true);
        current.map.setCenter(found.location);
        current.map.setZoom(17);
      }
    })();
  };

  /* A point becomes an address by being read back; the pin stays where it was put. */
  const holdPoint = (at: LatLngLike) => {
    scene.current?.pin.setPosition(at);
    scene.current?.pin.setVisible(true);
    hold(() => addressAt(at));
  };

  /* Words become a point by being looked up; the pin goes where Google says they are. */
  const holdText = (text: string) => {
    hold(() => locateAddress(text));
  };

  useEffect(() => {
    if (!open) return;
    alive.current = true;
    const opensOn = initialAddress?.trim() ?? "";

    void (async () => {
      setState("loading");
      setChosen(null);
      setQuery(opensOn);
      scene.current = null;

      const ok = await loadGoogleMaps();
      if (!alive.current) return;
      if (!ok) {
        setState("unavailable");
        return;
      }

      try {
        const [maps, markers] = await Promise.all([mapsLibrary(), markerLibrary()]);
        if (!alive.current || !mapHost.current) return;

        const map = new maps.Map(mapHost.current, {
          center: TBILISI,
          zoom: 12,
          disableDefaultUI: true,
          zoomControl: true,
          // A tap on a shop must drop our pin, not open Google's card about the shop.
          clickableIcons: false,
          // The page's own paper, see PAPER_MAP_STYLE. No map id: a cloud id would ignore it.
          styles: PAPER_MAP_STYLE,
        });
        /*
          One pin, moved rather than multiplied, and hidden until something has put it
          somewhere: a pin standing on the city centre before anything was chosen looked like an
          answer while the line under the map was saying there was none yet.
        */
        const pin = new markers.Marker({
          map,
          position: TBILISI,
          visible: false,
          draggable: true,
          icon: PIN_ICON_URL,
        });
        scene.current = { map, pin };

        map.addListener("click", (event) => {
          if (event.latLng) holdPoint(event.latLng);
        });
        pin.addListener("dragend", () => {
          const at = pin.getPosition();
          if (at) holdPoint(at);
        });

        setState("ready");
        if (opensOn) holdText(opensOn);
      } catch {
        if (alive.current) setState("unavailable");
      }
    })();

    return () => {
      alive.current = false;
    };
    // The address is read as the dialog opens and not again: a form edit behind an open map
    // must not move the pin under the parent's finger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="ux-location-dialog" overlayClassName="ux-location-overlay">
        {/* The title says what this is; the line under it says the three moves, in order. */}
        <div className="ux-location-head">
          <DialogTitle>{t.journey.checkout.pickLocationTitle}</DialogTitle>
          <p className="ux-location-steps">{t.journey.checkout.pickLocationSteps}</p>
        </div>

        {state === "unavailable" ? (
          /* Said plainly rather than shown as a broken map: the address typed by hand is a
             complete answer, and this dialog is a convenience over it, not a gate in front. */
          <DialogDescription className="ux-form-error">
            {t.journey.checkout.pickLocationUnavailable}
          </DialogDescription>
        ) : (
          <>
            <AddressAutocompleteField
              id="location-picker-search"
              value={query}
              onChange={setQuery}
              onChoose={({ address }) => {
                setQuery(address);
                holdText(address);
              }}
              fieldClassName="field"
              className="ux-location-search"
              label={t.journey.checkout.pickLocationSearch}
              placeholder={t.journey.checkout.addressPlaceholder}
            />
            {/*
              Google gets a box of its own, with nothing of React's inside it: Google empties the
              element it is handed and fills it with tiles, so the loader is a sibling laid over
              it rather than a child React would later fail to remove.
            */}
            <div className="ux-location-map">
              <div className="ux-location-canvas" ref={mapHost} />
              {state === "loading" ? (
                <div className="ux-location-map-wait">
                  <BekiLoader size={44} />
                </div>
              ) : null}
            </div>
            {/*
              What will be confirmed, named as such. Before a pick it says what to do; after one
              it is the address, and a line that the pin can still be moved. It is the dialog's
              description, so a screen reader hears it with the title and again when it changes.
            */}
            <DialogDescription
              className={`ux-location-chosen${chosen ? " is-set" : ""}`}
              role="status"
              aria-live="polite"
            >
              <small>{t.journey.checkout.pickLocationSelected}</small>
              {chosen ? (
                <>
                  <strong>{chosen.address}</strong>
                  <span>{t.journey.checkout.pickLocationDrag}</span>
                </>
              ) : (
                <span>{t.journey.checkout.pickLocationHint}</span>
              )}
            </DialogDescription>
          </>
        )}

        <div className="ux-location-actions">
          <button
            className="button ux-location-cancel"
            type="button"
            onClick={() => onOpenChange(false)}
          >
            {t.common.actions.cancel}
          </button>
          <button
            className="button button-primary"
            type="button"
            disabled={!chosen}
            onClick={() => {
              if (!chosen) return;
              onChoose(chosen);
              onOpenChange(false);
            }}
          >
            {t.journey.checkout.pickLocationConfirm}
          </button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
