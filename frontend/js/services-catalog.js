function serviceCatalogFormFields({name='',category='General',spec='',intervalKm='',intervalMonths='',pre='0'}={},existingNames=[]){
 return `<label>Nombre del servicio<input name="name" list="existingServiceNames" value="${esc(name)}" placeholder="Ej. Cambio de aceite de motor" required autocomplete="off"><small id="csNameDup" class="warntext" style="display:none">Ya existe un servicio con ese nombre.</small></label><datalist id="existingServiceNames">${existingNames.map(n=>`<option value="${esc(n)}"></option>`).join('')}</datalist><div class="service-interval-grid"><label>Intervalo por kilometraje <small class="optional-label">Opcional</small><input name="intervalKm" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="Ej. 10000" value="${esc(String(intervalKm))}"></label><label>Intervalo por meses <small class="optional-label">Opcional</small><input name="intervalMonths" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="Ej. 6" value="${esc(String(intervalMonths))}"></label></div><label>Prealerta (km) <small class="optional-label">Opcional</small><input name="pre" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" value="${esc(String(pre))}"></label><label>Especificación <small class="optional-label">Opcional</small><input name="spec" value="${esc(spec)}" placeholder="Ej. 5W-30"></label>`
}
function wireServiceCatalogDupWarning(root,existingNames,ownName=''){
 const nameInput=root.querySelector('[name="name"]'),warn=root.querySelector('#csNameDup');if(!nameInput||!warn)return;
 const check=()=>{const v=nameInput.value.trim().toLocaleLowerCase('es');warn.style.display=(v && v!==ownName.trim().toLocaleLowerCase('es') && existingNames.some(n=>n.toLocaleLowerCase('es')===v))?'block':'none'};
 nameInput.addEventListener('input',check);check();
}
function serviceCatalogPayload(f){
 const intervalKm=f.get('intervalKm')?Number(String(f.get('intervalKm')).replace(/\D/g,'')):null;
 const intervalMonths=f.get('intervalMonths')?Number(String(f.get('intervalMonths')).replace(/\D/g,'')):null;
 const pre=f.get('pre')?Number(String(f.get('pre')).replace(/\D/g,'')):null;
 return {name:f.get('name'),category:f.get('category')||'General',specification:f.get('spec')||null,defaultIntervalKm:intervalKm||null,defaultIntervalMonths:intervalMonths||null,defaultPrealertKm:pre,defaultPrealertDays:null};
}
async function renderServicesCatalog(){
 const main=document.getElementById('main'),out=await req(api+'/service-catalog');
 if(!out.r||!out.r.ok){main.innerHTML=card('<p class="error">No se pudieron cargar los servicios.</p>');return}
 const services=out.j.data||[];
 const serviceCard=s=>`<article class="service-catalog-card"><div class="service-catalog-info"><div class="service-catalog-title"><strong>${esc(s.name)}</strong><span class="rolepill">${esc(s.category)}</span></div>${s.specification?`<small class="muted">${esc(s.specification)}</small>`:''}<div class="service-catalog-meta">${s.defaultIntervalKm?`<span>${fmt(s.defaultIntervalKm)} km</span>`:''}${s.defaultIntervalMonths?`<span>${s.defaultIntervalMonths} meses</span>`:''}${s.defaultPrealertKm?`<span>Prealerta ${fmt(s.defaultPrealertKm)} km</span>`:''}</div></div><div class="service-catalog-actions"><button class="secondary editcs" data-id="${s.id}">Editar</button><button class="textbtn dangertext archivecs" data-id="${s.id}" data-name="${esc(s.name)}">Archivar</button></div></article>`;
 const existingNames=services.map(s=>s.name);
 main.innerHTML=card(`<div class="top service-catalog-page-head">${moduleTitle('service','Servicios','Catálogo de servicios de mantenimiento de tu empresa. Úsalo al armar planes o al agregar servicios a un vehículo.')}<button type="button" id="showArchivedServices" class="secondary mobile-archived-btn">Ver archivados</button></div><div class="service-catalog-create-title"><h2>Nuevo servicio</h2><p class="muted">Este servicio quedará disponible para usar en cualquier plan o vehículo de tu empresa.</p></div><form id="createServiceCatalog" class="service-catalog-create-grid">${serviceCatalogFormFields({},existingNames)}<div class="service-catalog-create-action"><button>Crear servicio</button></div></form><p id="csErr" class="error"></p>`)+`<div class="service-catalog-list-head"><h2><span id="csCount">${services.length}</span> servicio${services.length===1?'':'s'} activo${services.length===1?'':'s'}</h2><div class="service-catalog-search"><input id="csSearch" type="search" placeholder="Buscar servicio o categoría" aria-label="Buscar servicio o categoría" autocomplete="off"></div></div><div id="csList" class="list service-catalog-list">${services.length?services.map(serviceCard).join(''):'<div class="no-search-results">Todavía no hay servicios en el catálogo. Crea el primero arriba.</div>'}</div>`;
 const form=document.getElementById('createServiceCatalog');form.querySelectorAll('.integer-km').forEach(integerOnly);wireServiceCatalogDupWarning(form,existingNames);
 form.onsubmit=async e=>{e.preventDefault();const out2=await req(api+'/service-catalog',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(serviceCatalogPayload(new FormData(e.target)))});if(!out2.r||!out2.r.ok){document.getElementById('csErr').textContent=out2.j.error?.message||'No se pudo crear el servicio.';return}renderServicesCatalog()};
 document.getElementById('showArchivedServices').onclick=()=>showArchivedServices();
 const wire=()=>{
  document.querySelectorAll('.editcs').forEach(b=>b.onclick=()=>editServiceCatalog(services.find(x=>x.id===b.dataset.id),existingNames));
  document.querySelectorAll('.archivecs').forEach(b=>b.onclick=async()=>{if(!confirm(`¿Archivar el servicio "${b.dataset.name}"?\n\nLos planes y vehículos que ya lo usan no se ven afectados. Solo dejará de aparecer como opción para nuevos servicios.`))return;const x=await req(`${api}/service-catalog/${b.dataset.id}/archive`,{method:'PATCH'});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo archivar el servicio.');renderServicesCatalog()});
 };wire();
 document.getElementById('csSearch').oninput=e=>{const q=e.target.value.trim().toLocaleLowerCase('es'),filtered=!q?services:services.filter(s=>[s.name,s.category].some(x=>String(x||'').toLocaleLowerCase('es').includes(q)));document.getElementById('csCount').textContent=filtered.length;document.getElementById('csList').innerHTML=filtered.length?filtered.map(serviceCard).join(''):'<div class="no-search-results">No encontramos servicios con ese criterio.</div>';wire()};
}
function editServiceCatalog(s,existingNames=[]){
 if(!s)return;
 const wrap=openFormModal({
  title:`Editar servicio · ${s.name}`,
  body:serviceCatalogFormFields({name:s.name,category:s.category,spec:s.specification||'',intervalKm:s.defaultIntervalKm||'',intervalMonths:s.defaultIntervalMonths||'',pre:s.defaultPrealertKm||0},existingNames),
  onSubmit:async(f,err)=>{
   const out=await req(`${api}/service-catalog/${s.id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify(serviceCatalogPayload(f))});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo actualizar el servicio.';return false}
   renderServicesCatalog();return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly);wireServiceCatalogDupWarning(wrap,existingNames,s.name)
}
async function showArchivedServices(){
 const out=await req(api+'/service-catalog/archived');if(!out.r||!out.r.ok)return alert('No se pudieron cargar los servicios archivados.');
 const rows=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Servicios archivados</h1><p class="muted">Puedes reactivarlos para volver a usarlos en planes o vehículos.</p></div><button id="backServices" class="secondary">Volver a activos</button></div>`)+`<div class="list">${rows.length?rows.map(s=>card(`<div class="top"><div><h3>${esc(s.name)}</h3><p class="muted">${esc(s.category)}${s.defaultIntervalKm?` · ${fmt(s.defaultIntervalKm)} km`:''}</p></div><button class="reactcs" data-id="${s.id}">Reactivar</button></div>`)).join(''):card('<p class="muted">No hay servicios archivados.</p>')}</div>`;
 document.getElementById('backServices').onclick=()=>renderServicesCatalog();
 document.querySelectorAll('.reactcs').forEach(b=>b.onclick=async()=>{const x=await req(`${api}/service-catalog/${b.dataset.id}/reactivate`,{method:'PATCH'});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo reactivar.');renderServicesCatalog()});
}
