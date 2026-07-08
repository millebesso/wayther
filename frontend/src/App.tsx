import { useEffect, useState } from 'react'
import { MapContainer, Marker, Polyline, TileLayer, useMapEvents } from 'react-leaflet'
import L, { type LatLngExpression } from 'leaflet'
import './App.css'

type Point = { lat: number; lon: number }

const originIcon = makePinIcon('A', '#1a7f37')
const destinationIcon = makePinIcon('B', '#cf222e')

export default function App() {
  const [origin, setOrigin] = useState<Point | null>(null)
  const [destination, setDestination] = useState<Point | null>(null)
  const [route, setRoute] = useState<LatLngExpression[]>([])

  // Whenever both pins are placed, resolve the driving route and draw it.
  useEffect(() => {
    if (!origin || !destination) {
      setRoute([])
      return
    }

    const controller = new AbortController()
    fetchRoute(origin, destination, controller.signal)
      .then((geometry) => setRoute(geometry.map((p) => [p.lat, p.lon])))
      .catch((error) => {
        if (error.name !== 'AbortError') console.error('Route request failed', error)
      })
    return () => controller.abort()
  }, [origin, destination])

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
      <MapContainer center={[59.91, 10.75]} zoom={6} className="map">
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        <ClickHandler onClick={handleMapClick} />
        <DraggablePin point={origin} icon={originIcon} onMove={setOrigin} />
        <DraggablePin point={destination} icon={destinationIcon} onMove={setDestination} />
        {route.length > 0 && <Polyline positions={route} color="#0969da" weight={5} />}
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

async function fetchRoute(origin: Point, destination: Point, signal: AbortSignal): Promise<Point[]> {
  const response = await fetch('/api/route-forecast', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ origin, destination }),
    signal,
  })
  if (!response.ok) throw new Error(`route-forecast returned ${response.status}`)
  const data: { geometry: Point[] } = await response.json()
  return data.geometry
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
