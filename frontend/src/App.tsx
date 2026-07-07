import { MapContainer, TileLayer } from 'react-leaflet'
import './App.css'

export default function App() {
  return (
    <MapContainer center={[59.91, 10.75]} zoom={6} className="map">
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
    </MapContainer>
  )
}
