function vehicleCatalog(vehicles=[],plans=[]){
 const rows=[...vehicles,...plans].filter(x=>x&&x.brand&&x.model);
 const brands=[...new Set(rows.map(x=>x.brand.trim()).filter(Boolean))].sort((a,b)=>a.localeCompare(b,'es'));
 const modelsByBrand={};
 rows.forEach(x=>{const b=x.brand.trim(),m=x.model.trim();if(!modelsByBrand[b])modelsByBrand[b]=[];if(m&&!modelsByBrand[b].some(v=>v.toLocaleLowerCase('es')===m.toLocaleLowerCase('es')))modelsByBrand[b].push(m)});
 Object.values(modelsByBrand).forEach(x=>x.sort((a,b)=>a.localeCompare(b,'es')));
 return {brands,modelsByBrand};
}
function catalogFields(catalog,prefix=''){
 const bid=`${prefix}brands`,mid=`${prefix}models`;
 return `<label>Marca<input name="brand" class="title-case" list="${bid}" placeholder="Escribe o selecciona marca" autocomplete="off" required><datalist id="${bid}">${catalog.brands.map(x=>`<option value="${esc(x)}"></option>`).join('')}</datalist></label><label>Modelo<input name="model" class="title-case" list="${mid}" placeholder="Escribe o selecciona modelo" autocomplete="off" required><datalist id="${mid}"></datalist></label>`;
}
function wireVehicleCatalog(form,catalog,prefix=''){
 const brand=form.elements.brand,model=form.elements.model,list=document.getElementById(`${prefix}models`);
 const refresh=()=>{const typed=brand.value.trim().toLocaleLowerCase('es');const exact=Object.keys(catalog.modelsByBrand).find(b=>b.toLocaleLowerCase('es')===typed);const models=exact?catalog.modelsByBrand[exact]:[...new Set(Object.values(catalog.modelsByBrand).flat())].sort((a,b)=>a.localeCompare(b,'es'));list.innerHTML=models.map(x=>`<option value="${esc(x)}"></option>`).join('')};
 brand.addEventListener('input',refresh);brand.addEventListener('change',refresh);refresh();
}

function wireVehicleButtons(vehicles,plans,isAdmin=true){
 if(isAdmin)document.querySelectorAll('.editvehicle').forEach(b=>b.onclick=()=>editVehicle(b.dataset.vehicle,vehicles,plans));
 if(isAdmin)document.querySelectorAll('.archivevehicle').forEach(b=>b.onclick=()=>archiveVehicle(b.dataset.vehicle,b.dataset.plate));
 document.querySelectorAll('.maintbtn').forEach(b=>b.onclick=()=>maintenanceView(b.dataset.vehicle));
 document.querySelectorAll('.historybtn').forEach(b=>b.onclick=()=>maintenanceHistory(b.dataset.vehicle));
 if(isAdmin)document.querySelectorAll('.kmhistorybtn').forEach(b=>b.onclick=()=>mileageHistory(b.dataset.vehicle));
 document.querySelectorAll('.labelbtn').forEach(b=>b.onclick=()=>printQrLabel(b.dataset.vehicle));
}

function renderVehiclesQuickMenu(){
 const main=document.getElementById('main');
 main.innerHTML=quickMenuView('vehicle','Vehículos','¿Qué quieres hacer?',[
  {action:'search',icon:'search',label:'Buscar vehículo',desc:'Busca por placa o número interno'},
  {action:'create',icon:'plus',label:'Crear nuevo',desc:'Registra un vehículo en la flota'},
  {action:'list',icon:'list',label:'Ver vehículos',desc:'Consulta la lista completa'}
 ]);
 document.querySelectorAll('.quick-menu-item').forEach(b=>b.onclick=()=>adminShell('vehicles',b.dataset.action));
}
function renderVehicles(vehicles,plans,isAdmin=true,action=null){
 const main=document.getElementById('main'),catalog=vehicleCatalog(vehicles,plans),isMobile=innerWidth<=800;
 const backLink=(isAdmin&&isMobile&&action)?`<button type="button" id="vehiclesBackMenu" class="textbtn quick-menu-back">‹ Volver</button>`:'';
 main.innerHTML=backLink+(isAdmin?card(`<div class="top vehicle-page-head">${moduleTitle('vehicle','Vehículos','Administra la flota y consulta el mantenimiento de cada unidad.')}<button type="button" id="showArchivedVehicles" class="secondary mobile-archived-btn">Ver archivados</button></div><div class="vehicle-create-title"><h2>Nuevo vehículo</h2><p class="muted">Completa los datos. El plan es opcional; también puedes crear servicios propios para este vehículo.</p></div><form id="create" class="vehicle-create-grid"><label>Placa<input name="plate" class="force-upper" placeholder="ABC-123" autocomplete="off" required></label><label>Número interno<input name="internal" class="force-upper" placeholder="Opcional" autocomplete="off"></label>${catalogFields(catalog,'vehicle')}<label>Kilometraje<input name="km" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="134500" required></label><label>Plan de mantenimiento <small class="optional-label">Opcional</small><select name="planVersionId"><option value="">Sin plan por ahora</option>${plans.map(p=>`<option value="${p.versionId}">${esc(p.name)} V${p.versionNumber}</option>`).join('')}</select></label><div class="vehicle-create-action"><button>Crear vehículo</button></div></form><p id="err" class="error"></p>`):card(`<h1>Vehículos</h1><p class="muted">Consulta el estado y registra los mantenimientos realizados.</p>`))+`<div class="vehicle-list-head"><h2><span id="vehicleCount">${vehicles.length}</span> vehículo${vehicles.length===1?'':'s'} activo${vehicles.length===1?'':'s'}</h2><div class="vehicle-search"><input id="vehicleSearch" type="search" placeholder="Buscar por placa o interno" aria-label="Buscar por placa o interno" autocomplete="off"></div></div><div id="vehicleList" class="list vehicle-list">${vehicles.map(v=>vehicleCard(v,plans,isAdmin)).join('')}</div>`;
 const vehicleSearch=document.getElementById('vehicleSearch');
 if(vehicleSearch)vehicleSearch.oninput=()=>{const q=vehicleSearch.value.trim().toUpperCase();const filtered=!q?vehicles:vehicles.filter(v=>String(v.plate||'').toUpperCase().includes(q)||String(v.internalNumber||'').toUpperCase().includes(q));document.getElementById('vehicleList').innerHTML=filtered.length?filtered.map(v=>vehicleCard(v,plans,isAdmin)).join(''):'<div class="no-search-results">No encontramos vehículos con esa placa o número interno.</div>';document.getElementById('vehicleCount').textContent=filtered.length;wireVehicleButtons(filtered,plans,isAdmin)};
 if(isAdmin){
  document.getElementById('showArchivedVehicles').onclick=()=>showArchivedVehicles(plans);
  const form=document.getElementById('create');wireVehicleCatalog(form,catalog,'vehicle');form.querySelectorAll('.force-upper').forEach(upperLive);form.querySelectorAll('.title-case').forEach(titleLive);form.querySelectorAll('.integer-km').forEach(integerOnly);
  form.onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const out=await req(api+'/vehicles',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({plate:String(f.get('plate')).trim().toUpperCase(),internalNumber:f.get('internal')?String(f.get('internal')).trim().toUpperCase():null,brand:titleCaseValue(f.get('brand')),model:titleCaseValue(f.get('model')),variant:null,currentMileage:Number(String(f.get('km')).replace(/\D/g,'')),planVersionId:f.get('planVersionId')||null})});if(!out.r||!out.r.ok){document.getElementById('err').textContent=out.j.error?.message||'No se pudo crear.';return}adminShell('vehicles')};
 }
 wireVehicleButtons(vehicles,plans,isAdmin);
 if(backLink)document.getElementById('vehiclesBackMenu').onclick=()=>adminShell('vehicles');
 if(isMobile&&action==='search'&&vehicleSearch){vehicleSearch.scrollIntoView({block:'start'});vehicleSearch.focus()}
 else if(isMobile&&action==='create'&&isAdmin){document.querySelector('.vehicle-create-title')?.scrollIntoView({block:'start'})}
 else if(isMobile&&action==='list'){document.getElementById('vehicleList')?.scrollIntoView({block:'start'})}
}

async function showArchivedVehicles(plans){
 const out=await req(api+'/vehicles/archived');if(!out.r||!out.r.ok)return alert('No se pudieron cargar los vehículos archivados.');
 const rows=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Vehículos archivados</h1><p class="muted">Conservan todo su historial. Para volver a operación debes seleccionar un plan activo.</p></div><button id="backVehicles" class="secondary">Volver a activos</button></div>`)+`<div class="list">${rows.length?rows.map(v=>card(`<div class="top"><div><h3>${esc(v.plate)}${v.internalNumber?' / '+esc(v.internalNumber):''}</h3><p class="muted">${esc(v.brand)} ${esc(v.model)} · ${fmt(v.currentMileage)} km</p></div><div class="actions"><select id="reactplan-${v.id}"><option value="">Plan para reactivar…</option>${plans.map(p=>`<option value="${p.versionId}">${esc(p.name)} V${p.versionNumber}</option>`).join('')}</select><button class="reactvehicle" data-id="${v.id}">Reactivar</button></div></div>`)).join(''):card('<p class="muted">No hay vehículos archivados.</p>')}</div>`;
 document.getElementById('backVehicles').onclick=()=>adminShell('vehicles');
 document.querySelectorAll('.reactvehicle').forEach(b=>b.onclick=async()=>{const planVersionId=document.getElementById(`reactplan-${b.dataset.id}`).value;if(!planVersionId)return alert('Selecciona el plan con el que volverá a operación.');const x=await req(`${api}/vehicles/${b.dataset.id}/reactivate`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({planVersionId})});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo reactivar.');adminShell('vehicles')});
}
async function archiveVehicle(id,plate){
 if(!confirm(`¿Archivar el vehículo ${plate}?\n\nNo se borrará su historial. El QR quedará desactivado y el vehículo dejará de aparecer en la operación activa.`))return;
 const out=await req(`${api}/vehicles/${id}/archive`,{method:'PATCH'});if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo archivar el vehículo.');adminShell('vehicles');
}
async function editVehicle(id,vehicles,plans){
 const v=vehicles.find(x=>x.id===id);if(!v)return;
 const catalog=vehicleCatalog(vehicles,plans);
 const body=`<label>Placa<input name="plate" class="force-upper" value="${esc(v.plate)}" required></label><label>Número interno<input name="internal" class="force-upper" value="${esc(v.internalNumber||'')}" placeholder="Opcional"></label>${catalogFields(catalog,'editvehicle')}<label>Plan de mantenimiento<select name="planVersionId" required><option value="">Selecciona plan</option>${plans.map(p=>`<option value="${p.versionId}" ${v.planVersionId===p.versionId?'selected':''}>${esc(p.name)} V${p.versionNumber}</option>`).join('')}</select></label>`;
 const wrap=openFormModal({title:`Editar vehículo · ${v.plate}`,body,onSubmit:async(f,err)=>{
   const out=await req(`${api}/vehicles/${id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({plate:String(f.get('plate')).trim().toUpperCase(),internalNumber:f.get('internal')?String(f.get('internal')).trim().toUpperCase():null,brand:titleCaseValue(f.get('brand')),model:titleCaseValue(f.get('model')),planVersionId:f.get('planVersionId')})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo actualizar el vehículo.';return false}
   adminShell('vehicles');return true
 }});
 const form=wrap.querySelector('.modalform');form.elements.brand.value=v.brand;form.elements.model.value=v.model;wireVehicleCatalog(form,catalog,'editvehicle');form.querySelectorAll('.force-upper').forEach(upperLive);form.querySelectorAll('.title-case').forEach(titleLive);
}

function vehicleCard(v,plans,isAdmin=true){
 return `<article class="vehicle vehicle-card"><div class="vehicle-content"><div class="vehicle-main"><div class="vehicle-title"><strong>${esc(v.plate)}${v.internalNumber?` <span class="vehicle-slash">/</span> ${esc(v.internalNumber)}`:''}</strong></div><div class="vehicle-model">${esc(v.brand)} ${esc(v.model)}</div><div class="vehicle-meta"><div><small>Kilometraje</small><b>${fmt(v.currentMileage)} km</b></div><div><small>Plan de mantenimiento</small><b>${v.planName?`${esc(v.planName)} · V${v.planVersion}`:'Servicios individuales / Sin plan'}</b></div></div><div class="vehicle-actions-main"><button class="maintbtn vehicle-action-btn" data-vehicle="${v.id}">${uiIcon('check')}Estado de mantenimiento</button><button class="historybtn secondary vehicle-action-btn vehicle-action-history" data-vehicle="${v.id}">${uiIcon('reports')}Historial</button>${isAdmin?`<button class="kmhistorybtn secondary vehicle-action-btn vehicle-action-km" data-vehicle="${v.id}">${uiIcon('gauge')}Kilometrajes</button>`:''}</div>${isAdmin?`<div class="vehicle-actions-admin"><button class="editvehicle textbtn" data-vehicle="${v.id}">Editar vehículo</button><button class="archivevehicle textbtn dangertext" data-vehicle="${v.id}" data-plate="${esc(v.plate)}">Archivar</button></div>`:''}</div></div>${v.qrToken?`<div class="qr vehicle-qr"><img src="${api}/public/qr/${encodeURIComponent(v.qrToken)}.svg"><div class="qr-actions"><a class="button secondary" href="/v/${encodeURIComponent(v.qrToken)}">Abrir QR</a><button class="secondary labelbtn" data-vehicle="${v.id}">Imprimir etiqueta</button></div></div>`:''}</article>`;
}
async function printQrLabel(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/qr-label`);if(!out.r||!out.r.ok)return alert('No se pudo preparar la etiqueta QR.');
 const v=out.j.data,qr=`${api}/public/qr/${encodeURIComponent(v.qrToken)}.svg`;let w=window.open('','_blank');if(!w){return alert('Safari bloqueó la ventana de impresión. Activa temporalmente las ventanas emergentes para AutoControl QR e inténtalo de nuevo.')}
 w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Etiqueta QR ${esc(v.plate)}</title><style>
 @page{size:60mm 60mm;margin:0}*{box-sizing:border-box}html,body{width:60mm;height:60mm;font-family:Arial,sans-serif;margin:0;color:#111;overflow:hidden}.label{width:60mm;height:60mm;border:1.2px solid #111;border-radius:2.5mm;padding:1.5mm;margin:auto;text-align:center;display:flex;flex-direction:column;align-items:center}.brand{font-size:10px;font-weight:900;letter-spacing:.4px}.tag{font-size:8px;margin:0 0 2px}.company{font-size:7px;font-weight:700;margin:0}.plate-label{font-size:8px;font-weight:bold}.plate{font-size:17px;font-weight:900;letter-spacing:1px;margin:0}.internal{font-size:11px;font-weight:bold;margin-bottom:.5mm}.qrimg{width:36mm;height:36mm}.instruction{font-size:8px;font-weight:800;margin-top:.5mm}.sub{font-size:6px;margin:0}.vehicle{font-size:9px;margin-top:3mm;color:#444}@media print{button{display:none}.label{break-inside:avoid}}button{margin:20px auto;display:block;padding:10px 18px}</style></head><body><div class="label"><div class="brand">AUTOCONTROL QR</div><div class="company">${esc(v.companyName)}</div><div class="plate">${esc(v.plate)}${v.internalNumber?` / ${esc(v.internalNumber)}`:''}</div><img class="qrimg" src="${qr}"><div class="instruction">Escanee para actualizar kilometraje</div><div class="sub">Mantenga este código visible y en buen estado.</div></div><button onclick="window.print()">Imprimir / Guardar PDF</button><script>window.addEventListener('load',()=>setTimeout(()=>window.print(),450));<\/script></body></html>`);w.document.close();
}
