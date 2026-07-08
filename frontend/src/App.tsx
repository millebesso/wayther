import { useEffect, useMemo, useState } from 'react'
import { MapContainer, Marker, Polyline, TileLayer, useMapEvents } from 'react-leaflet'
import L, { type LatLngExpression } from 'leaflet'
import './App.css'

type Point = { lat: number; lon: number }
type Sample = { lat: number; lon: number; time: string }

const originIcon = makePinIcon('A', '#1a7f37')
const destinationIcon = makePinIcon('B', '#cf222e')

const INTERVAL_OPTIONS = [30, 60] as const
type Interval = (typeof INTERVAL_OPTIONS)[number]

export default function App() {
  const [origin, setOrigin] = useState<Point | null>(null)
  const [destination, setDestination] = useState<Point | null>(null)
  const [route, setRoute] = useState<LatLngExpression[]>([])
  const [samples, setSamples] = useState<Sample[]>([])

  // Departure options are fixed 30-minute slots across the next ~48 hours — the
  // window met.no forecasts hourly. Computed once on mount.
  const departureSlots = useMemo(departureSlotOptions, [])
  const [departure, setDeparture] = useState(() => departureSlots[0].value)
  const [sampleInterval, setSampleInterval] = useState<Interval>(60)

  // Whenever the inputs are complete, resolve the route and its timed samples.
  useEffect(() => {
    if (!origin || !destination) {
      setRoute([])
      setSamples([])
      return
    }

    const controller = new AbortController()
    fetchRouteForecast({ origin, destination, departure, interval: sampleInterval }, controller.signal)
      .then((result) => {
        setRoute(result.geometry.map((p) => [p.lat, p.lon]))
        setSamples(result.samples)
      })
      .catch((error) => {
        if (error.name !== 'AbortError') console.error('Route forecast request failed', error)
      })
    return () => controller.abort()
  }, [origin, destination, departure, sampleInterval])

  function handleMapClick(point: Point) {
    if (!origin) setOrigin(point)
    else if (!destination) setDestination(point)
    // Both placed: move whichever pin is nearer the click, so a mistaken point
    // is corrected in place without resetting the other.
    else if (isNearer(point, origin, destination)) setOrigin(point)
    else setDestination(point)
  }

  return (
    <div className="app">
      <div className="controls">
        <label className="control">
          <span>Departure</span>
          <select value={departure} onChange={(e) => setDeparture(e.target.value)}>
            {departureSlots.map((slot) => (
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
      </div>

      <MapContainer center={[59.91, 10.75]} zoom={6} className="map">
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        <ClickHandler onClick={handleMapClick} />
        <DraggablePin point={origin} icon={originIcon} onMove={setOrigin} />
        <DraggablePin point={destination} icon={destinationIcon} onMove={setDestination} />
        {route.length > 0 && <Polyline positions={route} color="#0969da" weight={5} />}
        {samples.map((sample, i) => (
          <Marker key={i} position={[sample.lat, sample.lon]} icon={etaIcon(sample.time)} />
        ))}
      </MapContainer>
      <p className="hint">{hintFor(origin, destination)}</p>
    </div>
  )
}

function ClickHandler({ onClick }: { onClick: (point: Point) => void }) {
  useMapEvents({ click: (e) => onClick(toPoint(e.latlng)) })
  return null
}

function DraggablePin({
  point,
  icon,
  onMove,
}: {
  point: Point | null
  icon: L.DivIcon
  onMove: (point: Point) => void
}) {
  if (!point) return null
  return (
    <Marker
      position={[point.lat, point.lon]}
      icon={icon}
      draggable
      eventHandlers={{ dragend: (e) => onMove(toPoint(e.target.getLatLng())) }}
    />
  )
}

function hintFor(origin: Point | null, destination: Point | null): string {
  if (!origin) return 'Click the map to set the start point.'
  if (!destination) return 'Click the map to set the destination.'
  return 'Click near a pin (or drag it) to move it.'
}

type RouteForecastResult = { geometry: Point[]; samples: Sample[] }

async function fetchRouteForecast(
  input: { origin: Point; destination: Point; departure: string; interval: Interval },
  signal: AbortSignal,
): Promise<RouteForecastResult> {
  const response = await fetch('/api/route-forecast', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      origin: input.origin,
      destination: input.destination,
      departureTime: input.departure,
      intervalMinutes: input.interval,
    }),
    signal,
  })
  if (!response.ok) throw new Error(`route-forecast returned ${response.status}`)
  return response.json()
}

/** 30-minute departure slots from the next slot through the next ~48 hours. */
function departureSlotOptions(): { value: string; label: string }[] {
  const slots: { value: string; label: string }[] = []
  const start = nextHalfHour(new Date())
  const formatter = new Intl.DateTimeFormat(undefined, {
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
  })
  for (let i = 0; i < 48 * 2; i++) {
    const slot = new Date(start.getTime() + i * 30 * 60 * 1000)
    slots.push({ value: slot.toISOString(), label: formatter.format(slot) })
  }
  return slots
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

/** True when `point` is closer to `a` than to `b` (squared degree distance is enough to compare). */
function isNearer(point: Point, a: Point, b: Point): boolean {
  return squaredDistance(point, a) <= squaredDistance(point, b)
}

function squaredDistance(p: Point, q: Point): number {
  const dLat = p.lat - q.lat
  const dLon = p.lon - q.lon
  return dLat * dLat + dLon * dLon
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

/** A marker labelled with the estimated time of arrival at a sampled position. */
function etaIcon(time: string): L.DivIcon {
  const label = new Date(time).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  return L.divIcon({
    className: 'eta',
    html: `<span class="eta-dot"></span><span class="eta-label">${label}</span>`,
    iconSize: [12, 12],
    iconAnchor: [6, 6],
  })
}
