import { MapPin } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";

import { useT } from "@/lib/i18n";
import {
  loadGoogleMaps,
  newAddressSession,
  resolvePrediction,
  suggestAddresses,
  type PlacePrediction,
} from "@/lib/maps/googleMaps";

export type ChosenAddress = { address: string; city: string };

type Props = {
  id: string;
  value: string;
  onChange: (address: string) => void;
  /** A whole address off the list, city included — the caller decides what to do with the city. */
  onChoose: (chosen: ChosenAddress) => void;
  /** Opens the map dialog. The button under the field is hidden when this is omitted. */
  onPickOnMap?: () => void;
  /** `field` on the create journey, `journey-field` in the parent's space. Styles the label. */
  fieldClassName?: string;
  /**
   * Goes on the wrapper, which is the grid child. Both forms put this field across the full
   * width of a two-column grid, and the modifier that does that — `field-wide`,
   * `journey-field-wide` — has to be on the element the grid is actually laying out.
   */
  className?: string;
  placeholder?: string;
  label: string;
  autoComplete?: string;
  name?: string;
};

/* Long enough that a fast typist does not spend a lookup per letter, short enough to feel live. */
const DEBOUNCE_MS = 220;

/**
 * The recipient's address, with Google's suggestions in our own list.
 *
 * Two ways in, one under the other, in the order they are wanted. Typing is what nearly everyone
 * does and it now completes itself; the map is for the address nobody can spell, and it is a
 * button rather than a link because opening a full-screen picker is not a footnote to a field.
 *
 * The list is drawn here rather than by Google's `PlaceAutocompleteElement`. That element brings
 * its own input and its own styling, which is right inside the map dialog — Google's box in
 * Google's box — and wrong standing between a recipient's name and their phone number, where it
 * would be the one control on the form that does not match the rest of it.
 *
 * With no key configured, `loadGoogleMaps()` resolves false: no list, no map button, and an
 * ordinary text field that works exactly as it did before. That is the state this deployment is
 * actually in today, so it is the state the field is written for rather than a fallback.
 */
export function AddressAutocompleteField({
  id,
  value,
  onChange,
  onChoose,
  onPickOnMap,
  fieldClassName = "field",
  className,
  placeholder,
  label,
  autoComplete = "street-address",
  name,
}: Props) {
  const t = useT();
  const [ready, setReady] = useState(false);
  const [options, setOptions] = useState<PlacePrediction[]>([]);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const listId = useId();

  /*
    Google bills a whole search as one lookup while the same token is passed, and starts charging
    per keystroke the moment it is dropped. A token belongs to one search: it is taken when the
    first suggestion is asked for and thrown away when an address is chosen.
  */
  const session = useRef<unknown>(undefined);
  /*
    Which request the answer belongs to. Suggestions come back out of order on a slow connection,
    so an older, shorter query could otherwise land on top of a newer one and leave the list
    showing results for half of what has been typed.
  */
  const run = useRef(0);
  /* What the parent last chose, so choosing does not immediately look like typing. */
  const chosen = useRef<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void loadGoogleMaps().then((ok) => {
      if (!cancelled) setReady(ok);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!ready) return;

    const typed = value.trim();
    // Nothing under three characters: one letter matches the whole country and costs a lookup.
    if (typed.length < 3 || typed === chosen.current) {
      setOptions([]);
      return;
    }

    const ticket = ++run.current;
    const timer = window.setTimeout(async () => {
      if (session.current === undefined) session.current = await newAddressSession();
      const found = await suggestAddresses(typed, session.current);
      if (ticket !== run.current) return;
      setOptions(found.slice(0, 5));
      setActive(-1);
    }, DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [value, ready]);

  const take = async (prediction: PlacePrediction) => {
    const resolved = await resolvePrediction(prediction);
    // The session is spent whether or not Google could resolve the place; the next search is a
    // new one either way.
    session.current = undefined;
    setOptions([]);
    setOpen(false);
    if (!resolved) return;
    chosen.current = resolved.address;
    onChoose(resolved);
  };

  const showList = open && options.length > 0;

  return (
    <div className={className ? `ux-address-field ${className}` : "ux-address-field"}>
      {/* The label and its list, in a box of their own: the suggestions hang off the bottom of
          the field, and the map button below must not push them further down the page. */}
      <div className="ux-address-input">
        <label className={fieldClassName} htmlFor={id}>
          <span>{label}</span>
          <input
            id={id}
            name={name}
            autoComplete={autoComplete}
            placeholder={placeholder}
            value={value}
            role="combobox"
            aria-expanded={showList}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={active >= 0 ? `${listId}-${active}` : undefined}
            onChange={(event) => {
              chosen.current = null;
              setOpen(true);
              onChange(event.target.value);
            }}
            onFocus={() => setOpen(true)}
            /*
            Late enough that a press on a suggestion is not cancelled by the blur that precedes
            it. `onMouseDown` with `preventDefault` on the row would be the other way; this keeps
            the row an ordinary button, which is what a screen reader and a keyboard both want.
          */
            onBlur={() => window.setTimeout(() => setOpen(false), 140)}
            onKeyDown={(event) => {
              if (!showList) return;
              if (event.key === "ArrowDown") {
                event.preventDefault();
                setActive((i) => (i + 1) % options.length);
              } else if (event.key === "ArrowUp") {
                event.preventDefault();
                setActive((i) => (i <= 0 ? options.length - 1 : i - 1));
              } else if (event.key === "Enter" && active >= 0) {
                event.preventDefault();
                void take(options[active]);
              } else if (event.key === "Escape") {
                setOpen(false);
              }
            }}
          />
        </label>

        {showList ? (
          <ul className="ux-address-suggestions" id={listId} role="listbox">
            {options.map((option, index) => {
              const main = option.mainText?.toString() ?? option.text?.toString() ?? "";
              const rest = option.secondaryText?.toString() ?? "";
              return (
                <li key={`${main}-${index}`} role="presentation">
                  <button
                    type="button"
                    id={`${listId}-${index}`}
                    role="option"
                    aria-selected={index === active}
                    className={index === active ? "is-active" : undefined}
                    onClick={() => void take(option)}
                  >
                    <strong>{main}</strong>
                    {rest ? <small>{rest}</small> : null}
                  </button>
                </li>
              );
            })}
          </ul>
        ) : null}
      </div>

      {/* The map, for the address nobody can spell. Only where there is a map to open. */}
      {ready && onPickOnMap ? (
        <button className="ux-address-map-button" type="button" onClick={onPickOnMap}>
          <MapPin aria-hidden="true" size={15} />
          {t.journey.checkout.pickLocation}
        </button>
      ) : null}
    </div>
  );
}
