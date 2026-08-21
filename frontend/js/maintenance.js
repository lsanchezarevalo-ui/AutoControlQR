async function maintenanceView(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/maintenance-status`);if(!out.r||!out.r.ok)return alert('No se pudo cargar el estado.');const d=out.j.data,user=JSON.parse(localStorage.getItem('user')||'{}'),isAdmin=user.role==='COMPANY_ADMIN';
 if(d.overallStatus==='NO_PLAN' && !isAdmin){document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><h1>${esc(d.plate)}</h1><p><b>${fmt(d.currentMileage)} km</b></p><div class="no-search-results"><b>Sin servicios configurados</b><small>El administrador todavía no ha creado servicios para este vehículo.</small></div>`);document.getElementById('back').onclick=()=>adminShell('vehicles');return}
 const canRemove=isAdmin && (d.individualServices||d.overallStatus==='NO_PLAN');
 const rows=d.services.map(s=>`<article class="statusrow"><div><strong class="service-name-icon">${serviceNameWithIcon(s.name)}</strong>${statusBadge(s.status)}<small>${s.lastServiceMileage!=null?`Último: ${fmt(s.lastServiceMileage)} km`:''}${s.nextDueMileage!=null?`${s.lastServiceMileage!=null?' · ':''}Próximo: ${fmt(s.nextDueMileage)} km`:''}${s.remainingKm!=null?` · ${s.remainingKm>0?'Faltan':'Vencido'} ${fmt(Math.abs(s.remainingKm))} km`:''}</small></div><div class="rowactions">${canRemove?`<button class="textbtn dangertext removeservicebtn" data-service="${s.serviceId}" data-name="${esc(s.name)}">Quitar</button>`:''}${s.status==='NO_BASELINE' && (JSON.parse(localStorage.getItem('user')||'{}').role==='COMPANY_ADMIN')?`<button class="baselinebtn secondary" data-service="${s.serviceId}">Cargar último servicio</button>`:''}<button class="registerbtn" data-service="${s.serviceId}" data-name="${esc(s.name)}" data-status="${s.status}">Registrar mantenimiento</button></div></article>`).join('');
 document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><div class="maintenance-page-head"><div><h1>${esc(d.plate)}</h1><p><b>${fmt(d.currentMileage)} km</b> ${d.overallStatus==='NO_PLAN'?'<span class="badge mutedb">Servicios individuales</span>':statusBadge(d.overallStatus)} ${d.hasIncompleteHistory?'<span class="badge mutedb">⚫ Historial incompleto</span>':''}</p></div>${isAdmin && (d.individualServices||d.overallStatus==='NO_PLAN')?'<button id="addVehicleService">+ Agregar servicio</button>':''}</div><h2>Estado de mantenimiento</h2>${rows||'<div class="no-search-results"><b>Aún no hay servicios</b><small>Agrega solamente los mantenimientos que deseas controlar para este vehículo.</small></div>'}`);
 document.getElementById('back').onclick=()=>adminShell('vehicles');const addVehicleServiceBtn=document.getElementById('addVehicleService');if(addVehicleServiceBtn)addVehicleServiceBtn.onclick=()=>addIndividualVehicleService(vehicleId);document.querySelectorAll('.baselinebtn').forEach(b=>b.onclick=()=>loadBaseline(vehicleId,b.dataset.service));document.querySelectorAll('.registerbtn').forEach(b=>b.onclick=()=>{if(b.dataset.status==='UP_TO_DATE'&&!confirm(`Este servicio está AL DÍA y todavía no corresponde por kilometraje o tiempo.\n\n¿Estás seguro de registrar "${b.dataset.name}" ahora?`))return;registerMaintenance(vehicleId,d.currentMileage,b.dataset.service,b.dataset.name)});
 document.querySelectorAll('.removeservicebtn').forEach(b=>b.onclick=async()=>{
  if(!confirm(`¿Quitar "${b.dataset.name}" de este vehículo?\n\nDejará de aparecer en su estado de mantenimiento. El historial de mantenimientos ya registrados con este servicio se conserva.`))return;
  const out2=await req(`${api}/plan-services/${b.dataset.service}/archive`,{method:'PATCH'});
  if(!out2.r||!out2.r.ok)return alert(out2.j.error?.message||'No se pudo quitar el servicio.');
  maintenanceView(vehicleId);
 });
}

async function addIndividualVehicleService(vehicleId){
 const catOut=await req(api+'/service-catalog'),catalog=catOut.r&&catOut.r.ok?(catOut.j.data||[]):[];
 const wrap=openFormModal({
  title:'Agregar servicio al vehículo',
  body:`${serviceIntervalFields({interval:'',pre:'1000'},catalog)}<p class="muted">Este servicio pertenecerá únicamente a este vehículo. No crea un Plan de Mantenimiento para otros vehículos.</p>`,
  onSubmit:async(f,err)=>{
    const interval=Number(String(f.get('interval')).replace(/\D/g,'')),pre=Number(String(f.get('pre')||'0').replace(/\D/g,''));
    if(!interval){err.textContent='Ingresa un intervalo válido.';return false}
    const out=await req(`${api}/vehicles/${vehicleId}/individual-services`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),category:'General',specification:f.get('spec')||null,intervalKm:interval,intervalMonths:null,prealertKm:pre,prealertDays:null})});
    if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo agregar el servicio.';return false}
    maintenanceView(vehicleId);return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly);wireServiceCatalogAutofill(wrap,catalog)
}

async function registerMaintenance(vehicleId,currentMileage,serviceId,serviceName){
 const wrap=openFormModal({
  title:`Registrar mantenimiento · ${serviceName}`,
  body:`<label>Kilometraje<input name="km" type="number" min="${currentMileage}" value="${currentMileage}" required></label>${dateField('date','Fecha del servicio')}<label>Observaciones<textarea name="notes" rows="3" placeholder="Opcional"></textarea></label>`,
  onSubmit:async(f,err)=>{
   const km=Number(f.get('km')),date=f.get('date'),notes=f.get('notes')||null;
   let out=await req(`${api}/vehicles/${vehicleId}/maintenance`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:km,serviceDate:date,serviceIds:[serviceId],notes,exceptionConfirmed:false})});
   if(out.j.data?.status==='CONFIRMATION_REQUIRED'){
     if(!confirm(`La lectura aumentó ${fmt(out.j.data.difference)} km desde el kilometraje actual.\n\n¿Confirmas que ${fmt(km)} km es correcto?`))return false;
     out=await req(`${api}/vehicles/${vehicleId}/maintenance`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:km,serviceDate:date,serviceIds:[serviceId],notes,exceptionConfirmed:true})});
   }
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo registrar el mantenimiento.';return false}
   maintenanceSuccess(vehicleId,km,serviceName,date);return true
  }
 });wireToday(wrap,'date')
}
function maintenanceSuccess(vehicleId,km,serviceName,date){
 const main=document.getElementById('main'),user=JSON.parse(localStorage.getItem('user')||'{}');
 main.innerHTML=card(`<div class="maintenance-success"><div class="success-check">✓</div><h1>¡Servicio registrado!</h1><div class="maintenance-success-service">${serviceNameWithIcon(serviceName)}</div><strong>${fmt(km)} <span>km</span></strong><small>${new Date(date+'T12:00:00').toLocaleDateString('es-CO')} · por ${esc(user.role==='TECHNICIAN'?'Técnico':'Administrador')}</small><p>El mantenimiento quedó guardado correctamente.</p><button id="successStatus">Ver estado de servicios</button></div>`);
 document.getElementById('successStatus').onclick=()=>maintenanceView(vehicleId)
}
async function maintenanceHistory(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/maintenance-history`);if(!out.r||!out.r.ok)return alert('No se pudo cargar el historial.');const rows=out.j.data||[];
 document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><h1>Historial de mantenimiento</h1>${rows.length?`<div class="historylist">${rows.map(x=>`<article class="historyrow"><div><strong>${esc(x.serviceName)}</strong><span>${new Date(x.serviceDate).toLocaleDateString('es-CO')} · ${fmt(x.mileage)} km</span><small>Técnico: ${esc(x.technician||'—')}${x.notes?` · ${esc(x.notes)}`:''}</small></div><div>${x.nextDueMileage!=null?`<b>Próximo ${fmt(x.nextDueMileage)} km</b>`:''}</div></article>`).join('')}</div>`:'<p class="muted">Todavía no hay mantenimientos registrados.</p>'}`);
 document.getElementById('back').onclick=()=>adminShell('vehicles');
}

async function loadBaseline(vehicleId,serviceId){
 const wrap=openFormModal({
  title:'Cargar último servicio conocido',
  body:`<label>Kilometraje del último servicio<input name="km" type="number" min="0" required></label>${dateField('date','Fecha del último servicio',false)}<p class="muted">Puedes usar Hoy o elegir cualquier fecha conocida.</p>`,
  onSubmit:async(f,err)=>{
   const km=f.get('km'),date=f.get('date');
   const out=await req(`${api}/vehicles/${vehicleId}/baselines`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({planServiceId:serviceId,lastServiceMileage:km===''?null:Number(km),lastServiceDate:date||null})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo guardar.';return false}
   maintenanceView(vehicleId);return true
  }
 });wireToday(wrap,'date')
}

async function mileageHistory(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/mileage-history`);if(!out.r||!out.r.ok)return alert('No se pudo cargar el historial de kilometraje.');
 const rows=out.j.data||[];const current=rows[0]?.mileage??0;
 document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><div class="dashboard-head"><div><h1>Historial de kilometraje</h1><p class="muted">Las lecturas del QR conservan su fecha y hora automáticas.</p></div><div class="mileage-head-actions"><button id="correctmileage" class="secondary">Corregir kilometraje actual</button><button id="addhistorical">+ Cargar lectura histórica</button></div></div>${rows.length?`<div class="historylist">${rows.map(x=>`<article class="historyrow"><div><strong>${fmt(x.mileage)} km</strong><span>${new Date(x.createdAt).toLocaleString('es-CO')}</span><small>${esc(sourceLabel(x.source))}${x.isExceptional?' · 🟠 Lectura excepcional':''}</small></div></article>`).join('')}</div>`:'<p class="muted">No hay lecturas todavía.</p>'}`);
 document.getElementById('back').onclick=()=>adminShell('vehicles');
 document.getElementById('addhistorical').onclick=()=>addHistoricalMileage(vehicleId);
 document.getElementById('correctmileage').onclick=()=>correctMileage(vehicleId,current);
}
function sourceLabel(s){return ({INITIAL:'Lectura inicial',PUBLIC_QR:'QR público',TECHNICIAN:'Técnico',ADMIN_HISTORICAL:'Carga histórica',ADMIN_CORRECTION:'Corrección administrativa',IMPORT:'Importación'})[s]||s}
function correctMileage(vehicleId,currentMileage){
 openFormModal({
  title:'Corregir kilometraje actual',
  body:`<p class="muted">Usa esta opción solo para corregir un error (por ejemplo, un dígito de más). Como administrador puedes poner cualquier valor, incluso menor al actual — no aplican los límites que sí se piden a conductores y técnicos.</p><label>Kilometraje actual<input name="km" type="number" min="0" value="${currentMileage}" required></label>`,
  onSubmit:async(f,err)=>{
   const km=Number(f.get('km'));
   if(!Number.isFinite(km)||km<0){err.textContent='Ingresa un kilometraje válido.';return false}
   if(!confirm(`¿Confirmas cambiar el kilometraje a ${fmt(km)} km?`))return false;
   const out=await req(`${api}/vehicles/${vehicleId}/mileage`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:km})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo corregir el kilometraje.';return false}
   mileageHistory(vehicleId);return true
  }
 })
}
function addHistoricalMileage(vehicleId){
 const wrap=openFormModal({
  title:'Cargar lectura histórica',
  body:`<label>Kilometraje<input name="km" type="number" min="0" required></label>${dateField('date','Fecha de la lectura')}<p class="muted">Uso administrativo. El QR público siempre registra automáticamente la fecha/hora real.</p>`,
  onSubmit:async(f,err)=>{
   const out=await req(`${api}/vehicles/${vehicleId}/mileage-history`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:Number(f.get('km')),readingDate:f.get('date')})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo guardar la lectura.';return false}
   mileageHistory(vehicleId);return true
  }
 });wireToday(wrap,'date')
}
