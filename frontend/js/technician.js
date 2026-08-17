async function technicianShell(){
 const me=JSON.parse(localStorage.getItem('user')||'null')||{},selected=JSON.parse(localStorage.getItem('workVehicle')||'null');
 app.innerHTML=`<div class="shell"><header class="tech-header"><div><div class="eyebrow">AUTOCONTROL QR · V31.6</div><strong>Modo Técnico</strong><small class="roleline">${esc(me.fullName||'Técnico')}</small></div><nav>${selected?'<button id="changeVehicle" class="secondary tech-nav-btn">Cambiar vehículo</button>':''}<button id="techMyPassword" class="secondary tech-nav-btn">Mi cuenta</button><button id="logout" class="secondary tech-nav-btn">Salir</button></nav></header><main id="main"></main></div>`;
 document.getElementById('logout').onclick=()=>{localStorage.clear();loginView()};const techMyPassword=document.getElementById('techMyPassword');if(techMyPassword)techMyPassword.onclick=changeOwnPasswordModal;
 if(selected){document.getElementById('changeVehicle').onclick=clearTechnicianVehicle;return technicianVehicleHome(selected)}
 technicianSelectView();
}

function technicianSelectView(){
 const main=document.getElementById('main');
 main.innerHTML=`<section class="techselect"><div class="techhero"><div class="eyebrow">INICIAR TRABAJO</div><h1>¿Qué vehículo vas a atender?</h1></div>
 <div class="techchoices">${card(`<h2>Buscar por placa o interno</h2><form id="techsearch"><label>Placa o número interno<input name="search" placeholder="Ej. ABC-123 o 254" autocomplete="off" required></label><button>Buscar</button></form><div id="techresults"></div>`)}
 ${card(`<h2>Escanear QR</h2><p class="muted">Usa la cámara para leer el QR pegado al vehículo.</p><button id="scanqr">Abrir cámara</button><div id="scanner"></div><details><summary>No puedo usar la cámara</summary><form id="qrmanual"><label>Pegar URL o código QR<input name="qr" placeholder="http://.../v/código"></label><button class="secondary">Abrir vehículo</button></form></details>`)}</div></section>`;
 document.getElementById('techsearch').onsubmit=searchTechnicianVehicle;
 document.getElementById('scanqr').onclick=startQrScanner;
 document.getElementById('qrmanual').onsubmit=e=>{e.preventDefault();selectTechnicianQr(new FormData(e.target).get('qr'))};
}
async function searchTechnicianVehicle(e){
 e.preventDefault();const q=new FormData(e.target).get('search').trim(),box=document.getElementById('techresults');box.innerHTML='<p class="muted">Buscando…</p>';
 const out=await req(`${api}/technician/vehicle-lookup?search=${encodeURIComponent(q)}`);
 if(!out.r||!out.r.ok){box.innerHTML=`<p class="error">${esc(out.j.error?.message||'No se pudo buscar.')}</p>`;return}
 const rows=out.j.data||[];if(rows.length===1)return selectTechnicianVehicle(rows[0].id);box.innerHTML=rows.length?`<div class="techresults">${rows.map(v=>`<button class="techvehicle" data-id="${v.id}"><b>${esc(v.plate)}</b>${v.internalNumber?`<span>Interno ${esc(v.internalNumber)}</span>`:''}<small>${esc(v.brand)} ${esc(v.model)} · ${fmt(v.currentMileage)} km</small></button>`).join('')}</div>`:'<p class="muted">No se encontró ningún vehículo.</p>';
 box.querySelectorAll('.techvehicle').forEach(b=>b.onclick=()=>selectTechnicianVehicle(b.dataset.id));
}
async function selectTechnicianVehicle(vehicleId){
 const out=await req(`${api}/technician/select-vehicle`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({vehicleId,qrToken:null})});
 if(!out.r||!out.r.ok)return alert('No se pudo abrir ese vehículo.');
 localStorage.setItem('token',out.j.data.accessToken);localStorage.setItem('workVehicle',JSON.stringify(out.j.data.vehicle));technicianShell();
}
function qrTokenFromValue(value){
 value=String(value||'').trim();if(!value)return '';
 try{const u=new URL(value,location.origin);const m=u.pathname.match(/\/v\/([^/?#]+)/);if(m)return decodeURIComponent(m[1])}catch{}
 const m=value.match(/\/v\/([^/?#]+)/);return m?decodeURIComponent(m[1]):value;
}
async function selectTechnicianQr(value){
 const qrToken=qrTokenFromValue(value);if(!qrToken)return alert('No se reconoció el QR.');
 const out=await req(`${api}/technician/select-vehicle`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({vehicleId:null,qrToken})});
 if(!out.r||!out.r.ok)return alert('Ese QR no corresponde a un vehículo disponible.');
 localStorage.setItem('token',out.j.data.accessToken);localStorage.setItem('workVehicle',JSON.stringify(out.j.data.vehicle));technicianShell();
}
async function clearTechnicianVehicle(){
 const out=await req(`${api}/technician/clear-vehicle`,{method:'POST'});
 if(out.r&&out.r.ok)localStorage.setItem('token',out.j.data.accessToken);
 localStorage.removeItem('workVehicle');technicianShell();
}
async function startQrScanner(){
 const box=document.getElementById('scanner');
 if(typeof jsQR!=='function'){box.innerHTML='<p class="warntext">No se pudo cargar el lector QR. Usa la búsqueda por placa/interno o pega el código QR abajo.</p>';return}
 let stream;
 try{
  stream=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'}}});
  box.innerHTML='<video id="qrvideo" autoplay playsinline muted></video><p class="muted">Apunta la cámara al QR del vehículo.</p><button id="stopscan" class="secondary">Cancelar cámara</button>';
  const video=document.getElementById('qrvideo');video.srcObject=stream;let active=true;
  const canvas=document.createElement('canvas'),ctx=canvas.getContext('2d',{willReadFrequently:true});
  document.getElementById('stopscan').onclick=()=>{active=false;stream.getTracks().forEach(t=>t.stop());box.innerHTML=''};
  const scan=()=>{
   if(!active)return;
   if(video.readyState===video.HAVE_ENOUGH_DATA){
    canvas.width=video.videoWidth;canvas.height=video.videoHeight;
    ctx.drawImage(video,0,0,canvas.width,canvas.height);
    const frame=ctx.getImageData(0,0,canvas.width,canvas.height);
    const code=jsQR(frame.data,frame.width,frame.height,{inversionAttempts:'dontInvert'});
    if(code&&code.data){active=false;stream.getTracks().forEach(t=>t.stop());return selectTechnicianQr(code.data)}
   }
   requestAnimationFrame(scan)
  };requestAnimationFrame(scan);
 }catch(e){box.innerHTML='<p class="error">No se pudo abrir la cámara. Revisa el permiso del navegador o usa la búsqueda manual.</p>'}
}
function technicianVehicleHome(v){
 const main=document.getElementById('main');
 main.innerHTML=`<div class="tech-selected-label"><span>Vehículo seleccionado</span><b>Solo puedes trabajar sobre esta unidad</b></div>${card(`<div class="techwork"><div class="tech-vehicle-info"><div class="eyebrow">VEHÍCULO EN TRABAJO</div><h1>${esc(v.plate)}${v.internalNumber?` <span class="tech-slash">/</span> ${esc(v.internalNumber)}`:''}</h1><p>${esc(v.brand)} ${esc(v.model)}</p><div class="tech-km-label">Kilometraje actual</div><div class="vehicle-km">${fmt(v.currentMileage)} <small>km</small></div></div><div class="techworkactions"><button id="workstatus">Estado y registrar mantenimiento</button><button id="workhistory" class="secondary">Ver historial</button></div></div>`)}
 <p class="techlock"><b>Acceso limitado:</b> esta sesión solo permite consultar y registrar información de ${esc(v.plate)}. Usa “Cambiar vehículo” para trabajar sobre otra unidad.</p>`;
 document.getElementById('workstatus').onclick=()=>maintenanceView(v.id);document.getElementById('workhistory').onclick=()=>maintenanceHistory(v.id);
}
