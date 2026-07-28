import { useEffect, type RefObject } from "react";

/**
 * On narrow viewports the map canvas is wider than the frame and scrolls
 * horizontally. Keep the active world node roughly centred when it changes.
 */
export function useCenterMapNode(
  scrollRef: RefObject<HTMLElement | null>,
  selector: string,
  activeId: string | null | undefined,
  maxWidth = 780,
) {
  useEffect(() => {
    const scroller = scrollRef.current;
    if (!scroller || !activeId) return;
    if (!window.matchMedia(`(max-width: ${maxWidth}px)`).matches) return;

    const node = scroller.querySelector(selector);
    if (!(node instanceof HTMLElement)) return;

    const left = node.offsetLeft - scroller.clientWidth / 2 + node.offsetWidth / 2;
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    scroller.scrollTo({ left: Math.max(0, left), behavior: reduce ? "auto" : "smooth" });
  }, [scrollRef, selector, activeId, maxWidth]);
}
