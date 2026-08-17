function reportVehicleDisplay(v){return `${v.plate}${v.internalNumber?` / ${v.internalNumber}`:''}`}
function resolveReportVehicleId(value){
 const q=String(value||'').trim().toLocaleLowerCase('es');if(!q)return null;
 const vehicles=window._reportVehicles||[];
 let v=vehicles.find(x=>reportVehicleDisplay(x).toLocaleLowerCase('es')===q);
 if(!v)v=vehicles.find(x=>String(x.plate||'').toLocaleLowerCase('es')===q||String(x.internalNumber||'').toLocaleLowerCase('es')===q);
 if(!v){const matches=vehicles.filter(x=>reportVehicleDisplay(x).toLocaleLowerCase('es').includes(q));if(matches.length===1)v=matches[0]}
 return v?.id||null
}
async function renderReports(vehicles){
 window._reportVehicles=vehicles;window._reportMode='history';
 const main=document.getElementById('main'),today=todayLocal(),d=new Date();d.setDate(d.getDate()-30);const off=d.getTimezoneOffset();const from=new Date(d.getTime()-off*60000).toISOString().slice(0,10);
 main.innerHTML=`<div class="report-page-head">${moduleTitle('reports','Reportes de mantenimiento','Consulta historial, últimos servicios o el estado actual de mantenimiento.')}</div>
 <div class="reporttabs"><button id="tabhistory" class="active">Historial de servicios</button><button id="tablatest" class="secondary">Últimos servicios</button><button id="tabstatus" class="secondary">Por estado</button></div>
 ${card(`<div class="report-filter-head"><div><h2>Filtros</h2><p class="muted" id="reportFilterHint">Selecciona periodo, vehículo y servicio.</p></div><button type="button" id="clearReportFilters" class="textbtn">Limpiar filtros</button></div><form id="reportfilters" class="reportfilters"><label class="datefilter">Desde<input type="date" name="from" value="${from}"></label><label class="datefilter">Hasta<input type="date" name="to" value="${today}"></label><label>Vehículo<input name="vehicle" list="reportVehicles" placeholder="Escribe placa o interno" autocomplete="off"><datalist id="reportVehicles">${vehicles.map(v=>`<option value="${esc(reportVehicleDisplay(v))}"></option>`).join('')}</datalist></label><label>Servicio<select name="service"><option value="">Todos los servicios</option></select></label><label class="statusfilter" style="display:none">Estado<select name="status"><option value="">Todos los estados</option><option value="OVERDUE">Vencidos</option><option value="DUE_SOON">Próximos</option><option value="UP_TO_DATE">Al día</option><option value="NO_BASELINE">Sin historial</option></select></label><div class="report-filter-action"><button type="submit">Consultar</button></div></form>`,'report-filter-card')}
 <div id="reportresult"></div>`;
 const form=document.getElementById('reportfilters');let reportMode='history';
 const run=()=>reportMode==='latest'?loadLatestServices(new FormData(form)):reportMode==='status'?loadStatusReport(new FormData(form)):loadReport(new FormData(form));
 const setMode=(mode)=>{
   reportMode=mode;window._reportMode=mode;
   const hist=document.getElementById('tabhistory'),latest=document.getElementById('tablatest'),status=document.getElementById('tabstatus'),dates=form.querySelectorAll('.datefilter'),statusFilter=form.querySelector('.statusfilter'),hint=document.getElementById('reportFilterHint');
   hist.className=mode==='history'?'active':'secondary';latest.className=mode==='latest'?'active':'secondary';status.className=mode==='status'?'active':'secondary';
   dates.forEach(x=>x.style.display=mode==='history'?'grid':'none');statusFilter.style.display=mode==='status'?'grid':'none';
   hint.textContent=mode==='history'?'Selecciona periodo, vehículo y servicio.':mode==='latest'?'Filtra por vehículo o servicio. Se mostrará únicamente la ejecución más reciente.':'Filtra el estado actual por vehículo, servicio o estado.';
   refreshReportServices(form);run()
 };
 form.onsubmit=e=>{e.preventDefault();run()};
 form.vehicle.oninput=()=>{clearTimeout(form._vehicleTimer);form._vehicleTimer=setTimeout(()=>{refreshReportServices(form);run()},250)};
 form.service.onchange=run;form.status.onchange=run;
 form.from.onchange=()=>{refreshReportServices(form);if(reportMode==='history')run()};
 form.to.onchange=()=>{refreshReportServices(form);if(reportMode==='history')run()};
 document.getElementById('tabhistory').onclick=()=>setMode('history');
 document.getElementById('tablatest').onclick=()=>setMode('latest');
 document.getElementById('tabstatus').onclick=()=>setMode('status');
 document.getElementById('clearReportFilters').onclick=()=>{form.vehicle.value='';form.service.value='';form.status.value='';form.from.value=from;form.to.value=today;refreshReportServices(form);run()};
 refreshReportServices(form);loadReport(new FormData(form));
}
async function refreshReportServices(form){
 const sel=form.querySelector('[name="service"]'),keep=sel.value;let names=[];
 if(window._reportMode==='status'){
   const out=await req(api+'/dashboard');if(!out.r||!out.r.ok)return;
   const vehicleId=resolveReportVehicleId(form.vehicle.value);
   names=[...new Set((out.j.data.priorities||[]).filter(x=>!vehicleId||x.vehicleId===vehicleId).map(x=>x.serviceName))].sort((x,y)=>x.localeCompare(y,'es'));
 }else{
   const q=new URLSearchParams({from:form.from.value,to:form.to.value}),vehicleId=resolveReportVehicleId(form.vehicle.value);if(vehicleId)q.set('vehicleId',vehicleId);
   const out=await req(`${api}/reports/maintenance?${q}`);if(!out.r||!out.r.ok)return;
   names=[...new Set((out.j.data.rows||[]).map(x=>x.serviceName))].sort((x,y)=>x.localeCompare(y,'es'));
 }
 sel.innerHTML='<option value="">Todos los servicios</option>'+names.map(n=>`<option value="${esc(n)}">${esc(n)}</option>`).join('');
 if(names.includes(keep))sel.value=keep;
}
function reportVehicleCell(x){return `<b>${esc(x.plate)}${x.internalNumber?` <span class="report-slash">/</span> ${esc(x.internalNumber)}`:''}</b><small>${esc(x.brand)} ${esc(x.model)}</small>`}
function reportEmpty(message){return `<div class="report-empty"><b>Sin resultados</b><small>${esc(message)}</small></div>`}
function wireReportExports(filename,headers,rows){
 const pb=document.getElementById('printreport');if(pb)pb.onclick=()=>window.print();
 const xb=document.getElementById('excelreport');if(xb)xb.onclick=()=>downloadExcel(filename,headers,rows)
}
function reportStatsBar(stats){return `<div class="report-overview">${stats.map(([label,value])=>`<article class="report-stat"><small>${esc(label)}</small><strong>${value}</strong></article>`).join('')}</div>`}
function reportResultHead(title,subtitle){return `<div class="sectionhead report-result-head"><div><h2>${esc(title)}</h2><p class="muted">${esc(subtitle)}</p></div><div class="report-export-actions"><button id="excelreport" class="secondary">Descargar Excel</button><button id="printreport" class="secondary">Imprimir / PDF</button></div></div>`}
function reportTable(headerCells,rows,emptyMessage){
 if(!rows.length)return reportEmpty(emptyMessage);
 return `<div class="reporttable"><div class="reportrow reportheader">${headerCells.map(h=>`<span>${esc(h)}</span>`).join('')}</div>${rows.join('')}</div>`
}
async function loadLatestServices(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando últimos servicios…</p>');
 const q=new URLSearchParams(),vehicleId=resolveReportVehicleId(f.get('vehicle'));if(vehicleId)q.set('vehicleId',vehicleId);if(f.get('service'))q.set('service',f.get('service'));
 const out=await req(`${api}/reports/latest-services?${q}`);if(!out.r||!out.r.ok){box.innerHTML=card('<p class="error">No se pudo cargar el reporte.</p>');return}
 let rows=out.j.data.rows||[];if(f.get('vehicle')&&!vehicleId){const text=String(f.get('vehicle')).toLocaleLowerCase('es');rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text))}
 const vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`${reportStatsBar([['Últimos servicios',rows.length],['Vehículos mostrados',vehicleCount]])}
 ${card(`${reportResultHead('Últimos servicios ejecutados','Una sola fila por servicio y vehículo: siempre la ejecución más reciente.')}${reportTable(['Fecha','Vehículo','Servicio','Último km','Técnico','Próximo'],rows.map(x=>`<div class="reportrow"><span class="report-date">${new Date(x.serviceDate).toLocaleDateString('es-CO')}</span><span data-label="Vehículo">${reportVehicleCell(x)}</span><span data-label="Servicio"><b>${esc(x.serviceName)}</b>${x.notes?`<small>${esc(x.notes)}</small>`:''}</span><span data-label="Último km"><b>${fmt(x.mileage)} km</b></span><span data-label="Técnico">${esc(x.technician||'—')}</span><span data-label="Próximo"><b>${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'—'}</b></span></div>`),'No hay servicios registrados con los filtros seleccionados.')}`)}`;
 wireReportExports('ultimos_servicios',['Fecha','Placa','Interno','Marca','Modelo','Servicio','Kilometraje','Técnico','Próximo'],rows.map(x=>[new Date(x.serviceDate).toLocaleDateString('es-CO'),x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,x.mileage,x.technician||'',x.nextDueMileage??(x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'')]))
}
async function loadReport(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando reporte…</p>');
 const q=new URLSearchParams({from:f.get('from'),to:f.get('to')}),vehicleId=resolveReportVehicleId(f.get('vehicle'));if(vehicleId)q.set('vehicleId',vehicleId);if(f.get('service'))q.set('service',f.get('service'));
 const out=await req(`${api}/reports/maintenance?${q}`);if(!out.r||!out.r.ok){box.innerHTML=card(`<p class="error">${esc(out.j.error?.message||'No se pudo cargar el reporte.')}</p>`);return}
 const d=out.j.data;let rows=d.rows||[];if(f.get('vehicle')&&!vehicleId){const text=String(f.get('vehicle')).toLocaleLowerCase('es');rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text))}
 const vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`<div class="report-overview"><article class="report-stat"><small>Servicios registrados</small><strong>${rows.length}</strong></article><article class="report-stat"><small>Vehículos mostrados</small><strong>${vehicleCount}</strong></article><article class="report-period"><small>Periodo consultado</small><b>${new Date(d.from).toLocaleDateString('es-CO')} — ${new Date(d.to).toLocaleDateString('es-CO')}</b></article></div>
 ${card(`${reportResultHead('Historial de servicios',rows.length?'Resultados del periodo y filtros seleccionados.':'No hay mantenimientos registrados con estos filtros.')}${reportTable(['Fecha','Vehículo','Servicio','Km','Técnico','Próximo'],rows.map(x=>`<div class="reportrow"><span class="report-date">${new Date(x.serviceDate).toLocaleDateString('es-CO')}</span><span data-label="Vehículo">${reportVehicleCell(x)}</span><span data-label="Servicio"><b>${esc(x.serviceName)}</b>${x.notes?`<small>${esc(x.notes)}</small>`:''}</span><span data-label="Km"><b>${fmt(x.mileage)} km</b></span><span data-label="Técnico">${esc(x.technician||'—')}</span><span data-label="Próximo"><b>${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'—'}</b></span></div>`),'Prueba ampliando el periodo o quitando algún filtro.')}`)}`;
 wireReportExports('historial_servicios',['Fecha','Placa','Interno','Marca','Modelo','Servicio','Kilometraje','Técnico','Próximo','Observaciones'],rows.map(x=>[new Date(x.serviceDate).toLocaleDateString('es-CO'),x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,x.mileage,x.technician||'',x.nextDueMileage??(x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):''),x.notes||'']))
}
async function loadStatusReport(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando estado actual…</p>');
 const out=await req(api+'/dashboard');if(!out.r||!out.r.ok){box.innerHTML=card('<p class="error">No se pudo cargar el reporte por estado.</p>');return}
 const vehicleId=resolveReportVehicleId(f.get('vehicle')),service=String(f.get('service')||''),status=String(f.get('status')||''),text=String(f.get('vehicle')||'').toLocaleLowerCase('es');
 let rows=(out.j.data.priorities||[]).filter(x=>(!vehicleId||x.vehicleId===vehicleId)&&(!service||x.serviceName===service)&&(!status||x.status===status));
 if(f.get('vehicle')&&!vehicleId)rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text));
 const labels={OVERDUE:'Vencido',DUE_SOON:'Próximo',UP_TO_DATE:'Al día',NO_BASELINE:'Sin historial'},vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`${reportStatsBar([['Servicios mostrados',rows.length],['Vehículos mostrados',vehicleCount]])}
 ${card(`<div class="sectionhead report-result-head"><div><h2>Estado actual de mantenimiento</h2><p class="muted">Situación actual de cada servicio según kilometraje e historial.</p></div><div class="report-export-actions"><button id="excelreport" class="secondary">Descargar Excel</button><button id="printreport" class="secondary">Imprimir / PDF</button></div></div>${rows.length?`<div class="reporttable status-report-table"><div class="reportrow status-report-row reportheader"><span>Vehículo</span><span>Servicio</span><span>Estado</span><span>Actual</span><span>Próximo</span><span>Distancia</span></div>${rows.map(x=>`<div class="reportrow status-report-row"><span data-label="Vehículo">${reportVehicleCell(x)}</span><span data-label="Servicio"><b>${esc(x.serviceName)}</b></span><span data-label="Estado">${statusBadge(x.status)}</span><span data-label="Actual"><b>${fmt(x.currentMileage)} km</b></span><span data-label="Próximo">${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:'—'}</span><span data-label="Distancia">${x.remainingKm==null?'—':x.remainingKm<0?`${fmt(Math.abs(x.remainingKm))} km vencido`:`Faltan ${fmt(x.remainingKm)} km`}</span></div>`).join('')}</div>`:reportEmpty('No hay servicios con el estado y filtros seleccionados.')}`)}`;
 wireReportExports('reporte_por_estado',['Placa','Interno','Marca','Modelo','Servicio','Estado','Km actual','Próximo km','Distancia'],rows.map(x=>[x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,labels[x.status]||x.status,x.currentMileage,x.nextDueMileage??'',x.remainingKm??'']))
}
