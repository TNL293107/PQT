import { useId } from "react";
import type { Bar } from "../../types/marketData";

interface SparklineProps {
  readonly bars: readonly Bar[];
  readonly label: string;
}

/** Plot geometry. Wide and short, because it sits inside a transcript line. */
const WIDTH = 640;
const HEIGHT = 120;
const PADDING = 4;

/**
 * The close series, drawn small.
 *
 * A price series in a console is worth drawing rather than tabulating: the one
 * thing a reader wants from forty closes is the shape, and a corporate action
 * detaching shows up as a cliff that no column of numbers makes obvious.
 *
 * One series, so no legend — the line above it names what this is. The extremes
 * are labelled and nothing else is: a number on every point is unreadable at
 * this size and hides the shape it was meant to support.
 */
export function Sparkline({ bars, label }: SparklineProps) {
  const gradientId = useId();

  if (bars.length < 2) {
    return null;
  }

  const closes = bars.map((bar) => bar.close);
  const low = Math.min(...closes);
  const high = Math.max(...closes);
  const span = high - low || 1;

  const x = (index: number) =>
    PADDING + (index / (bars.length - 1)) * (WIDTH - PADDING * 2);
  const y = (value: number) =>
    PADDING + (1 - (value - low) / span) * (HEIGHT - PADDING * 2);

  const line = closes.map((close, index) => `${index === 0 ? "M" : "L"}${x(index)} ${y(close)}`).join(" ");
  const area = `${line} L${x(closes.length - 1)} ${HEIGHT} L${x(0)} ${HEIGHT} Z`;

  const first = closes[0] ?? 0;
  const last = closes[closes.length - 1] ?? 0;
  const rising = last >= first;

  return (
    <svg
      className={`spark ${rising ? "spark--up" : "spark--down"}`}
      viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
      preserveAspectRatio="none"
      role="img"
      aria-label={`${label}. ${bars.length} sessions, low ${low}, high ${high}.`}
    >
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" className="spark__stop spark__stop--top" />
          <stop offset="100%" className="spark__stop spark__stop--bottom" />
        </linearGradient>
      </defs>

      <path className="spark__area" d={area} fill={`url(#${gradientId})`} />
      <path className="spark__line" d={line} />
    </svg>
  );
}
