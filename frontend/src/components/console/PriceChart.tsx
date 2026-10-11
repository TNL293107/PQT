import {
  memo,
  useId,
  useMemo,
  useState,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
} from "react";
import { useElementWidth } from "../../hooks/useElementWidth";
import { formatCompactVolume, formatFactor, formatPrice } from "../../lib/chart/format";
import { chartLayout, FALLBACK_WIDTH, type ChartLayout } from "../../lib/chart/layout";
import { bandCenter, indexAt, slotWidth, stepIndex } from "../../lib/chart/scale";
import {
  chartSummary,
  describeSeries,
  directionOf,
  readoutFor,
  sessionDay,
  type ChartStyle,
  type Readout,
} from "../../lib/chart/series";
import type { Instrument } from "../../types/instrument";
import type { Bar, BarSeries } from "../../types/marketData";

interface PriceChartProps {
  readonly instrument: Instrument;
  readonly series: BarSeries;
  /** The as-of the caller asked for, so the caption states the cut. */
  readonly knownAsOf: string | null;
  readonly style?: ChartStyle;
}

/** Room either side of a date label so it is never clipped at the plot's edge. */
const DATE_LABEL_INSET = 22;

/**
 * GP's price chart: a price pane, a volume pane under it on the same sessions,
 * a crosshair with a readout, and a caption saying which series this is.
 *
 * SVG written here rather than a charting library (ADR-026): the console is
 * tested under jsdom, where canvas does not render, and a few hundred sessions
 * do not need one.
 *
 * Up and down are told apart by shape as well as colour — a session that
 * closed above its open is a hollow body, one that closed below is filled — so
 * the chart reads the same to a reader who cannot tell red from green.
 */
export function PriceChart({ instrument, series, knownAsOf, style = "candles" }: PriceChartProps) {
  const [containerRef, measuredWidth] = useElementWidth<HTMLDivElement>(FALLBACK_WIDTH);
  const [active, setActive] = useState<number | null>(null);
  const captionId = useId();

  const { bars } = series;
  const layout = useMemo(() => chartLayout(bars, style, measuredWidth), [bars, style, measuredWidth]);
  const exDates = useMemo(() => new Set(layout.exDates), [layout]);

  if (bars.length === 0) {
    return (
      <p className="out__line out__line--dim">
        No sessions to draw for {instrument.ticker}.
      </p>
    );
  }

  const last = bars.length - 1;
  const readout = readoutFor(bars, active ?? last, exDates);
  const caption = describeSeries(series, knownAsOf);

  const onPointerMove = (event: PointerEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    // The viewBox is the measured width, so this is one unless the element
    // has been resized since the last measurement — or has no layout at all.
    const ratio = box.width > 0 ? layout.width / box.width : 1;
    setActive(indexAt(layout.band, (event.clientX - box.left) * ratio));
  };

  const onKeyDown = (event: KeyboardEvent<SVGSVGElement>) => {
    if (event.key === "Escape") {
      setActive(null);
      return;
    }

    const next = stepIndex(active, event.key, bars.length);

    if (next !== undefined) {
      // Arrow keys and Home/End would otherwise scroll the transcript out from
      // under the session being read.
      event.preventDefault();
      setActive(next);
    }
  };

  // The console focuses its prompt on any click. A click on the chart is a
  // request to read it, so it keeps focus here where the arrow keys work.
  const onClick = (event: MouseEvent<SVGSVGElement>) => event.stopPropagation();

  return (
    <figure className="chart">
      {readout === null ? null : <ChartReadout readout={readout} />}

      <div className="chart__frame" ref={containerRef}>
        <svg
          className="chart__svg"
          viewBox={`0 0 ${layout.width} ${layout.height}`}
          role="img"
          aria-label={chartSummary(instrument.ticker, series, style)}
          aria-describedby={captionId}
          tabIndex={0}
          onPointerMove={onPointerMove}
          onPointerLeave={() => setActive(null)}
          onFocus={() => setActive((current) => current ?? last)}
          onBlur={() => setActive(null)}
          onKeyDown={onKeyDown}
          onClick={onClick}
        >
          <Plot bars={bars} layout={layout} style={style} />
          {active === null ? null : <Crosshair bar={bars[active]} index={active} layout={layout} />}
        </svg>
      </div>

      <figcaption className="chart__caption" id={captionId}>
        <span className="chart__what">
          {instrument.ticker} · {series.interval} · prices in {instrument.currency}
          {style === "candles" ? " · hollow body closed above its open, filled closed below" : ""}
          {layout.exDates.length > 0 ? " · ▲ marks an ex-date, where the price factor changes" : ""}
        </span>
        {caption.map((sentence) => (
          <span className="chart__sentence" key={sentence}>
            {sentence}
          </span>
        ))}
        <span className="chart__keys">
          Focus the chart: ← → step a session, PgUp PgDn ten, Home End to either end, Esc to let go.
        </span>
      </figcaption>
    </figure>
  );
}

/**
 * The plot itself — everything that does not move with the crosshair.
 *
 * Memoised on the layout, so a pointer crossing a few hundred candles redraws
 * the crosshair, not the candles.
 */
const Plot = memo(function Plot({
  bars,
  layout,
  style,
}: {
  readonly bars: readonly Bar[];
  readonly layout: ChartLayout;
  readonly style: ChartStyle;
}) {
  const { plot, priceY, volumeY, band } = layout;

  return (
    <g>
      <g className="chart__axis">
        {layout.price.ticks.map((tick) => (
          <g key={tick}>
            <line className="chart__grid" x1={plot.left} x2={plot.right} y1={priceY(tick)} y2={priceY(tick)} />
            <text className="chart__tick" x={plot.right + 6} y={priceY(tick)} dominantBaseline="middle">
              {formatPrice(tick, layout.priceDecimals)}
            </text>
          </g>
        ))}

        <line className="chart__rule" x1={plot.left} x2={plot.right} y1={plot.volumeBottom} y2={plot.volumeBottom} />
        <text className="chart__tick" x={plot.right + 6} y={plot.volumeTop} dominantBaseline="hanging">
          {formatCompactVolume(layout.volume.domain[1])}
        </text>
        <text className="chart__tick chart__tick--dim" x={plot.right + 6} y={plot.volumeBottom}>
          VOL
        </text>

        {layout.dates.ticks.map((tick) => {
          const x = bandCenter(band, tick.index);
          const labelX = Math.min(plot.right - DATE_LABEL_INSET, Math.max(plot.left + DATE_LABEL_INSET, x));
          return (
            <g key={tick.index}>
              <line className="chart__rule" x1={x} x2={x} y1={plot.volumeBottom} y2={plot.volumeBottom + 4} />
              <text className="chart__tick" x={labelX} y={plot.volumeBottom + 16} textAnchor="middle">
                {tick.label}
              </text>
            </g>
          );
        })}
      </g>

      {layout.exDates.map((index) => (
        <ExDateMarker key={index} bars={bars} index={index} layout={layout} />
      ))}

      {style === "candles" ? (
        <g className="chart__candles">
          {bars.map((bar, index) => (
            <Candle key={bar.openedAtUtc} bar={bar} x={bandCenter(band, index)} layout={layout} />
          ))}
        </g>
      ) : (
        <CloseLine bars={bars} layout={layout} />
      )}

      <g className="chart__volume">
        {layout.maxVolume > 0 ? (
          bars.map((bar, index) => {
            const top = volumeY(bar.volume);
            return (
              <rect
                key={bar.openedAtUtc}
                className={`chart__vol chart__vol--${directionOf(bar)}`}
                x={bandCenter(band, index) - layout.bodyWidth / 2}
                y={top}
                width={layout.bodyWidth}
                height={Math.max(0, plot.volumeBottom - top)}
              />
            );
          })
        ) : (
          <text className="chart__tick chart__tick--dim" x={plot.left + 4} y={plot.volumeBottom - 6}>
            No volume reported for this window.
          </text>
        )}
      </g>
    </g>
  );
});

function Candle({ bar, x, layout }: { readonly bar: Bar; readonly x: number; readonly layout: ChartLayout }) {
  const { priceY, bodyWidth } = layout;
  const direction = directionOf(bar);
  const top = priceY(Math.max(bar.open, bar.close));
  const bottom = priceY(Math.min(bar.open, bar.close));

  return (
    <g className={`chart__candle chart__candle--${direction}`} data-direction={direction} data-session={sessionDay(bar)}>
      <line className="chart__wick" x1={x} x2={x} y1={priceY(bar.high)} y2={priceY(bar.low)} />
      <rect
        className="chart__body"
        x={x - bodyWidth / 2}
        y={top}
        width={bodyWidth}
        // A doji's body has no height; one pixel keeps it from vanishing.
        height={Math.max(1, bottom - top)}
      />
    </g>
  );
}

function CloseLine({ bars, layout }: { readonly bars: readonly Bar[]; readonly layout: ChartLayout }) {
  const points = bars.map((bar, index) => [bandCenter(layout.band, index), layout.priceY(bar.close)] as const);
  const path = points.map(([x, y], index) => `${index === 0 ? "M" : "L"}${x.toFixed(1)} ${y.toFixed(1)}`).join(" ");
  const [lastX, lastY] = points[points.length - 1]!;

  return (
    <g className="chart__closes">
      <path className="chart__line" d={path} />
      {/* The last close is marked, which is also what keeps a one-session line visible. */}
      <circle className="chart__dot" cx={lastX} cy={lastY} r={2.5} />
    </g>
  );
}

function ExDateMarker({
  bars,
  index,
  layout,
}: {
  readonly bars: readonly Bar[];
  readonly index: number;
  readonly layout: ChartLayout;
}) {
  const { plot } = layout;
  // On the boundary between the two sessions, because the ex-date is the
  // first session the old factor no longer covers.
  const x = layout.band.left + index * slotWidth(layout.band);
  const before = bars[index - 1]!.priceFactor;
  const from = bars[index]!.priceFactor;
  const y = plot.priceBottom;

  return (
    <g className="chart__exdate" data-session={sessionDay(bars[index]!)}>
      <title>
        {`Ex-date ${sessionDay(bars[index]!)}: price factor ${formatFactor(before)} before, ${formatFactor(from)} from this session`}
      </title>
      <line className="chart__exdate-rule" x1={x} x2={x} y1={plot.top} y2={plot.volumeBottom} />
      <path className="chart__exdate-mark" d={`M${x - 4} ${y} L${x + 4} ${y} L${x} ${y - 7} Z`} />
    </g>
  );
}

function Crosshair({
  bar,
  index,
  layout,
}: {
  readonly bar: Bar | undefined;
  readonly index: number;
  readonly layout: ChartLayout;
}) {
  if (bar === undefined) {
    return null;
  }

  const { plot } = layout;
  const x = bandCenter(layout.band, index);
  const y = layout.priceY(bar.close);

  return (
    <g className="chart__crosshair" aria-hidden="true">
      <line className="chart__cross" x1={x} x2={x} y1={plot.top} y2={plot.volumeBottom} />
      <line className="chart__cross" x1={plot.left} x2={plot.right} y1={y} y2={y} />
      <rect className="chart__tag" x={plot.right + 2} y={y - 8} width={layout.width - plot.right - 2} height={16} rx={2} />
      <text className="chart__tag-text" x={plot.right + 6} y={y} dominantBaseline="middle">
        {formatPrice(bar.close)}
      </text>
    </g>
  );
}

/**
 * The selected session in words.
 *
 * Always present — at rest it shows the last session — so the chart never
 * shifts when the pointer arrives, and polite so a screen reader hears each
 * session the arrow keys land on without being interrupted mid-sentence.
 */
function ChartReadout({ readout }: { readonly readout: Readout }) {
  return (
    <p className="chart__readout" aria-live="polite">
      <span className="chart__day">{readout.day}</span>
      <Field name="O" value={readout.open} />
      <Field name="H" value={readout.high} />
      <Field name="L" value={readout.low} />
      <Field name="C" value={readout.close} />
      <span className={`chart__chg is-${readout.direction}`}>
        {readout.change ?? "first session in window"}
      </span>
      <Field name="Vol" value={readout.volume} />
      {readout.adjustment === null ? null : (
        <span className="chart__flag">
          adjusted {formatFactor(readout.adjustment.factor)} — printed close {readout.adjustment.printedClose}
        </span>
      )}
      {readout.exDate ? <span className="chart__flag">▲ ex-date: the price factor changes here</span> : null}
    </p>
  );
}

function Field({ name, value }: { readonly name: string; readonly value: string }) {
  return (
    <span className="chart__field">
      <span className="chart__key">{name}</span> {value}
    </span>
  );
}
