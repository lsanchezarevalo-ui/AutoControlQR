async function showArchivedPlans(){
 const out=await req(api+'/maintenance-plans/archived');if(!out.r||!out.r.ok)return alert('No se pudieron cargar los planes archivados.');
 const rows=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Planes archivados</h1><p class="muted">Puedes reactivarlos sin perder sus servicios ni versiones.</p></div><button id="backPlans" class="secondary">Volver a activos</button></div>`)+`<div class="list">${rows.length?rows.map(p=>card(`<div class="top"><div><h3>${esc(p.name)}</h3><p class="muted">${esc(p.brand)} ${esc(p.model)}</p></div><button class="reactplan" data-id="${p.id}">Reactivar</button></div>`)).join(''):card('<p class="muted">No hay planes archivados.</p>')}</div>`;
 document.getElementById('backPlans').onclick=()=>adminShell('plans');
 document.querySelectorAll('.reactplan').forEach(b=>b.onclick=async()=>{const x=await req(`${api}/maintenance-plans/${b.dataset.id}/reactivate`,{method:'PATCH'});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo reactivar.');adminShell('plans')});
}
async function archivePlan(id,name){
 if(!confirm(`¿Archivar el plan "${name}"?\n\nNo se borrará su historial. Solo puede archivarse si ningún vehículo activo lo está usando.`))return;
 const out=await req(`${api}/maintenance-plans/${id}/archive`,{method:'PATCH'});if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo archivar el plan.');adminShell('plans');
}
async function editPlan(id,plans,vehicles){
 const p=plans.find(x=>x.id===id);if(!p)return;
 const name=prompt('Nombre del plan:',p.name);if(name===null)return;
 const brand=prompt('Marca:',p.brand);if(brand===null)return;
 const model=prompt('Modelo:',p.model);if(model===null)return;
 if(!name.trim()||!brand.trim()||!model.trim())return alert('Nombre, marca y modelo son obligatorios.');
 const out=await req(`${api}/maintenance-plans/${id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:name.trim(),brand:titleCaseValue(brand),model:titleCaseValue(model)})});
 if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo actualizar el plan.');
 adminShell('plans');
}

function renderPlans(plans,vehicles=[]){
 const main=document.getElementById('main'),catalog=vehicleCatalog(vehicles,plans);
 main.innerHTML=card(`<div class="top plan-page-head">${moduleTitle('plans','Planes de mantenimiento','Define los servicios y sus intervalos. Un mismo plan puede utilizarse en uno o varios vehículos.')}<button type="button" id="showArchivedPlans" class="secondary mobile-archived-btn">Ver archivados</button></div><div class="plan-create-title"><h2>Nuevo plan</h2><p class="muted">Para un vehículo particular puedes comenzar con un plan sencillo y agregar únicamente los servicios que necesites.</p></div><form id="createPlan" class="plan-create-grid"><label>Nombre del plan<input name="name" placeholder="Ej. Mantenimiento personal" required></label>${catalogFields(catalog,'plan')}<div class="plan-create-action"><button>Crear plan</button></div></form><p id="planerr" class="error"></p>`)+`<div class="plan-list-head"><h2><span id="planCount">${plans.length}</span> plan${plans.length===1?'':'es'} activo${plans.length===1?'':'s'}</h2><div class="plan-search"><input id="planSearch" type="search" placeholder="Buscar plan, marca o modelo" aria-label="Buscar plan, marca o modelo" autocomplete="off"></div></div><div id="planList" class="list plan-list">${plans.length?plans.map(planCard).join(''):'<div class="no-search-results">Todavía no hay planes. Crea el primero arriba.</div>'}</div>`;
 document.getElementById('showArchivedPlans').onclick=()=>showArchivedPlans();
 const form=document.getElementById('createPlan');wireVehicleCatalog(form,catalog,'plan');form.querySelectorAll('.title-case').forEach(titleLive);
 form.onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const out=await req(api+'/maintenance-plans',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),brand:titleCaseValue(f.get('brand')),model:titleCaseValue(f.get('model')),variant:null})});if(!out.r||!out.r.ok){document.getElementById('planerr').textContent=out.j.error?.message||'No se pudo crear el plan.';return}adminShell('plans')};
 const wire=()=>{document.querySelectorAll('.editplan').forEach(b=>b.onclick=()=>editPlan(b.dataset.plan,plans,vehicles));document.querySelectorAll('.archiveplan').forEach(b=>b.onclick=()=>archivePlan(b.dataset.plan,b.dataset.name));document.querySelectorAll('.servicebtn').forEach(b=>b.onclick=()=>addService(b.dataset.version,b.dataset.name));document.querySelectorAll('.showservices').forEach(b=>b.onclick=()=>showServices(b.dataset.version,b.dataset.name))};wire();
 document.getElementById('planSearch').oninput=e=>{const q=e.target.value.trim().toLocaleLowerCase('es'),filtered=!q?plans:plans.filter(p=>[p.name,p.brand,p.model].some(x=>String(x||'').toLocaleLowerCase('es').includes(q)));document.getElementById('planCount').textContent=filtered.length;document.getElementById('planList').innerHTML=filtered.length?filtered.map(planCard).join(''):'<div class="no-search-results">No encontramos planes con ese criterio.</div>';wire()};
}
function planCard(p){return `<article class="planrow plan-card"><div class="plan-info"><div class="plan-title"><strong>${esc(p.name)}</strong><span class="plan-version">V${p.versionNumber}</span></div><span class="plan-model">${esc(p.brand)} ${esc(p.model)}</span></div><div class="plan-actions"><button class="showservices secondary" data-version="${p.versionId}" data-name="${esc(p.name)}">Servicios</button><button class="servicebtn" data-version="${p.versionId}" data-name="${esc(p.name)}">+ Agregar servicio</button><button class="editplan textbtn" data-plan="${p.id}">Editar</button><button class="archiveplan textbtn dangertext" data-plan="${p.id}" data-name="${esc(p.name)}">Archivar</button></div></article>`}

function serviceIntervalFields({name='',interval='',pre='0',spec=''}={}){
 return `<label>Servicio<input name="name" value="${esc(name)}" placeholder="Ej. Aceite de motor" required></label><div class="service-interval-grid"><label>Intervalo por kilometraje<input name="interval" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="Ej. 10000" value="${esc(String(interval))}" required></label><label>Prealerta<input name="pre" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" value="${esc(String(pre))}"></label></div><label>Especificación <small class="optional-label">Opcional</small><input name="spec" value="${esc(spec)}" placeholder="Ej. 5W-30"></label>`
}
async function addService(versionId,planName='Plan'){
 const wrap=openFormModal({
  title:`Agregar servicio · ${planName}`,
  body:`${serviceIntervalFields({interval:'',pre:'1000'})}<p class="muted">Ejemplo: intervalo 10.000 km y prealerta 1.000 km avisará cuando falten 1.000 km para el servicio.</p>`,
  onSubmit:async(f,err)=>{
   const interval=Number(String(f.get('interval')).replace(/\D/g,'')),pre=Number(String(f.get('pre')||'0').replace(/\D/g,''));
   if(!interval){err.textContent='Ingresa un intervalo de kilometraje válido.';return false}
   const out=await req(`${api}/plan-versions/${versionId}/services`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),category:'General',specification:f.get('spec')||null,intervalKm:interval,intervalMonths:null,prealertKm:pre,prealertDays:null})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo crear el servicio.';return false}
   showServices(versionId,planName);return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly)
}
async function showServices(versionId,name){
 const out=await req(`${api}/plan-versions/${versionId}/services`);if(!out.r||!out.r.ok)return alert('No se pudieron cargar los servicios.');
 const services=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<button id="backPlans" class="secondary">← Volver a planes</button><div class="service-page-head"><div><h1>${esc(name)}</h1><p class="muted">Servicios configurados en este plan.</p></div><div class="service-head-actions"><button id="editServicesHere" class="secondary">Editar servicios</button><button id="addServiceHere">+ Agregar servicio</button></div></div>${services.length?`<div class="service-plan-list">${services.map(x=>`<article class="service-plan-row"><div><strong>${esc(x.name)}</strong>${x.specification?`<small>${esc(x.specification)}</small>`:''}</div><div><small>Intervalo</small><b>${x.intervalKm?fmt(x.intervalKm)+' km':'—'}</b></div><div><small>Prealerta</small><b>${x.prealertKm?fmt(x.prealertKm)+' km':'—'}</b></div><div class="service-row-action service-edit-action"><button class="secondary editservice" data-service="${x.id}">Editar</button></div></article>`).join('')}</div>`:'<div class="no-search-results">Este plan todavía no tiene servicios. Agrega el primero para comenzar.</div>'}`);
 document.getElementById('backPlans').onclick=()=>adminShell('plans');
 document.getElementById('addServiceHere').onclick=()=>addService(versionId,name);
 const editToggle=document.getElementById('editServicesHere');
 let editMode=false;
 editToggle.onclick=()=>{editMode=!editMode;document.querySelectorAll('.service-edit-action').forEach(x=>x.classList.toggle('visible',editMode));editToggle.textContent=editMode?'Terminar edición':'Editar servicios'};
 document.querySelectorAll('.editservice').forEach(b=>b.onclick=()=>editService(versionId,name,services.find(x=>x.id===b.dataset.service)))
}

async function editService(versionId,planName,service){
 if(!service)return;
 const wrap=openFormModal({
  title:`Editar servicio · ${service.name}`,
  body:`${serviceIntervalFields({name:service.name,interval:service.intervalKm??'',pre:service.prealertKm??0,spec:service.specification||''})}<p class="muted">Este cambio modifica la configuración futura del plan. Los mantenimientos ya registrados conservan los datos históricos con los que fueron ejecutados.</p>`,
  onSubmit:async(f,err)=>{
    const interval=Number(String(f.get('interval')).replace(/\D/g,'')),pre=Number(String(f.get('pre')||'0').replace(/\D/g,''));
    if(!interval){err.textContent='Ingresa un intervalo de kilometraje válido.';return false}
    const out=await req(`${api}/plan-services/${service.id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),specification:f.get('spec')||null,intervalKm:interval,intervalMonths:null,prealertKm:pre,prealertDays:null})});
    if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo actualizar el servicio.';return false}
    showServices(versionId,planName);return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly)
}
