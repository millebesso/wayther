import { useEffect, useMemo, useRef, useState } from 'react'
import { MapContainer, Marker, Polyline, Popup, TileLayer, useMapEvents } from 'react-leaflet'
import L, { type LatLngExpression } from 'leaflet'
import './App.css'

type Point = { lat: number; lon: number }
type Forecast = { symbolCode: string; temperatureCelsius: number; precipitationMm: number }
type Sample = { lat: number; lon: number; time: string; forecast: Forecast }

// The backend's 422 discriminator, a share-load failure, plus a catch-all for
// anything else that fails.
type ForecastError = 'route_not_found' | 'forecast_unavailable' | 'share_not_found' | 'generic'

/** User-facing copy for each failure, shown in the timeline pane. */
const ERROR_MESSAGES: Record<ForecastError, string> = {
  route_not_found: 'No route found. Try placing your points on or near a road — not in water or off-grid.',
  forecast_unavailable: 'No forecast reaches that far ahead yet. Try an earlier departure time.',
  share_not_found: 'This shared link is invalid or has expired. Click the map to start a new route.',
  generic: 'Something went wrong fetching your forecast. Please try again.',
}

/** Carries which failure occurred so the effect can pick the right message. */
class RouteForecastRequestError extends Error {
  readonly code: ForecastError
  constructor(code: ForecastError) {
    super(code)
    this.name = 'RouteForecastRequestError'
    this.code = code
  }
}

const originColor = '#1a7f37'
const destinationColor = '#cf222e'
const stopColor = '#57606a'

// A route needs a start and an end; the cap matches the backend's MaxWaypoints and
// keeps the ORS request and the number of forecast lookups bounded.
const MAX_WAYPOINTS = 10

const INTERVAL_OPTIONS = [30, 60] as const
type Interval = (typeof INTERVAL_OPTIONS)[number]

export default function App() {
  // The ordered list of stops: the first is the start, the last the destination,
  // any between are intermediate waypoints. Clicking the map appends to the end.
  const [waypoints, setWaypoints] = useState<Point[]>([])
  const [route, setRoute] = useState<LatLngExpression[]>([])
  const [samples, setSamples] = useState<Sample[]>([])
  const [error, setError] = useState<ForecastError | null>(null)

  // Departure is chosen as a day and a time of day. Days run from today through a
  // week ahead; times are 30-minute slots, and for today the past slots are hidden.
  const dayOptions = useMemo(departureDayOptions, [])
  const [departureDay, setDepartureDay] = useState(() => dayOptions[0].value)
  const timeOptions = useMemo(() => timeSlotsForDay(departureDay), [departureDay])
  const [departureMinutes, setDepartureMinutes] = useState(() => timeOptions[0]?.value ?? 0)
  const departure = useMemo(() => combineDayAndTime(departureDay, departureMinutes), [departureDay, departureMinutes])
  const [sampleInterval, setSampleInterval] = useState<Interval>(60)

  // The Share button's transient status: sharing while the POST is in flight, then
  // copied/error feedback that reverts to idle after a moment.
  const [shareState, setShareState] = useState<ShareState>('idle')

  // The Leaflet map instance, captured once it's ready, so the share-open flow can
  // frame the route for a recipient who didn't build it.
  const mapRef = useRef<L.Map | null>(null)
  // Set true only when a /m/{id} share hydrates; consumed once by the next route
  // resolution to frame the trip, then cleared. Author-driven edits never set it, so
  // clicking or dragging pins never moves the map under the user.
  const pendingFitRef = useRef(false)

  // If the page was opened via a /m/{id} share link, load the stored route once on
  // mount and hydrate the editor with it. A successful load becomes the recipient's
  // own working session, so we drop the /m/{id} from the address bar; a failed load
  // leaves it so a refresh retries, and surfaces the reason in the timeline pane.
  useEffect(() => {
    const id = shareIdFromPath(window.location.pathname)
    if (!id) return

    const controller = new AbortController()
    fetchShare(id, controller.signal)
      .then((share) => {
        const departAt = new Date(share.departureTime)
        setWaypoints(share.waypoints.map((p) => ({ lat: p.lat, lon: p.lon })))
        setDepartureDay(localDateKey(departAt))
        setDepartureMinutes(minutesOfDay(departAt))
        setSampleInterval(share.intervalMinutes)
        // Frame the trip on the next route resolution: the map starts on its default
        // view, and a recipient who didn't build the route would otherwise not see it.
        pendingFitRef.current = true
        window.history.replaceState(null, '', '/')
      })
      .catch((err) => {
        if (err.name === 'AbortError') return
        setError(err instanceof RouteForecastRequestError ? err.code : 'generic')
      })
    return () => controller.abort()
  }, [])

  // Changing the day changes which times are offered (today drops past slots), so
  // snap the selected time to the first available slot when it's no longer listed.
  useEffect(() => {
    if (!timeOptions.some((slot) => slot.value === departureMinutes)) {
      setDepartureMinutes(timeOptions[0]?.value ?? 0)
    }
  }, [timeOptions, departureMinutes])

  // Once there's a start and an end, resolve the route and its timed samples.
  useEffect(() => {
    if (waypoints.length < 2) {
      setRoute([])
      setSamples([])
      setError(null)
      return
    }

    const controller = new AbortController()
    fetchRouteForecast({ waypoints, departure, interval: sampleInterval }, controller.signal)
      .then((result) => {
        setRoute(result.geometry.map((p) => [p.lat, p.lon]))
        setSamples(result.samples)
        setError(null)
        // A share just opened: frame the true road the recipient will trace.
        consumePendingFit(result.geometry)
      })
      .catch((err) => {
        if (err.name === 'AbortError') return
        // Clear any stale route/samples so the map never shows a prior success,
        // then surface the failure (pins are left in place for the user to adjust).
        setRoute([])
        setSamples([])
        setError(err instanceof RouteForecastRequestError ? err.code : 'generic')
        // No polyline to frame, but a share still deserves orienting: fall back to the
        // waypoints so the recipient sees where the trip is despite the failure.
        consumePendingFit(waypoints)
      })
    return () => controller.abort()
  }, [waypoints, departure, sampleInterval])

  // The copied/error feedback on the Share button is momentary — revert it to idle
  // shortly after it appears so the button returns to its normal "Share route" label.
  useEffect(() => {
    if (shareState !== 'copied' && shareState !== 'error') return
    const timer = setTimeout(() => setShareState('idle'), 2500)
    return () => clearTimeout(timer)
  }, [shareState])

  // If a share is waiting to be framed, fit the map to `points` (the resolved route,
  // or the waypoints when the forecast failed) and clear the flag so it fires once.
  // A maxZoom cap keeps a short route at a town-level view instead of rooftop zoom.
  function consumePendingFit(points: Point[]) {
    if (!pendingFitRef.current) return
    pendingFitRef.current = false
    const map = mapRef.current
    if (!map || points.length === 0) return
    map.fitBounds(
      L.latLngBounds(points.map((p) => [p.lat, p.lon] as [number, number])),
      { padding: [48, 48], maxZoom: 13 },
    )
  }

  // A click adds a new stop at the end of the route; once the cap is reached the
  // gesture goes inert (the hint tells the user why).
  function handleMapClick(point: Point) {
    setWaypoints((current) => (current.length >= MAX_WAYPOINTS ? current : [...current, point]))
  }

  function handleMoveWaypoint(index: number, point: Point) {
    setWaypoints((current) => current.map((wp, i) => (i === index ? point : wp)))
  }

  // Remove a single stop, but never drop below a start and an end.
  function handleRemoveWaypoint(index: number) {
    setWaypoints((current) => (current.length <= 2 ? current : current.filter((_, i) => i !== index)))
  }

  // Drop all pins and any resolved route/forecast; the effect clears route and
  // samples once fewer than two remain, so this just empties the list.
  function handleClear() {
    setWaypoints([])
  }

  // Mint a share for the route currently on screen and copy its /m/{id} link to the
  // clipboard. Create-on-demand: a row is stored only when the user actually shares.
  async function handleShare() {
    setShareState('sharing')
    try {
      const id = await createShare({ waypoints, departure, interval: sampleInterval })
      await navigator.clipboard.writeText(`${window.location.origin}/m/${id}`)
      setShareState('copied')
    } catch {
      setShareState('error')
    }
  }

  return (
    <div className="app">
      <div className="map-pane">
        <MapContainer center={[59.91, 10.75]} zoom={6} className="map" ref={mapRef}>
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <ClickHandler onClick={handleMapClick} />
          {waypoints.map((point, i) => (
            <WaypointPin
              key={i}
              point={point}
              icon={waypointIcon(i, waypoints.length)}
              canRemove={waypoints.length > 2}
              onMove={(moved) => handleMoveWaypoint(i, moved)}
              onRemove={() => handleRemoveWaypoint(i)}
            />
          ))}
          {route.length > 0 && <Polyline positions={route} color="#0969da" weight={5} />}
          {samples.map((sample, i) => (
            <Marker key={i} position={[sample.lat, sample.lon]} icon={weatherIcon(sample.forecast)}>
              <Popup>
                <strong>{formatTime(sample.time)}</strong>
                <br />
                {weatherEmoji(sample.forecast.symbolCode)} {formatTemperature(sample.forecast.temperatureCelsius)}
                <br />
                {formatPrecipitation(sample.forecast.precipitationMm)}
              </Popup>
            </Marker>
          ))}
        </MapContainer>
        <p className="hint">{hintFor(waypoints)}</p>
      </div>

      <aside className="timeline">
        <h1>Weather along the way</h1>
        <div className="controls">
          <label className="control">
            <span>Day</span>
            <select value={departureDay} onChange={(e) => setDepartureDay(e.target.value)}>
              {dayOptions.map((day) => (
                <option key={day.value} value={day.value}>
                  {day.label}
                </option>
              ))}
            </select>
          </label>
          <label className="control">
            <span>Departure time</span>
            <select value={departureMinutes} onChange={(e) => setDepartureMinutes(Number(e.target.value))}>
              {timeOptions.map((slot) => (
                <option key={slot.value} value={slot.value}>
                  {slot.label}
                </option>
              ))}
            </select>
          </label>
          <label className="control">
            <span>Sample every</span>
            <select value={sampleInterval} onChange={(e) => setSampleInterval(Number(e.target.value) as Interval)}>
              {INTERVAL_OPTIONS.map((minutes) => (
                <option key={minutes} value={minutes}>
                  {minutes} min
                </option>
              ))}
            </select>
          </label>
          <div className="control-actions">
            <button
              type="button"
              className="share-button"
              onClick={handleShare}
              disabled={samples.length === 0 || error !== null || shareState === 'sharing'}
            >
              {shareLabel(shareState)}
            </button>
            <button type="button" className="clear-button" onClick={handleClear} disabled={waypoints.length === 0}>
              Clear route
            </button>
          </div>
        </div>
        {error ? (
          <p className="timeline-error">{ERROR_MESSAGES[error]}</p>
        ) : samples.length === 0 ? (
          <p className="timeline-empty">Pick a start and destination to see the forecast along your route.</p>
        ) : (
          <ol className="timeline-list">
            {samples.map((sample, i) => (
              <li key={i} className="timeline-row">
                <span className="timeline-time">{formatTime(sample.time)}</span>
                <span className="timeline-symbol">{weatherEmoji(sample.forecast.symbolCode)}</span>
                <span className="timeline-temp">{formatTemperature(sample.forecast.temperatureCelsius)}</span>
                <span className={`timeline-rain${isRaining(sample.forecast) ? ' is-raining' : ''}`}>
                  {isRaining(sample.forecast) ? `🌧 ${formatMm(sample.forecast.precipitationMm)}` : 'Dry'}
                </span>
              </li>
            ))}
          </ol>
        )}
        <footer className="attribution">
          Weather data from{' '}
          <a href="https://www.met.no/en" target="_blank" rel="noreferrer">
            MET Norway
          </a>{' '}
          (CC BY 4.0)
        </footer>
      </aside>
    </div>
  )
}

function ClickHandler({ onClick }: { onClick: (point: Point) => void }) {
  useMapEvents({
    click: (e) => {
      // A click inside an open popup (e.g. a pin's "Remove stop" button) bubbles up
      // to the map as a click too. Ignore those, so managing a pin never also drops
      // a stray waypoint at the popup's location.
      if ((e.originalEvent.target as HTMLElement).closest('.leaflet-popup')) return
      onClick(toPoint(e.latlng))
    },
  })
  return null
}

function WaypointPin({
  point,
  icon,
  canRemove,
  onMove,
  onRemove,
}: {
  point: Point
  icon: L.DivIcon
  canRemove: boolean
  onMove: (point: Point) => void
  onRemove: () => void
}) {
  return (
    <Marker
      position={[point.lat, point.lon]}
      icon={icon}
      draggable
      eventHandlers={{ dragend: (e) => onMove(toPoint(e.target.getLatLng())) }}
    >
      <Popup>
        {canRemove ? (
          <RemoveStopButton onRemove={onRemove} />
        ) : (
          // A route needs a start and an end, so the last two can't be removed.
          <span className="remove-waypoint-hint">A route needs a start and an end.</span>
        )}
      </Popup>
    </Marker>
  )
}

/**
 * The "Remove stop" action inside a pin's popup. Removing a stop unmounts this marker
 * and its popup, so the click is handled by a native listener that stops propagation
 * *before* it can bubble to the Leaflet map — whose click handler would otherwise fire
 * (asynchronously, after the popup is gone) and drop a stray waypoint. React's own
 * onClick is delegated above the map, so it can't both remove the stop and stop the map.
 */
function RemoveStopButton({ onRemove }: { onRemove: () => void }) {
  const ref = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const button = ref.current
    if (!button) return
    const handle = (e: MouseEvent) => {
      e.stopPropagation()
      onRemove()
    }
    button.addEventListener('click', handle)
    return () => button.removeEventListener('click', handle)
  }, [onRemove])
  return (
    <button type="button" ref={ref} className="remove-waypoint">
      Remove stop
    </button>
  )
}

function hintFor(waypoints: Point[]): string {
  if (waypoints.length === 0) return 'Click the map to set the start point.'
  if (waypoints.length === 1) return 'Click the map to set the destination.'
  if (waypoints.length >= MAX_WAYPOINTS) return `Maximum of ${MAX_WAYPOINTS} waypoints reached.`
  return 'Click to add another stop, or drag a pin to move it.'
}

/**
 * The pin style for a waypoint at <code>index</code> of <code>total</code>: a green
 * "A" for the start, a red "B" for the destination, and neutral dots numbered in
 * visiting order for the stops between. Colours follow position, not identity, so
 * adding a stop re-labels the old endpoint.
 */
function waypointIcon(index: number, total: number): L.DivIcon {
  if (index === 0) return makePinIcon('A', originColor)
  if (index === total - 1) return makePinIcon('B', destinationColor)
  return makePinIcon(String(index), stopColor)
}

// The Share button walks idle → sharing (POST in flight) → copied/error, then back
// to idle once the transient feedback times out.
type ShareState = 'idle' | 'sharing' | 'copied' | 'error'

/** The stored inputs a /m/{id} share returns, enough to reopen and re-forecast the trip. */
type ShareResponse = { waypoints: Point[]; departureTime: string; intervalMinutes: Interval }

function shareLabel(state: ShareState): string {
  switch (state) {
    case 'sharing':
      return 'Copying…'
    case 'copied':
      return 'Link copied!'
    case 'error':
      return 'Copy failed'
    default:
      return 'Share route'
  }
}

/** The slug of a `/m/{id}` share link, or null for any other path. */
function shareIdFromPath(pathname: string): string | null {
  const match = pathname.match(/^\/m\/([A-Za-z0-9]+)\/?$/)
  return match ? match[1] : null
}

/**
 * Loads a stored share by slug. A 404 (unknown or expired slug) becomes a
 * `share_not_found` error; any other non-OK response is a generic failure.
 */
async function fetchShare(id: string, signal: AbortSignal): Promise<ShareResponse> {
  const response = await fetch(`/api/shares/${id}`, { signal })
  if (response.status === 404) throw new RouteForecastRequestError('share_not_found')
  if (!response.ok) throw new RouteForecastRequestError('generic')
  return response.json()
}

/** Persists the current route as a share and returns its slug. */
async function createShare(input: { waypoints: Point[]; departure: string; interval: Interval }): Promise<string> {
  const response = await fetch('/api/shares', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      waypoints: input.waypoints,
      departureTime: input.departure,
      intervalMinutes: input.interval,
    }),
  })
  if (!response.ok) throw new Error('Failed to create share')
  const body: { id: string } = await response.json()
  return body.id
}

type RouteForecastResult = { geometry: Point[]; samples: Sample[] }

async function fetchRouteForecast(
  input: { waypoints: Point[]; departure: string; interval: Interval },
  signal: AbortSignal,
): Promise<RouteForecastResult> {
  const response = await fetch('/api/route-forecast', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      waypoints: input.waypoints,
      departureTime: input.departure,
      intervalMinutes: input.interval,
    }),
    signal,
  })
  if (!response.ok) throw new RouteForecastRequestError(await errorCode(response))
  return response.json()
}

/**
 * Reads the failure kind from a non-OK response. A 422 carries the backend's
 * `{ error }` discriminator; anything else (500, and a network error never gets
 * here) is treated as a generic failure.
 */
async function errorCode(response: Response): Promise<ForecastError> {
  try {
    const body = await response.json()
    if (body?.error === 'route_not_found' || body?.error === 'forecast_unavailable') return body.error
  } catch {
    // Non-JSON or empty body — fall through to the generic message.
  }
  return 'generic'
}

/**
 * Today through three days ahead, keyed by local date (`YYYY-MM-DD`). Capped to
 * met.no's hourly window: past ~3 days there's no hourly forecast to attach, so
 * offering those days would only lead into the "no forecast" error.
 */
function departureDayOptions(): { value: string; label: string }[] {
  const formatter = new Intl.DateTimeFormat(undefined, { weekday: 'long', day: 'numeric', month: 'short' })
  const today = new Date()
  const days: { value: string; label: string }[] = []
  for (let i = 0; i < 4; i++) {
    const day = new Date(today.getFullYear(), today.getMonth(), today.getDate() + i)
    const label = i === 0 ? 'Today' : i === 1 ? 'Tomorrow' : formatter.format(day)
    days.push({ value: localDateKey(day), label })
  }
  return days
}

/**
 * 30-minute time-of-day slots (value = minutes since midnight). For today the
 * slots already in the past are dropped so the earliest choice is the next one.
 */
function timeSlotsForDay(dayKey: string): { value: number; label: string }[] {
  const next = nextHalfHour(new Date())
  // If "today" has rolled past its last slot, the next one lands tomorrow.
  const earliest = dayKey === localDateKey(new Date()) && localDateKey(next) === dayKey ? minutesOfDay(next) : 0
  const slots: { value: number; label: string }[] = []
  for (let minutes = earliest; minutes < 24 * 60; minutes += 30) {
    slots.push({ value: minutes, label: formatMinutes(minutes) })
  }
  return slots
}

/** Combine a day key and minutes-since-midnight into an absolute ISO timestamp. */
function combineDayAndTime(dayKey: string, minutes: number): string {
  const [year, month, day] = dayKey.split('-').map(Number)
  return new Date(year, month - 1, day, 0, minutes).toISOString()
}

/** Local calendar date as `YYYY-MM-DD` (avoids UTC shifting the day). */
function localDateKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

function minutesOfDay(date: Date): number {
  return date.getHours() * 60 + date.getMinutes()
}

function formatMinutes(minutes: number): string {
  const hour = String(Math.floor(minutes / 60)).padStart(2, '0')
  const minute = String(minutes % 60).padStart(2, '0')
  return `${hour}:${minute}`
}

/** The next :00 or :30 boundary at or after `from`. */
function nextHalfHour(from: Date): Date {
  const slot = new Date(from)
  slot.setSeconds(0, 0)
  const remainder = slot.getMinutes() % 30
  const bump = remainder === 0 ? (slot.getTime() >= from.getTime() ? 0 : 30) : 30 - remainder
  slot.setMinutes(slot.getMinutes() + bump)
  return slot
}

function toPoint(latlng: L.LatLng): Point {
  return { lat: latlng.lat, lon: latlng.lng }
}

function makePinIcon(label: string, color: string): L.DivIcon {
  return L.divIcon({
    className: 'pin',
    html: `<span class="pin-badge" style="background:${color}">${label}</span>`,
    iconSize: [26, 26],
    iconAnchor: [13, 13],
  })
}

/** A marker showing the sample's weather symbol and temperature at a glance. */
function weatherIcon(forecast: Forecast): L.DivIcon {
  const emoji = weatherEmoji(forecast.symbolCode)
  const temp = formatTemperature(forecast.temperatureCelsius)
  return L.divIcon({
    className: 'weather',
    html: `<span class="weather-symbol">${emoji}</span><span class="weather-temp">${temp}</span>`,
    iconSize: [34, 34],
    iconAnchor: [17, 17],
  })
}

/** True when the forecast expects any precipitation over the hour. */
function isRaining(forecast: Forecast): boolean {
  return forecast.precipitationMm > 0
}

function formatTime(time: string): string {
  return new Date(time).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
}

function formatTemperature(celsius: number): string {
  // met.no gives a temperature for every hourly step, but guard the missing case
  // (NaN from the API) so a marker never renders "NaN°".
  return Number.isFinite(celsius) ? `${Math.round(celsius)}°` : '–'
}

function formatMm(mm: number): string {
  return `${mm.toFixed(1)} mm`
}

function formatPrecipitation(mm: number): string {
  return mm > 0 ? `Precipitation: ${formatMm(mm)}` : 'No precipitation'
}

/**
 * Maps a MET Norway symbol code (e.g. `lightrainshowers_day`) to an emoji. The
 * day/night/polartwilight suffix is irrelevant here, so we match on the substrings
 * that carry the condition, most specific first.
 */
function weatherEmoji(symbolCode: string): string {
  const code = symbolCode.toLowerCase()
  if (code.includes('thunder')) return '⛈️'
  if (code.includes('snow')) return '❄️'
  if (code.includes('sleet')) return '🌨️'
  if (code.includes('rainshowers')) return '🌦️'
  if (code.includes('rain')) return '🌧️'
  if (code.includes('fog')) return '🌫️'
  if (code.includes('cloudy')) return code.includes('partly') ? '⛅' : '☁️'
  if (code.includes('fair')) return '🌤️'
  if (code.includes('clearsky')) return '☀️'
  return '🌡️'
}
