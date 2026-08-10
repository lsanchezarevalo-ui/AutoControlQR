import React, { useState } from 'react'
import axios from 'axios'
import { useSearchParams } from 'react-router-dom'

export default function Scan(){
  const [searchParams] = useSearchParams()
  const vehicleId = searchParams.get('vehicle_id') || ''
  const [odometer, setOdometer] = useState('')
  const [message, setMessage] = useState(null)

  const submit = async (e)=>{
    e.preventDefault()
    try{
      const km = parseInt(odometer,10)
      const res = await axios.post('/api/readings', { vehicleId, odometerKm: km })
      setMessage('Lectura registrada')
    }catch(err){
      setMessage(err.response?.data?.error || 'Error')
    }
  }

  return (
    <div style={{padding:20}}>
      <h2>Actualizar kilometraje</h2>
      <p>Vehículo: <strong>{vehicleId || 'no identificado'}</strong></p>
      <form onSubmit={submit}>
        <div>
          <label>Kilometraje</label><br/>
          <input type="number" value={odometer} onChange={e=>setOdometer(e.target.value)} required />
        </div>
        <button type="submit">Enviar</button>
      </form>
      {message && <p>{message}</p>}
    </div>
  )
}
