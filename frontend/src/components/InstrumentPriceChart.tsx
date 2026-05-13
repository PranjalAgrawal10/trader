import { Fragment, useCallback, useMemo, type ReactNode } from 'react'
import {
  Bar,
  BarChart,
  Cell,
  ComposedChart,
  LabelList,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { CandlestickChart } from './CandlestickChart'
import { ChartWithRightGutter } from './ChartWithRightGutter'
import { MlDirectionRibbonSvg } from './MlDirectionRibbon'
import { attachLinearTrendToChartPoints, LINEAR_CLOSE_TREND_COLOR } from '../utils/closeLinearTrend'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import type { MlPredictionLogEntry } from '../utils/mlPredictionHistory'
import { mapMlPredictionsPerTargetBar } from '../utils/mlPredictionHistory'
import type { ChartPointWithMa, MaLineVisibility } from '../utils/movingAverages'
import {
  DEFAULT_MA_LINE_VISIBILITY,
  MA_EMA_FAST_PERIOD,
  MA_EMA_SLOW_PERIOD,
  MA_LINE_COLORS,
  MA_SMA_PERIOD,
  SR_LINE_COLORS,
  SR_SWING_PERIOD,
} from '../utils/movingAverages'
import { xAxisDomainCenterLatest } from '../utils/chartZoom'

/** Recharts margins for OHLC-derived line/bar (matches legacy Kite layout). */
const RECHARTS_MARGINS = { top: 4, right: 8, left: 0, bottom: 0 }

export type InstrumentPriceChartGraphType = 'line' | 'bar' | 'candlestick'

export type InstrumentChartDensity = 'compact' | 'comfortable'

export type InstrumentPriceChartProps = {
  graphType: InstrumentPriceChartGraphType
  data: ChartPointWithMa[]
  maLineVisibility?: MaLineVisibility
  customEmaPeriod?: number | null
  livePrice?: number | null
  paperLastBuyPrice?: number | null
  paperBuyDataIndices?: readonly number[]
  mlPredictionEntries?: readonly MlPredictionLogEntry[]
  /** Recharts Y domain; ignored for candles. */
  rechartsYDomain?: [number | string, number | string] | undefined
  /** Reference lines rendered inside LineChart/ComposedChart (paper buy verticals, LTP, Last buy …). */
  referenceLines?: ReactNode
  /** Compact = favorite tiles; comfortable = Browse detail defaults. */
  density?: InstrumentChartDensity
  showVolume?: boolean
  /** Recharts candles: empty slots past newest when dragging “newer” while zoomed. */
  newerGhostBars?: number
}

/** SMA / EMA / S&R overlays — same styling as CandlestickChart. */
function MovingAverageOverlays({
  visibility,
  customEmaLinePeriod,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
}) {
  return (
    <>
      {visibility.showSma20 ? (
        <Line
          type="monotone"
          dataKey="sma20"
          stroke={MA_LINE_COLORS.sma20}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`SMA ${MA_SMA_PERIOD}`}
        />
      ) : null}
      {visibility.showEma9 ? (
        <Line
          type="monotone"
          dataKey="ema9"
          stroke={MA_LINE_COLORS.ema9}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${MA_EMA_FAST_PERIOD}`}
        />
      ) : null}
      {visibility.showEma21 ? (
        <Line
          type="monotone"
          dataKey="ema21"
          stroke={MA_LINE_COLORS.ema21}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${MA_EMA_SLOW_PERIOD}`}
        />
      ) : null}
      {visibility.showCustomEma && customEmaLinePeriod != null && customEmaLinePeriod >= 2 ? (
        <Line
          type="monotone"
          dataKey="emaCustom"
          stroke={MA_LINE_COLORS.emaCustom}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${customEmaLinePeriod}`}
        />
      ) : null}
      {visibility.showSupportResistance ? (
        <Line
          type="monotone"
          dataKey="srSupport"
          stroke={SR_LINE_COLORS.support}
          dot={false}
          strokeWidth={1.25}
          connectNulls
          strokeDasharray="4 3"
          name={`Support (${SR_SWING_PERIOD})`}
        />
      ) : null}
      {visibility.showSupportResistance ? (
        <Line
          type="monotone"
          dataKey="srResistance"
          stroke={SR_LINE_COLORS.resistance}
          dot={false}
          strokeWidth={1.25}
          connectNulls
          strokeDasharray="4 3"
          name={`Resistance (${SR_SWING_PERIOD})`}
        />
      ) : null}
    </>
  )
}

function InstrumentChartCornerLegend({
  visibility,
  customEmaLinePeriod,
  graphType,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
  graphType: InstrumentPriceChartGraphType
}) {
  const items: { key: string; label: string; color: string }[] = []
  if (graphType === 'candlestick' && visibility.showLinearCloseTrend) {
    items.push({ key: 'tlr', label: 'Trend LR', color: LINEAR_CLOSE_TREND_COLOR })
  }
  if (visibility.showSma20) items.push({ key: 'sma', label: `SMA${MA_SMA_PERIOD}`, color: MA_LINE_COLORS.sma20 })
  if (visibility.showEma9) items.push({ key: 'e9', label: `EMA${MA_EMA_FAST_PERIOD}`, color: MA_LINE_COLORS.ema9 })
  if (visibility.showEma21) items.push({ key: 'e21', label: `EMA${MA_EMA_SLOW_PERIOD}`, color: MA_LINE_COLORS.ema21 })
  if (visibility.showCustomEma && customEmaLinePeriod != null && customEmaLinePeriod >= 2) {
    items.push({
      key: 'ecust',
      label: `EMA${customEmaLinePeriod}`,
      color: MA_LINE_COLORS.emaCustom,
    })
  }
  if (visibility.showSupportResistance) {
    items.push({ key: 'srs', label: `S${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.support })
    items.push({ key: 'srr', label: `R${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.resistance })
  }
  if (items.length === 0) return null
  return (
    <div
      className="position-absolute small text-secondary"
      style={{ right: 8, top: 4, fontSize: '0.65rem', pointerEvents: 'none', zIndex: 2 }}
    >
      {items.map((item, i) => (
        <Fragment key={item.key}>
          {i > 0 ? <span className="text-muted"> · </span> : null}
          <span style={{ color: item.color }}>{item.label}</span>
        </Fragment>
      ))}
    </div>
  )
}

function InstrumentVolumeHistogram({
  chartData,
  compact,
  centeredXDomain,
}: {
  chartData: ChartPointWithMa[]
  compact?: boolean
  centeredXDomain?: [number, number]
}) {
  if (chartData.length === 0) return null

  const paneH = compact ? 46 : 54
  const maxBarSize = compact ? 20 : 34
  const yTickFs = compact ? 8 : 9
  const yAxisWidth = compact ? 32 : 37

  return (
    <div
      style={{ height: paneH }}
      className="flex-shrink-0 border-top border-secondary border-opacity-25"
    >
      <ResponsiveContainer width="100%" height="100%" debounce={50}>
        <BarChart data={chartData} margin={{ ...RECHARTS_MARGINS, top: 3, bottom: 1 }}>
          <XAxis
            dataKey="idx"
            type={centeredXDomain ? 'number' : undefined}
            domain={centeredXDomain ?? ['auto', 'auto']}
            allowDataOverflow={Boolean(centeredXDomain)}
            stroke="#adb5bd"
            hide
          />
          <YAxis stroke="#adb5bd" tick={{ fontSize: yTickFs }} width={yAxisWidth} domain={[0, 'auto']} />
          <Tooltip
            cursor={{ fill: 'rgba(255,255,255,0.04)' }}
            formatter={(value: number) => [
              typeof value === 'number' ? value.toLocaleString() : String(value),
              'Volume',
            ]}
            labelFormatter={() => ''}
            contentStyle={{
              background: '#212529',
              border: '1px solid #495057',
              borderRadius: 6,
              fontSize: 11,
            }}
          />
          <Bar dataKey="volume" maxBarSize={maxBarSize} radius={[2, 2, 0, 0]} isAnimationActive={false}>
            {chartData.map((e) => (
              <Cell
                key={`vol-cell-${e.idx}`}
                fill={e.close >= e.open ? 'rgba(34, 197, 94, 0.62)' : 'rgba(239, 68, 68, 0.62)'}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

function MlTargetBarPredictionLabelList({
  chartData,
  mlEntries,
}: {
  chartData: ChartPointWithMa[]
  mlEntries: readonly MlPredictionLogEntry[]
}) {
  const labelMap = useMemo(() => mapMlPredictionsPerTargetBar(mlEntries, chartData), [mlEntries, chartData])
  const LabelContent = useCallback(
    ({ x, y, index }: { x?: number; y?: number; index?: number }) => {
      if (typeof index !== 'number' || x == null || y == null) return null
      const preds = labelMap.get(index)
      if (!preds?.length) return null
      const iconPx = 7
      const fh = Math.round(iconPx + 5)
      const yTop = y - 10 - fh
      return <MlDirectionRibbonSvg cx={x} yTop={yTop} entries={preds} iconPx={iconPx} />
    },
    [labelMap],
  )

  if (mlEntries.length === 0) return null

  return (
    <LabelList
      dataKey="close"
      position="top"
      offset={14}
      style={{ pointerEvents: 'none' }}
      content={LabelContent}
    />
  )
}

function InstrumentChartTooltipContent({
  active,
  payload,
  maLineVisibility = DEFAULT_MA_LINE_VISIBILITY,
  customEmaLinePeriod = null,
}: {
  active?: boolean
  payload?: readonly { payload?: ChartPointWithMa & { trendLine?: number | null } }[]
  maLineVisibility?: MaLineVisibility
  customEmaLinePeriod?: number | null
}) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload
  if (!p) return null
  return (
    <div
      className="rounded border border-secondary p-2 small"
      style={{ background: '#212529', color: '#f8f9fa' }}
    >
      <div>{formatLocalDateTime(p.t)}</div>
      <div className="font-monospace mt-1">
        O {p.open} · H {p.high} · L {p.low} · C {p.close}
      </div>
      <div className="text-secondary small">
        Vol{' '}
        {Number.isFinite(Number(p.volume)) ? Number(p.volume).toLocaleString() : '—'}
      </div>
      {maLineVisibility.showSma20 && p.sma20 != null ? (
        <div className="font-monospace mt-1" style={{ color: MA_LINE_COLORS.sma20 }}>
          SMA{MA_SMA_PERIOD} {p.sma20.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showEma9 && p.ema9 != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.ema9 }}>
          EMA{MA_EMA_FAST_PERIOD} {p.ema9.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showEma21 && p.ema21 != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.ema21 }}>
          EMA{MA_EMA_SLOW_PERIOD} {p.ema21.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showCustomEma &&
      customEmaLinePeriod != null &&
      customEmaLinePeriod >= 2 &&
      p.emaCustom != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.emaCustom }}>
          EMA{customEmaLinePeriod} {p.emaCustom.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showSupportResistance && p.srSupport != null ? (
        <div className="font-monospace mt-1" style={{ color: SR_LINE_COLORS.support }}>
          Sup{SR_SWING_PERIOD} {p.srSupport.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showSupportResistance && p.srResistance != null ? (
        <div className="font-monospace" style={{ color: SR_LINE_COLORS.resistance }}>
          Res{SR_SWING_PERIOD} {p.srResistance.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showLinearCloseTrend && p.trendLine != null && Number.isFinite(p.trendLine) ? (
        <div className="font-monospace mt-1" style={{ color: LINEAR_CLOSE_TREND_COLOR }}>
          Trend LR {Number(p.trendLine).toFixed(4)}
        </div>
      ) : null}
    </div>
  )
}

/**
 * Single entry point for Zerodha-style OHLC charts: Candlestick SVG, or Recharts line/bar (+ volume strip + ML labels).
 */
export function InstrumentPriceChart({
  graphType,
  data,
  maLineVisibility = DEFAULT_MA_LINE_VISIBILITY,
  customEmaPeriod = null,
  livePrice = null,
  paperLastBuyPrice = null,
  paperBuyDataIndices = [],
  mlPredictionEntries = [],
  rechartsYDomain,
  referenceLines = null,
  density = 'compact',
  showVolume = true,
  newerGhostBars = 0,
}: InstrumentPriceChartProps) {
  const ghost = Math.max(0, Math.floor(newerGhostBars))
  const centeredXDomain = useMemo(() => xAxisDomainCenterLatest(data, ghost), [data, ghost])

  if (graphType === 'candlestick') {
    return (
      <div className="w-100 h-100">
        <CandlestickChart
          data={data}
          maLineVisibility={maLineVisibility}
          customEmaPeriod={customEmaPeriod}
          livePrice={livePrice}
          paperLastBuyPrice={paperLastBuyPrice}
          paperBuyDataIndices={paperBuyDataIndices}
          mlPredictionEntries={mlPredictionEntries}
          newerGhostSlots={ghost}
        />
      </div>
    )
  }

  const xTickFs = density === 'compact' ? 9 : 10
  const yAxisW = density === 'compact' ? 48 : 56
  const barMaxSize = density === 'compact' ? 32 : 48
  const compactVol = density === 'compact'

  const lineChartData =
    maLineVisibility.showLinearCloseTrend
      ? attachLinearTrendToChartPoints(data)
      : data

  return (
    <div className="position-relative w-100 h-100 d-flex flex-column">
      <div className="flex-grow-1 d-flex flex-column" style={{ minHeight: 0 }}>
        <ChartWithRightGutter>
          <div className="d-flex flex-column w-100 h-100" style={{ minHeight: 0 }}>
            <div className="flex-grow-1 w-100" style={{ minHeight: 0 }}>
              <ResponsiveContainer width="100%" height="100%">
                {graphType === 'line' ? (
                  <LineChart data={lineChartData} margin={RECHARTS_MARGINS}>
                    <XAxis
                      dataKey="idx"
                      type="number"
                      domain={centeredXDomain ?? ['dataMin', 'dataMax']}
                      allowDataOverflow
                      stroke="#adb5bd"
                      tick={{ fontSize: xTickFs }}
                      hide
                    />
                    <YAxis
                      stroke="#adb5bd"
                      tick={{ fontSize: xTickFs + 1 }}
                      domain={rechartsYDomain ?? ['auto', 'auto']}
                      width={yAxisW}
                    />
                    <Tooltip
                      content={(props) => (
                        <InstrumentChartTooltipContent
                          active={props.active}
                          payload={
                            props.payload as readonly {
                              payload?: ChartPointWithMa & { trendLine?: number | null }
                            }[] | undefined
                          }
                          maLineVisibility={maLineVisibility}
                          customEmaLinePeriod={customEmaPeriod}
                        />
                      )}
                    />
                    <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} name="Close">
                      <MlTargetBarPredictionLabelList chartData={data} mlEntries={mlPredictionEntries} />
                    </Line>
                    <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaPeriod} />
                    {referenceLines}
                  </LineChart>
                ) : (
                  <ComposedChart data={data} margin={RECHARTS_MARGINS}>
                    <XAxis
                      dataKey="idx"
                      type="number"
                      domain={centeredXDomain ?? ['dataMin', 'dataMax']}
                      allowDataOverflow
                      stroke="#adb5bd"
                      tick={{ fontSize: xTickFs }}
                      hide
                    />
                    <YAxis
                      stroke="#adb5bd"
                      tick={{ fontSize: xTickFs + 1 }}
                      domain={rechartsYDomain ?? ['auto', 'auto']}
                      width={yAxisW}
                    />
                    <Tooltip
                      content={(props) => (
                        <InstrumentChartTooltipContent
                          active={props.active}
                          payload={props.payload as readonly { payload?: ChartPointWithMa }[] | undefined}
                          maLineVisibility={maLineVisibility}
                          customEmaLinePeriod={customEmaPeriod}
                        />
                      )}
                    />
                    <Bar dataKey="close" fill="#0d6efd" maxBarSize={barMaxSize} radius={[2, 2, 0, 0]} name="Close">
                      <MlTargetBarPredictionLabelList chartData={data} mlEntries={mlPredictionEntries} />
                    </Bar>
                    <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaPeriod} />
                    {referenceLines}
                  </ComposedChart>
                )}
              </ResponsiveContainer>
            </div>
            {showVolume ? <InstrumentVolumeHistogram chartData={data} compact={compactVol} centeredXDomain={centeredXDomain} /> : null}
          </div>
        </ChartWithRightGutter>
      </div>
      <InstrumentChartCornerLegend
        visibility={maLineVisibility}
        customEmaLinePeriod={customEmaPeriod}
        graphType={graphType}
      />
    </div>
  )
}
