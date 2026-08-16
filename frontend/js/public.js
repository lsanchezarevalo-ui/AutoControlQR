async function publicView(qr,successMileage=null){
 const out=await req(api+'/public/v/'+encodeURIComponent(qr));
 if(!out.r||!out.r.ok){app.innerHTML=card('<h1>QR no válido</h1><p class="muted">Este código no está activo o no corresponde a un vehículo disponible.</p>','narrow');return}
 const v=out.j.data,services=v.services||[];
 const statusText={UP_TO_DATE:'Al día',DUE_SOON:'Próximo',OVERDUE:'Vencido',NO_BASELINE:'Sin historial',NO_PLAN:'Sin plan'};
 app.innerHTML=`<main class="public-shell"><section class="public-card">
  ${successMileage!==null?`<div class="public-success public-success-focus"><div class="success-check">✓</div><strong>¡Kilometraje actualizado!</strong><span>${fmt(successMileage)} <em>km</em></span><small>${new Date().toLocaleString('es-CO')} · por Conductor</small><p>Gracias. Kilometraje registrado.</p><button type="button" class="secondary" id="successServices">Ver Estado De Servicios</button><button type="button" class="public-exit" id="publicExit">Salir</button></div>`:''}
  <div class="public-regular ${successMileage!==null?'after-success':''}"><div class="public-top"><div><div class="eyebrow">AUTOCONTROL QR</div><h1>${esc(v.plate)}${v.internalNumber?` <span class="public-slash">/</span> ${esc(v.internalNumber)}`:''}</h1><p>${esc(v.brand)} ${esc(v.model)}</p></div><span class="public-status ${String(v.overallStatus||'').toLowerCase()}">${statusText[v.overallStatus]||v.overallStatus}</span></div>
  <div class="public-mileage"><small>Kilometraje actual</small><strong>${fmt(v.currentMileage)} <span>km</span></strong><p>Última actualización: ${new Date(v.lastMileageUpdate).toLocaleDateString('es-CO')}</p></div>
  <div class="public-update"><h2>Actualizar kilometraje</h2><p class="muted">Ingresa la lectura que muestra actualmente el vehículo.</p><form id="km"><label>Nueva lectura<input name="mileage" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="${v.currentMileage}" required></label><button>Guardar kilometraje</button></form><p id="msg"></p></div>
  <details class="public-maintenance"><summary>Ver estado de mantenimiento</summary><div class="public-maintenance-body">${services.length?services.map(s=>`<div class="public-service"><div><strong class="service-name-icon">${serviceNameWithIcon(s.name)}</strong><small>${s.nextDueMileage!=null?`Próximo: ${fmt(s.nextDueMileage)} km`:s.nextDueDate?`Próximo: ${new Date(s.nextDueDate).toLocaleDateString('es-CO')}`:'Sin fecha/km próximo'}</small></div>${statusBadge(s.status)}</div>`).join(''):'<p class="muted">Este vehículo todavía no tiene servicios configurados.</p>'}</div></details>
  <p class="public-note">Esta pantalla permite actualizar kilometraje y consultar información. No permite registrar mantenimientos ni cambiar planes.</p></div>
 </section></main>`;
 const successBtn=document.getElementById('successServices');if(successBtn)successBtn.onclick=()=>{const regular=document.querySelector('.public-regular');if(regular)regular.classList.add('show-after-success');const details=document.querySelector('.public-maintenance');if(details){details.open=true;details.scrollIntoView({behavior:'smooth',block:'start'})}};const publicExit=document.getElementById('publicExit');if(publicExit)publicExit.onclick=()=>{location.href='/'};
 const input=document.querySelector('#km .integer-km');if(input)integerOnly(input);
 document.getElementById('km').onsubmit=async e=>{
   e.preventDefault();const km=Number(String(new FormData(e.target).get('mileage')).replace(/\D/g,''));
   if(!Number.isFinite(km)||km<v.currentMileage){document.getElementById('msg').className='error';document.getElementById('msg').textContent=`El kilometraje debe ser igual o mayor que ${fmt(v.currentMileage)} km.`;return}
   let r=await req(api+'/public/v/'+encodeURIComponent(qr)+'/mileage',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:km,exceptionConfirmed:false})});
   if(r.j.data?.status==='CONFIRMATION_REQUIRED'){
     if(!confirm(`La lectura aumentó ${fmt(r.j.data.difference)} km desde la última actualización.\n\n¿Confirmas que ${fmt(km)} km es correcto?`))return;
     r=await req(api+'/public/v/'+encodeURIComponent(qr)+'/mileage',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mileage:km,exceptionConfirmed:true})})
   }
   if(!r.r||!r.r.ok){document.getElementById('msg').className='error';document.getElementById('msg').textContent=r.j.error?.message||'No se pudo actualizar.';return}
   publicView(qr,km)
 };
}
