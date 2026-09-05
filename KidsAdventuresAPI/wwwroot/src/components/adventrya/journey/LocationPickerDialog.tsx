import { useEffect, useRef, useState } from "react";

import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { useT } from "@/lib/i18n";
import {
  cityOf,
  loadGoogleMaps,
  mapsLibrary,
  markerLibrary,
  placesLibrary,
  type PlacePrediction,
} from "@/lib/maps/googleMaps";

type Chosen = { address: string; city: string };

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChoose: (chosen: Chosen) => void;
};

/**
 * Find the address on a map instead of spelling it out.
 *
 * A typed Georgian street address is the hardest thing on this form to get right and the most
 * expensive to get wrong: a printed book goes to a courier, and a courier with a misspelt street
 * either rings or gives up. Searching resolves it against Google's own record, so what reaches
 * the order is an address that exists.
 *
 * The map is here to confirm, not to pick: a pin dropped on a roof does not tell a courier which
 * building it is, while a chosen place does. So the map follows the search and never leads it.
 *
 * Everything below the street — entrance, floor, flat, door code — is deliberately NOT here. No
 * map knows it and the parent always does, so it is a note on the form behind this dialog.
 */
export function LocationPickerDialog({ open, onOpenChange, onChoose }: Props) {
  const t = useT();
  const [state, setState] = useState<"loading" | "ready" | "unavailable">("loading");
  const [chosen, setChosen] = useState<Chosen | null>(null);
  const searchHost = useRef<HTMLDivElement | null>(null);
  const mapHost = useRef<HTMLDivElement | null>(null);
  /*
    Which selection is the live one.

    Looking a place up is a round trip, so two quick picks are two requests that can land in
    either order — and between the pick and its answer the previous address is still sitting in
    state, still confirmable. A parent who chooses B and presses confirm straight away would have
    sent A. The counter makes every pick invalidate the one before it: only the newest answer is
    allowed to write, and nothing is confirmable until it arrives.
  */
  const selection = useRef(0);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    void (async () => {
      setState("loading");
      setChosen(null);

      const ok = await loadGoogleMaps();
      if (cancelled) return;
      if (!ok) {
        setState("unavailable");
        return;
      }

      try {
        const [places, maps, markers] = await Promise.all([
          placesLibrary(),
          mapsLibrary(),
          markerLibrary(),
        ]);
        if (cancelled || !searchHost.current || !mapHost.current) return;

        // Tbilisi, so the first predictions are near where most parcels go rather than
        // wherever Google guesses from an IP address.
        const start = { lat: 41.7151, lng: 44.8271 };
        const map = new maps.Map(mapHost.current, {
          center: start,
          zoom: 12,
          mapId: "DEMO_MAP_ID",
          disableDefaultUI: true,
          zoomControl: true,
        }) as unknown as { setCenter(p: unknown): void; setZoom(z: number): void };

        // One marker, moved by each search rather than a new one littering the map.
        const pin = new markers.AdvancedMarkerElement({ map, position: start });

        const search = new places.PlaceAutocompleteElement({ includedRegionCodes: ["ge"] });
        search.id = "location-picker-search";
        searchHost.current.replaceChildren(search);

        search.addEventListener("gmp-select", (event: Event) => {
          const prediction = (event as unknown as { placePrediction?: PlacePrediction })
            .placePrediction;
          if (!prediction) return;

          const ticket = ++selection.current;
          // Synchronously, before the round trip: what is on screen is no longer what is held.
          setChosen(null);

          void (async () => {
            try {
              const place = prediction.toPlace();
              await place.fetchFields({
                fields: ["formattedAddress", "location", "addressComponents"],
              });
              if (cancelled || ticket !== selection.current) return;

              const address = place.formattedAddress?.trim() ?? "";
              if (!address) return;

              setChosen({ address, city: cityOf(place) });
              if (place.location) {
                map.setCenter(place.location);
                map.setZoom(17);
                pin.position = place.location;
              }
            } catch {
              /*
                A lookup can fail on its own — network, quota, a server error — and leaving the
                previous address confirmable would let the parent send an address they had
                already moved on from. Nothing is held, so nothing can be confirmed by mistake;
                the hint below the map asks them to pick again.
              */
              if (!cancelled && ticket === selection.current) setChosen(null);
            }
          })();
        });

        setState("ready");
      } catch {
        if (!cancelled) setState("unavailable");
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open]);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="ux-location-dialog">
        <DialogTitle>{t.journey.checkout.pickLocationTitle}</DialogTitle>

        {state === "unavailable" ? (
          /* Said plainly rather than shown as a broken map: the address typed by hand is a
             complete answer, and this dialog is a convenience over it, not a gate in front. */
          <p className="ux-form-error">{t.journey.checkout.pickLocationUnavailable}</p>
        ) : (
          <>
            <div className="ux-location-search" ref={searchHost} />
            <div className="ux-location-map" ref={mapHost}>
              {state === "loading" ? (
                <div className="ux-location-map-wait">
                  <BekiLoader size={44} />
                </div>
              ) : null}
            </div>
            <p className="ux-location-chosen" role="status" aria-live="polite">
              {chosen?.address ?? t.journey.checkout.pickLocationHint}
            </p>
          </>
        )}

        <div className="ux-location-actions">
          <button className="button" type="button" onClick={() => onOpenChange(false)}>
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
