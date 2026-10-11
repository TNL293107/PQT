import { useLayoutEffect, useRef, useState, type RefObject } from "react";

/**
 * Tracks an element's rendered width.
 *
 * Measured in a layout effect, so the first paint already uses the real width
 * and the chart never draws once at the fallback and then jumps. Rounded to a
 * whole pixel, because sub-pixel widths change on every zoom step and each
 * change would redraw a few hundred SVG nodes for nothing.
 *
 * Without ResizeObserver — jsdom, and any environment that reports a zero
 * width — the fallback stands, which is what keeps the chart testable.
 *
 * @param fallback The width to assume until, or unless, one is measured.
 */
export function useElementWidth<T extends Element>(
  fallback: number,
): readonly [RefObject<T | null>, number] {
  const ref = useRef<T | null>(null);
  const [width, setWidth] = useState(fallback);

  useLayoutEffect(() => {
    const element = ref.current;

    if (element === null) {
      return undefined;
    }

    const accept = (measured: number) => {
      if (measured > 0 && Number.isFinite(measured)) {
        setWidth(Math.round(measured));
      }
    };

    accept(element.getBoundingClientRect().width);

    if (typeof ResizeObserver === "undefined") {
      return undefined;
    }

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry !== undefined) {
        accept(entry.contentRect.width);
      }
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return [ref, width] as const;
}
