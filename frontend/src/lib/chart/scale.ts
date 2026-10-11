/**
 * Scales and hit-testing for the price chart.
 *
 * The x-axis is a band per session, not a time scale. A trading calendar has
 * weekends, holidays and the occasional suspension in it, and a time scale
 * would draw each of them as an empty gap the reader has to look past to see
 * consecutive sessions. Sessions are what was traded, so sessions are what is
 * spaced evenly; the date labels say where the calendar falls.
 */

/** Maps a value onto a pixel coordinate. */
export type Scale = (value: number) => number;

/**
 * A linear map from a domain to a pixel range.
 *
 * A domain of zero width maps everything to the middle of the range rather
 * than dividing by zero — a series that never moved is drawn as a level line,
 * not as nothing.
 */
export function linearScale(
  domain: readonly [number, number],
  range: readonly [number, number],
): Scale {
  const [d0, d1] = domain;
  const [r0, r1] = range;
  const span = d1 - d0;

  if (span === 0 || !Number.isFinite(span)) {
    const middle = (r0 + r1) / 2;
    return () => middle;
  }

  const ratio = (r1 - r0) / span;
  return (value) => r0 + (value - d0) * ratio;
}

/** Where a run of evenly spaced sessions sits horizontally. */
export interface Band {
  /** Left edge of the plot, in pixels. */
  readonly left: number;

  /** Width of the plot, in pixels. */
  readonly width: number;

  /** How many sessions share it. */
  readonly count: number;
}

/** The width one session is given. */
export function slotWidth(band: Band): number {
  return band.count > 0 ? band.width / band.count : band.width;
}

/** The horizontal centre of a session's slot. */
export function bandCenter(band: Band, index: number): number {
  return band.left + (index + 0.5) * slotWidth(band);
}

/**
 * The session under a horizontal pixel position.
 *
 * Clamped rather than refused at the edges: the pointer is often a pixel or two
 * outside the plot when it is aimed at the first or last session, and a
 * readout that blanked there would flicker exactly where it is most wanted.
 *
 * @returns The session index, or null when there are no sessions at all.
 */
export function indexAt(band: Band, x: number): number | null {
  if (band.count <= 0) {
    return null;
  }

  if (!Number.isFinite(x)) {
    return band.count - 1;
  }

  const slot = slotWidth(band);
  const raw = slot > 0 ? Math.floor((x - band.left) / slot) : 0;

  return Math.min(band.count - 1, Math.max(0, raw));
}

/** How far PageUp and PageDown move, in sessions — about two trading weeks. */
export const PAGE_STEP = 10;

/**
 * Where a key moves the selected session.
 *
 * With nothing selected the readout already shows the last session, so the
 * first ← moves to the one before it rather than jumping to the start.
 *
 * @param current The selected session, or null when the readout is at rest.
 * @param key The `KeyboardEvent.key` pressed.
 * @param count How many sessions there are.
 * @returns The new index, or undefined when the key is not a chart key and
 * should be left to the browser.
 */
export function stepIndex(current: number | null, key: string, count: number): number | undefined {
  if (count <= 0) {
    return undefined;
  }

  const last = count - 1;
  const from = current ?? last;

  switch (key) {
    case "ArrowLeft":
      return current === null ? Math.max(0, last - 1) : Math.max(0, from - 1);
    case "ArrowRight":
      return Math.min(last, current === null ? last : from + 1);
    case "PageUp":
      return Math.max(0, from - PAGE_STEP);
    case "PageDown":
      return Math.min(last, from + PAGE_STEP);
    case "Home":
      return 0;
    case "End":
      return last;
    default:
      return undefined;
  }
}
