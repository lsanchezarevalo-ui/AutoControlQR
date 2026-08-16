const api='/api/v1';
const app=document.getElementById('app');
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
const fmt=n=>Number(n).toLocaleString('es-CO');
const token=()=>localStorage.getItem('token');
function card(x,cls=''){return `<section class="card ${cls}">${x}</section>`}
async function req(url,opt={}){const headers={...(opt.headers||{})};const hadToken=!!token();if(hadToken)headers.Authorization='Bearer '+token();try{const r=await fetch(url,{...opt,headers,cache:'no-store'});let j={};try{j=await r.json()}catch{}if(r.status===401&&hadToken){localStorage.clear();loginView('La sesión expiró. Vuelve a iniciar sesión.')}return{r,j,networkError:null}}catch(e){return{r:null,j:{},networkError:e}}}
function uiIcon(type,cls=''){
 const paths={
  dashboard:'<path d="M3 11.5 12 4l9 7.5"/><path d="M5 10.5V20h5v-6h4v6h5v-9.5"/>',
  vehicle:'<path d="M5 17h14l-1-6-2-3H8l-2 3-1 6Z"/><circle cx="7.5" cy="17.5" r="1.5"/><circle cx="16.5" cy="17.5" r="1.5"/><path d="M7 12h10"/>',
  plans:'<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M9 3v4h6V3M9 11h6M9 15h6"/>',
  reports:'<path d="M5 20V10M10 20V4M15 20v-7M20 20V7"/>',
  users:'<circle cx="9" cy="8" r="3"/><path d="M3 20c0-4 2.5-6 6-6s6 2 6 6"/><circle cx="17" cy="9" r="2"/><path d="M15 15c3.5 0 6 1.5 6 5"/>',
  settings:'<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3V2.8h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z"/>',
  oil:'<path d="M4 10h10l3 3v5H7l-3-3v-5Z"/><path d="M9 10V7h4l2 3M18 9h3M20 7v4"/>',
  transmission:'<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2"/>',
  differential:'<circle cx="5" cy="12" r="2"/><circle cx="19" cy="12" r="2"/><path d="M7 12h10M12 7v10M9 7h6M9 17h6"/>',
  coolant:'<path d="M10 4v10.5a4 4 0 1 0 4 0V4a2 2 0 0 0-4 0Z"/><path d="M12 8v8"/>',
  brake:'<circle cx="12" cy="12" r="6"/><path d="M4 7c-2 3-2 7 0 10M20 7c2 3 2 7 0 10"/>',
  filter:'<path d="M4 5h16l-6 7v6l-4 2v-8L4 5Z"/>',
  belt:'<path d="M5 8c4-4 10-4 14 0M5 16c4 4 10 4 14 0"/><circle cx="5" cy="12" r="4"/><circle cx="19" cy="12" r="4"/>',
  spark:'<path d="m13 2-7 11h6l-1 9 7-12h-6l1-8Z"/>',
  service:'<path d="m14 6 4-4 4 4-4 4"/><path d="M18 2v8M4 20l7-7"/><circle cx="8" cy="16" r="4"/>',
  check:'<circle cx="12" cy="12" r="9"/><path d="m8 12 2.5 2.5L16 9"/>',
  alert:'<path d="M12 3 2.5 20h19L12 3Z"/><path d="M12 9v5M12 17h.01"/>',
  gauge:'<path d="M4 17a8 8 0 1 1 16 0"/><path d="m12 13 4-4M7 17h10"/>',
  search:'<circle cx="10.5" cy="10.5" r="6.5"/><path d="m15.5 15.5 5 5"/>'
 };
 return `<svg class="ui-icon ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[type]||paths.service}</svg>`
}
function serviceIconType(name){
 const n=String(name||'').toLocaleLowerCase('es');
 if(n.includes('transmis'))return 'transmission';if(n.includes('diferencial'))return 'differential';
 if(n.includes('refrig')||n.includes('coolant'))return 'coolant';if(n.includes('freno'))return 'brake';
 if(n.includes('filtro'))return 'filter';if(n.includes('correa'))return 'belt';if(n.includes('buj'))return 'spark';
 if(n.includes('motor')||n.includes('aceite'))return 'oil';return 'service'
}
function serviceIcon(name){return uiIcon(serviceIconType(name))}
function serviceNameWithIcon(name){return `<span class="service-icon" aria-hidden="true">${serviceIcon(name)}</span><span>${esc(name)}</span>`}

function statusBadge(s){const map={UP_TO_DATE:['🟢','Al día','good'],DUE_SOON:['🟡','Próximo','warn'],OVERDUE:['🔴','Vencido','bad'],NO_BASELINE:['⚫','Sin historial','mutedb'],NO_PLAN:['⚪','Sin plan','mutedb']};const x=map[s]||['⚪',s,'mutedb'];return `<span class="badge ${x[2]}">${x[0]} ${x[1]}</span>`}


function todayLocal(){const d=new Date();const off=d.getTimezoneOffset();return new Date(d.getTime()-off*60000).toISOString().slice(0,10)}
function openFormModal({title,body,onSubmit}){
 const wrap=document.createElement('div');wrap.className='modalbackdrop';
 wrap.innerHTML=`<div class="modalcard"><div class="modalhead"><h2>${esc(title)}</h2><button type="button" class="iconbtn closemodal" aria-label="Cerrar">×</button></div><form class="modalform">${body}<div class="modalactions"><button type="button" class="secondary closemodal">Cancelar</button><button type="submit">Guardar</button></div><p class="error modalerr"></p></form></div>`;
 document.body.appendChild(wrap);document.body.classList.add('modal-open');
 const closeModal=()=>{wrap.remove();document.body.classList.remove('modal-open')};
 wrap.querySelectorAll('.closemodal').forEach(b=>b.onclick=closeModal);
 wrap.onclick=e=>{if(e.target===wrap)closeModal()};
 wrap.querySelector('.modalform').onsubmit=async e=>{e.preventDefault();const err=wrap.querySelector('.modalerr');err.textContent='';const ok=await onSubmit(new FormData(e.target),err);if(ok)closeModal()};
 return wrap;
}
function dateField(name,label,required=true){
 const t=todayLocal();
 return `<label>${label}<div class="datepick"><input name="${name}" type="date" value="${t}" ${required?'required':''}><button class="secondary todaybtn" type="button">Hoy</button></div></label>`;
}
function wireToday(wrap,name){const b=wrap.querySelector('.todaybtn');if(b)b.onclick=()=>{wrap.querySelector(`[name="${name}"]`).value=todayLocal()}}

function loginView(message=''){
 app.innerHTML=card(`<div class="eyebrow">AUTOCONTROL QR · V31.6</div><h1>Iniciar sesión</h1>${message?`<p class="error">${esc(message)}</p>`:''}<form id="login"><label>Correo<input name="email" type="email" autocomplete="username" placeholder="correo@empresa.com"></label><label>Contraseña<input name="password" type="password" autocomplete="current-password" placeholder="Contraseña"></label><button id="loginBtn">Ingresar</button><button type="button" id="forgotPassword" class="textbtn login-help">¿Olvidaste tu contraseña?</button><div id="forgotInfo" class="login-forgot-info" hidden><strong>Recuperación de acceso</strong><p>Si eres Técnico, solicita el restablecimiento al Administrador de tu empresa. Si eres Administrador de Empresa, contacta al Administrador General de AutoControl QR.</p></div><p id="err" class="error"></p></form>`,'narrow');
 const forgot=document.getElementById('forgotPassword');if(forgot)forgot.onclick=()=>{const box=document.getElementById('forgotInfo');box.hidden=!box.hidden};
 document.getElementById('login').onsubmit=async e=>{e.preventDefault();const btn=document.getElementById('loginBtn');btn.disabled=true;btn.textContent='Ingresando…';const f=new FormData(e.target);const out=await req(api+'/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:f.get('email'),password:f.get('password')})});if(!out.r||!out.r.ok){document.getElementById('err').textContent='No fue posible iniciar sesión.';btn.disabled=false;btn.textContent='Ingresar';return}localStorage.setItem('token',out.j.data.accessToken);localStorage.setItem('user',JSON.stringify(out.j.data.user));adminShell('dashboard')}
}

function changeOwnPasswordModal(){
 openFormModal({
  title:'Cambiar mi contraseña',
  body:`<label>Contraseña actual<input name="current" type="password" autocomplete="current-password" required></label><label>Nueva contraseña<input name="password" type="password" minlength="8" autocomplete="new-password" placeholder="Mínimo 8 caracteres" required></label><label>Confirmar nueva contraseña<input name="confirm" type="password" minlength="8" autocomplete="new-password" required></label>`,
  onSubmit:async(f,err)=>{
   const current=String(f.get('current')||''),password=String(f.get('password')||''),confirmPassword=String(f.get('confirm')||'');
   if(password.length<8){err.textContent='La nueva contraseña debe tener mínimo 8 caracteres.';return false}
   if(password!==confirmPassword){err.textContent='Las contraseñas nuevas no coinciden.';return false}
   const out=await req(api+'/auth/password',{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({currentPassword:current,newPassword:password})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo cambiar la contraseña.';return false}
   alert('Contraseña actualizada correctamente.');return true
  }
 })
}

async function adminShell(view='dashboard'){
 const me=JSON.parse(localStorage.getItem('user')||'null')||{};
 if(me.role==='TECHNICIAN')return technicianShell();
 if(me.role==='PLATFORM_ADMIN')return platformShell();
 const [vr,pr,cr]=await Promise.all([req(api+'/vehicles'),req(api+'/maintenance-plans'),req(api+'/company')]);
 if(!vr.r||vr.r.status===401){localStorage.clear();return loginView('La sesión expiró.')} if(!vr.r.ok)return loginView('La API no pudo cargar los vehículos.');
 const vehicles=vr.j.data||[],plans=pr.r&&pr.r.ok?(pr.j.data||[]):[],company=cr.r&&cr.r.ok?cr.j.data:{name:'Empresa',logoDataUrl:null};
 const nav=[
  ['dashboard','dashboard','Centro de Control'],['vehicles','vehicle','Vehículos'],['plans','plans','Planes de Mantenimiento'],
  ['reports','reports','Reportes'],['users','users','Usuarios'],['notifications','alert','Notificaciones'],['settings','settings','Empresa']
 ];
 app.innerHTML=`<div class="app-layout"><aside class="sidebar">
   <div class="sidebar-brand"><div class="sidebar-mark">${uiIcon('vehicle')}</div><div><strong>AUTOCONTROL QR</strong><small>Control de mantenimiento</small></div></div>
   <nav class="sidebar-nav">${nav.map(x=>`<button data-view="${x[0]}" class="sidebtn ${view===x[0]?'active':''}">${uiIcon(x[1])}<span>${x[2]}</span></button>`).join('')}</nav>
   <div class="sidebar-bottom"><button id="logout" class="sidebtn">${uiIcon('settings')}<span>Salir</span></button><small>v31.6</small></div>
 </aside>
 <div class="app-workspace"><header class="topbar"><button type="button" id="mobileMenu" class="mobile-menu-btn" aria-label="Abrir menú">☰</button><div class="top-company">${company.logoDataUrl?`<div class="top-company-logo"><img src="${esc(company.logoDataUrl)}" alt=""></div>`:`<div class="top-company-logo fallback">${esc(String(company.name||'E').charAt(0).toUpperCase())}</div>`}<strong>${esc(company.name)}</strong></div><button type="button" id="myPassword" class="top-account-btn">Mi cuenta</button><div class="top-user"><span class="top-avatar">${esc(String(me.fullName||me.name||'A').charAt(0).toUpperCase())}</span><div><strong>${esc(me.fullName||me.name||'Usuario')}</strong><small>Administrador</small></div></div></header><main id="main"></main></div></div>`;
 document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>adminShell(b.dataset.view));document.getElementById('logout').onclick=()=>{localStorage.clear();loginView()};const myPassword=document.getElementById('myPassword');if(myPassword)myPassword.onclick=changeOwnPasswordModal;const mobileMenu=document.getElementById('mobileMenu'),sidebar=document.querySelector('.sidebar');if(mobileMenu&&sidebar){mobileMenu.onclick=()=>sidebar.classList.toggle('mobile-open');document.addEventListener('click',e=>{if(innerWidth<=800&&sidebar.classList.contains('mobile-open')&&!sidebar.contains(e.target)&&e.target!==mobileMenu)sidebar.classList.remove('mobile-open')},{once:true})};
 if(view==='dashboard')renderDashboard(vehicles);else if(view==='plans')renderPlans(plans,vehicles);else if(view==='reports')renderReports(vehicles);else if(view==='users')renderUsers();else if(view==='notifications')renderNotifications();else if(view==='settings')renderCompanySettings();else renderVehicles(vehicles,plans,true)
}

async function platformShell(){
 const me=JSON.parse(localStorage.getItem('user')||'null')||{},out=await req(api+'/platform/companies');
 if(!out.r||out.r.status===401){localStorage.clear();return loginView('La sesión expiró.')}
 if(!out.r.ok)return loginView('No se pudo abrir la administración de empresas.');
 const companies=out.j.data||[];
 app.innerHTML=`<div class="shell"><header><div><div class="eyebrow">AUTOCONTROL QR · V31.6</div><strong>Administración de plataforma</strong><small class="roleline">${esc(me.fullName||'Administrador')}</small></div><nav class="platform-head-actions"><button id="platformMyPassword" class="secondary">Mi cuenta</button><button id="logout" class="secondary">Salir</button></nav></header><main id="main"></main></div>`;
 document.getElementById('logout').onclick=()=>{localStorage.clear();loginView()};document.getElementById('platformMyPassword').onclick=changeOwnPasswordModal;renderPlatformCompanies(companies);
}
function renderPlatformCompanies(companies){
 const main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Empresas</h1><p class="muted">Vista general de las empresas registradas en AutoControl QR.</p></div></div>
 <form id="createcompany" class="companyform"><label>Empresa<input name="name" placeholder="Nombre de la empresa" required></label><label>Administrador<input name="adminName" placeholder="Nombre completo" required></label><label>Correo administrador<input name="adminEmail" type="email" placeholder="admin@empresa.com" required></label><label>Contraseña inicial<input name="password" type="password" minlength="8" required></label><button>Crear empresa</button></form><p id="companyerr" class="error"></p>`)
 +`<div class="companygrid">${companies.map(c=>`<article class="companycard"><div><span class="badge ${c.status==='ACTIVE'?'good':'mutedb'}">${c.status==='ACTIVE'?'Activa':'Inactiva'}</span><h2>${esc(c.name)}</h2><p class="muted">${c.code?`Código ${esc(c.code)}`:'Sin código'}</p></div><div class="companynumbers"><span><b>${c.vehicles}</b><small>Vehículos</small></span><span><b>${c.activeUsers}</b><small>Usuarios activos</small></span></div><div class="platform-actions"><button class="viewcompany" data-id="${c.id}">Ver empresa</button><button class="secondary companystatus" data-id="${c.id}" data-status="${c.status==='ACTIVE'?'INACTIVE':'ACTIVE'}">${c.status==='ACTIVE'?'Desactivar':'Activar'}</button></div></article>`).join('')}</div>`;
 document.getElementById('createcompany').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const out=await req(api+'/platform/companies',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),adminName:f.get('adminName'),adminEmail:f.get('adminEmail'),adminPassword:f.get('password')})});if(!out.r||!out.r.ok){document.getElementById('companyerr').textContent=out.j.error?.message||'No se pudo crear la empresa.';return}platformShell()};
 document.querySelectorAll('.viewcompany').forEach(b=>b.onclick=()=>renderPlatformCompanyDetail(b.dataset.id));
 document.querySelectorAll('.companystatus').forEach(b=>b.onclick=async()=>{if(b.dataset.status==='INACTIVE'&&!confirm('¿Desactivar esta empresa? Sus usuarios dejarán de poder iniciar sesión.'))return;const out=await req(`${api}/platform/companies/${b.dataset.id}/status`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({status:b.dataset.status})});if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo cambiar el estado.');platformShell()});
}
async function renderPlatformCompanyDetail(id){
 const main=document.getElementById('main');main.innerHTML=card('<p class="muted">Cargando empresa…</p>');
 const out=await req(`${api}/platform/companies/${id}`);
 if(!out.r||!out.r.ok){main.innerHTML=card(`<button id="backcompanies" class="secondary">← Empresas</button><p class="error">No se pudo cargar la empresa.</p>`);document.getElementById('backcompanies').onclick=platformShell;return}
 const d=out.j.data,c=d.company,users=d.users||[],vehicles=d.vehicles||[];
 const roleLabel=r=>r==='COMPANY_ADMIN'?'Administrador':r==='TECHNICIAN'?'Técnico':r;
 main.innerHTML=`<section class="platform-detail"><div class="platform-detail-head"><button id="backcompanies" class="secondary">← Empresas</button><div><span class="badge ${c.status==='ACTIVE'?'good':'mutedb'}">${c.status==='ACTIVE'?'Activa':'Inactiva'}</span><h1>${esc(c.name)}</h1><p class="muted">${c.code?`Código ${esc(c.code)}`:''}</p></div><div class="companynumbers"><span><b>${c.vehicles}</b><small>Vehículos activos</small></span><span><b>${c.activeUsers}</b><small>Usuarios activos</small></span></div></div>
 ${card(`<div class="top"><div><h2>Usuarios</h2><p class="muted">Consulta de usuarios de esta empresa. Los cambios se realizan desde el administrador de la empresa.</p></div></div><div class="platform-table-wrap"><table class="platform-table"><thead><tr><th>Nombre</th><th>Correo</th><th>Rol</th><th>Estado</th></tr></thead><tbody>${users.length?users.map(u=>`<tr><td>${esc(u.fullName)}</td><td>${esc(u.email)}</td><td>${esc(roleLabel(u.role))}</td><td><span class="badge ${u.status==='ACTIVE'?'good':'mutedb'}">${u.status==='ACTIVE'?'Activo':'Inactivo'}</span>${u.role==='COMPANY_ADMIN'?`<button class="textbtn platform-reset-admin" data-company="${c.id}" data-user="${u.id}" data-name="${esc(u.fullName)}">Restablecer contraseña</button>`:''}</td></tr>`).join(''):`<tr><td colspan="4" class="muted">No hay usuarios registrados.</td></tr>`}</tbody></table></div>`)}
 ${card(`<div class="top"><div><h2>Vehículos</h2><p class="muted">Vista de consulta de los vehículos registrados por la empresa.</p></div></div><div class="platform-table-wrap"><table class="platform-table"><thead><tr><th>Placa</th><th>Interno</th><th>Vehículo</th><th>Kilometraje</th><th>Estado</th></tr></thead><tbody>${vehicles.length?vehicles.map(v=>`<tr><td><b>${esc(v.plate)}</b></td><td>${esc(v.internalNumber||'—')}</td><td>${esc(v.brand)} ${esc(v.model)}${v.variant?` · ${esc(v.variant)}`:''}</td><td>${fmt(v.currentMileage)} km</td><td><span class="badge ${v.status==='ACTIVE'?'good':'mutedb'}">${v.status==='ACTIVE'?'Activo':'Archivado'}</span></td></tr>`).join(''):`<tr><td colspan="5" class="muted">No hay vehículos registrados.</td></tr>`}</tbody></table></div>`)}</section>`;
 document.getElementById('backcompanies').onclick=platformShell;
 document.querySelectorAll('.platform-reset-admin').forEach(b=>b.onclick=()=>platformResetAdminPassword(b.dataset.company,b.dataset.user,b.dataset.name));
}
function platformResetAdminPassword(companyId,userId,name){
 openFormModal({
  title:`Restablecer contraseña · ${name}`,
  body:`<p class="muted">Esta acción cambia únicamente la contraseña del Administrador de Empresa seleccionado. Los datos de la empresa no se modifican.</p><label>Nueva contraseña<input name="password" type="password" minlength="8" autocomplete="new-password" required></label><label>Confirmar contraseña<input name="confirm" type="password" minlength="8" autocomplete="new-password" required></label>`,
  onSubmit:async(f,err)=>{
   const password=String(f.get('password')||''),confirmPassword=String(f.get('confirm')||'');
   if(password.length<8){err.textContent='La contraseña debe tener mínimo 8 caracteres.';return false}
   if(password!==confirmPassword){err.textContent='Las contraseñas no coinciden.';return false}
   const out=await req(`${api}/platform/companies/${companyId}/admins/${userId}/password`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({password})});
   if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo restablecer la contraseña.';return false}
   alert('Contraseña restablecida correctamente.');return true
  }
 })
}

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
 if(!('BarcodeDetector' in window)){box.innerHTML='<p class="warntext">Este navegador no tiene lector QR integrado. Usa la búsqueda por placa/interno o pega el código QR abajo.</p>';return}
 let stream;
 try{
  stream=await navigator.mediaDevices.getUserMedia({video:{facingMode:{ideal:'environment'}}});
  box.innerHTML='<video id="qrvideo" autoplay playsinline></video><p class="muted">Apunta la cámara al QR del vehículo.</p><button id="stopscan" class="secondary">Cancelar cámara</button>';
  const video=document.getElementById('qrvideo');video.srcObject=stream;const detector=new BarcodeDetector({formats:['qr_code']});let active=true;
  document.getElementById('stopscan').onclick=()=>{active=false;stream.getTracks().forEach(t=>t.stop());box.innerHTML=''};
  const scan=async()=>{if(!active)return;try{const codes=await detector.detect(video);if(codes.length){active=false;stream.getTracks().forEach(t=>t.stop());return selectTechnicianQr(codes[0].rawValue)}}catch{}requestAnimationFrame(scan)};requestAnimationFrame(scan);
 }catch(e){box.innerHTML='<p class="error">No se pudo abrir la cámara. Revisa el permiso del navegador o usa la búsqueda manual.</p>'}
}
function technicianVehicleHome(v){
 const main=document.getElementById('main');
 main.innerHTML=`<div class="tech-selected-label"><span>Vehículo seleccionado</span><b>Solo puedes trabajar sobre esta unidad</b></div>${card(`<div class="techwork"><div class="tech-vehicle-info"><div class="eyebrow">VEHÍCULO EN TRABAJO</div><h1>${esc(v.plate)}${v.internalNumber?` <span class="tech-slash">/</span> ${esc(v.internalNumber)}`:''}</h1><p>${esc(v.brand)} ${esc(v.model)}</p><div class="tech-km-label">Kilometraje actual</div><div class="vehicle-km">${fmt(v.currentMileage)} <small>km</small></div></div><div class="techworkactions"><button id="workstatus">Estado y registrar mantenimiento</button><button id="workhistory" class="secondary">Ver historial</button></div></div>`)}
 <p class="techlock"><b>Acceso limitado:</b> esta sesión solo permite consultar y registrar información de ${esc(v.plate)}. Usa “Cambiar vehículo” para trabajar sobre otra unidad.</p>`;
 document.getElementById('workstatus').onclick=()=>maintenanceView(v.id);document.getElementById('workhistory').onclick=()=>maintenanceHistory(v.id);
}


async function renderNotifications(){
 const main=document.getElementById('main');main.innerHTML=card(`${moduleTitle('alert','Notificaciones','Controla cómo AutoControl QR te avisa sobre mantenimientos próximos y vencidos.')}<p class="muted">Cargando…</p>`);
 const [cr,nr,hr]=await Promise.all([req(api+'/company'),req(api+'/notifications'),req(api+'/notifications/history')]);
 if(!cr.r?.ok||!nr.r?.ok){main.innerHTML=card(`${moduleTitle('alert','Notificaciones','Alertas de mantenimiento.')}<p class="error">No se pudo cargar la configuración.</p>`);return}
 const c=cr.j.data,items=nr.j.data||[],history=hr.r?.ok?(hr.j.data||[]):[];
 const active=items.filter(x=>(x.status==='OVERDUE'&&c.notificationOverdue)||(x.status==='DUE_SOON'&&c.notificationDueSoon));
 main.innerHTML=`<div class="notification-head">${moduleTitle('alert','Notificaciones','Alertas de mantenimiento próximas y vencidas.')}</div>
 ${card(`<div class="notification-settings-head"><div><h2>Configuración</h2><p class="muted">El administrador decide qué alertas utilizar y con qué frecuencia.</p></div><label class="switchline"><input id="notifyEnabled" type="checkbox" ${c.notificationEnabled?'checked':''}> Activar notificaciones</label></div>
 <form id="notificationSettings" class="notification-settings-grid">
  <label><span>Alertas internas</span><select name="internal"><option value="true" ${c.notificationInternal?'selected':''}>Activadas</option><option value="false" ${!c.notificationInternal?'selected':''}>Desactivadas</option></select></label>
  <label><span>Correo electrónico</span><select name="emailEnabled"><option value="false" ${!c.notificationEmailEnabled?'selected':''}>Desactivado</option><option value="true" ${c.notificationEmailEnabled?'selected':''}>Preparar correo</option></select></label>
  <label><span>Correo destinatario</span><input name="email" type="email" value="${esc(c.notificationEmail||c.email||'')}" placeholder="mantenimiento@empresa.com"></label>
  <label><span>Recordar vencidos cada</span><select name="repeatDays">${[3,7,15,30].map(x=>`<option value="${x}" ${c.notificationRepeatDays===x?'selected':''}>${x} días</option>`).join('')}</select></label>
  <label class="checkline"><input name="dueSoon" type="checkbox" ${c.notificationDueSoon?'checked':''}> Avisar cuando esté Próximo</label>
  <label class="checkline"><input name="overdue" type="checkbox" ${c.notificationOverdue?'checked':''}> Avisar cuando esté Vencido</label>
  <div class="notification-save"><button>Guardar configuración</button><span id="notifyMsg"></span></div>
 </form>`,'notification-settings-card')}
 <section class="notification-grid">
  ${card(`<div class="sectionhead"><div><h2>Alertas actuales</h2><p class="muted">${active.length} servicio${active.length===1?'':'s'} requiere${active.length===1?'':'n'} atención.</p></div></div><div class="notification-list">${active.length?active.map(x=>`<div class="notification-row"><span class="notification-service-icon">${serviceIcon(x.serviceName)}</span><span><small>${x.internalNumber?`Interno ${esc(x.internalNumber)}`:''}</small><b>${esc(x.plate)}</b><em>${esc(x.serviceName)}</em></span><span>${statusBadge(x.status)}<small>${x.nextDueMileage!=null?`Programado: ${fmt(x.nextDueMileage)} km`:''}</small></span><button class="secondary notifyRecord" data-v="${x.vehicleId}" data-s="${x.serviceId}" data-st="${x.status}">Marcar avisado</button></div>`).join(''):'<div class="report-empty"><b>Sin alertas pendientes</b><small>No hay servicios próximos o vencidos según la configuración actual.</small></div>'}</div>`)}
  ${card(`<div class="sectionhead"><div><h2>Historial</h2><p class="muted">Últimos avisos registrados.</p></div></div><div class="notification-history">${history.length?history.slice(0,20).map(x=>`<div><span><b>${esc(x.plate)}</b><small>${esc(x.serviceName)}</small></span><span>${statusBadge(x.status)}</span><span><b>${x.channel==='EMAIL'?'Correo':'Interna'}</b><small>${new Date(x.createdAt).toLocaleString('es-CO')}</small></span></div>`).join(''):'<p class="muted cc-empty">Todavía no hay avisos registrados.</p>'}</div>`)}
 </section>
 <div class="notification-note"><b>Correo automático:</b> la configuración queda preparada en esta versión. Para enviar correos reales necesitaremos conectar un proveedor de correo transaccional. WhatsApp queda reservado como siguiente canal.</div>`;
 document.getElementById('notificationSettings').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target),payload={enabled:document.getElementById('notifyEnabled').checked,internal:f.get('internal')==='true',emailEnabled:f.get('emailEnabled')==='true',email:String(f.get('email')||''),dueSoon:f.get('dueSoon')==='on',overdue:f.get('overdue')==='on',repeatDays:Number(f.get('repeatDays'))};const r=await req(api+'/company/notifications',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});const m=document.getElementById('notifyMsg');m.className=r.r?.ok?'success':'error';m.textContent=r.r?.ok?'Configuración guardada.':'No se pudo guardar.'};
 document.querySelectorAll('.notifyRecord').forEach(b=>b.onclick=async()=>{const r=await req(api+'/notifications/mark-sent',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({vehicleId:b.dataset.v,serviceId:b.dataset.s,status:b.dataset.st,channel:'INTERNAL',recipient:null})});if(r.r?.ok)renderNotifications()})
}
async function renderCompanySettings(){
 const main=document.getElementById('main'),out=await req(api+'/company');
 if(!out.r||!out.r.ok){main.innerHTML=card('<p class="error">No se pudo cargar la empresa.</p>');return}
 const c=out.j.data;
 main.innerHTML=`<div class="settingshead">${moduleTitle('settings',c.name,`Código interno: ${c.code} · generado automáticamente por AutoControl QR.`)}</div>
 ${card(`<h2>Datos de la empresa</h2><p class="muted">Estos datos identifican a tu empresa dentro de AutoControl QR y podrán utilizarse posteriormente en documentos y reportes.</p><form id="companysettings" class="settingsgrid"><label>Nombre comercial<input name="name" value="${esc(c.name||'')}" required></label><label>Razón social<input name="legalName" value="${esc(c.legalName||'')}" placeholder="Opcional"></label><label>NIT / identificación<input name="taxId" value="${esc(c.taxId||'')}" placeholder="Opcional"></label><label>Teléfono<input name="phone" value="${esc(c.phone||'')}" placeholder="Opcional"></label><label>Correo de empresa<input name="email" type="email" value="${esc(c.email||'')}" placeholder="Opcional"></label><label class="wide">Dirección<input name="address" value="${esc(c.address||'')}" placeholder="Opcional"></label><label class="wide">Logo de la empresa<input id="companylogo" type="file" accept="image/png,image/jpeg,image/webp"><small class="muted">PNG, JPG o WebP. Recomendado: logo horizontal y fondo claro.</small></label>${c.logoDataUrl?`<div class="wide logopreview"><img src="${esc(c.logoDataUrl)}" alt="Logo actual"><button type="button" id="removelogo" class="secondary">Quitar logo</button></div>`:''}<div class="wide"><button>Guardar cambios</button><span id="settingsmsg"></span></div></form>`)}`;
 window._companyLogoData=undefined;const logoInput=document.getElementById('companylogo');logoInput.onchange=()=>{const file=logoInput.files[0];if(!file)return;if(file.size>700000){alert('El logo es demasiado grande. Usa una imagen menor de 700 KB.');logoInput.value='';return}const rd=new FileReader();rd.onload=()=>window._companyLogoData=rd.result;rd.onerror=()=>{alert('No se pudo leer el archivo de imagen.');logoInput.value=''};rd.readAsDataURL(file)};const removeLogo=document.getElementById('removelogo');if(removeLogo)removeLogo.onclick=()=>{window._companyLogoData=null;removeLogo.closest('.logopreview').innerHTML='<span class="muted">El logo se eliminará al guardar.</span>'};
 document.getElementById('companysettings').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target),msg=document.getElementById('settingsmsg');msg.textContent='Guardando…';const x=await req(api+'/company',{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),legalName:f.get('legalName')||null,taxId:f.get('taxId')||null,phone:f.get('phone')||null,email:f.get('email')||null,address:f.get('address')||null,logoDataUrl:window._companyLogoData===undefined?(c.logoDataUrl||null):window._companyLogoData})});if(!x.r||!x.r.ok){msg.className='error';msg.textContent=x.j.error?.message||'No se pudo guardar.';return}msg.className='success';msg.textContent='Cambios guardados.';setTimeout(()=>adminShell('settings'),500)};
}

async function renderUsers(){
 const main=document.getElementById('main'),out=await req(api+'/users');
 if(!out.r||!out.r.ok){main.innerHTML=card('<p class="error">No se pudieron cargar los usuarios.</p>');return}
 const users=out.j.data||[];
 const userCard=u=>`<article class="user-card"><div class="user-identity"><div class="user-avatar">${esc((u.fullName||'?').trim().charAt(0).toUpperCase())}</div><div><strong>${esc(u.fullName)}</strong><small>${esc(u.email)}</small><div class="user-tags"><span class="rolepill">${u.role==='COMPANY_ADMIN'?'Administrador':'Técnico'}</span><span class="badge ${u.status==='ACTIVE'?'good':'mutedb'}">${u.status==='ACTIVE'?'Activo':'Inactivo'}</span></div></div></div><div class="user-actions"><button class="secondary edituser" data-id="${u.id}">Editar</button><button class="secondary userpassword" data-id="${u.id}">Contraseña</button><button class="textbtn ${u.status==='ACTIVE'?'dangertext':''} userstatus" data-id="${u.id}" data-status="${u.status==='ACTIVE'?'INACTIVE':'ACTIVE'}">${u.status==='ACTIVE'?'Desactivar':'Activar'}</button></div></article>`;
 main.innerHTML=card(`<div class="top user-page-head">${moduleTitle('users','Usuarios','Administra quién puede entrar a AutoControl QR y qué función cumple.')}</div><div class="user-create-title"><h2>Nuevo usuario</h2><p class="muted">Los técnicos trabajan únicamente sobre el vehículo que seleccionan o escanean. Los administradores gestionan la empresa.</p></div><form id="createuser" class="user-create-grid"><label>Nombre completo<input name="name" placeholder="Nombre y apellido" required></label><label>Correo<input name="email" type="email" placeholder="usuario@empresa.com" required></label><label>Contraseña<input name="password" type="password" minlength="8" placeholder="Mínimo 8 caracteres" required></label><label>Rol<select name="role"><option value="TECHNICIAN">Técnico</option><option value="COMPANY_ADMIN">Administrador</option></select></label><div class="user-create-action"><button>Crear usuario</button></div></form><p id="usererr" class="error"></p>`)+`<div class="user-list-head"><h2><span id="userCount">${users.length}</span> usuario${users.length===1?'':'s'}</h2><div class="user-search"><input id="userSearch" type="search" placeholder="Buscar por nombre o correo" aria-label="Buscar por nombre o correo" autocomplete="off"></div></div><div id="userList" class="user-list">${users.length?users.map(userCard).join(''):'<div class="no-search-results">Todavía no hay usuarios.</div>'}</div>`;
 document.getElementById('createuser').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const x=await req(api+'/users',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({fullName:f.get('name'),email:f.get('email'),password:f.get('password'),role:f.get('role')})});if(!x.r||!x.r.ok){document.getElementById('usererr').textContent=x.j.error?.message||'No se pudo crear el usuario.';return}renderUsers()};
 const wire=()=>{
  document.querySelectorAll('.edituser').forEach(b=>b.onclick=()=>editUserModal(users.find(x=>x.id===b.dataset.id)));
  document.querySelectorAll('.userpassword').forEach(b=>b.onclick=()=>passwordUserModal(users.find(x=>x.id===b.dataset.id)));
  document.querySelectorAll('.userstatus').forEach(b=>b.onclick=async()=>{const u=users.find(x=>x.id===b.dataset.id),verb=b.dataset.status==='INACTIVE'?'desactivar':'activar';if(!confirm(`¿${verb.charAt(0).toUpperCase()+verb.slice(1)} a ${u?.fullName||'este usuario'}?`))return;const x=await req(`${api}/users/${b.dataset.id}/status`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({status:b.dataset.status})});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo cambiar el estado.');renderUsers()});
 };wire();
 document.getElementById('userSearch').oninput=e=>{const q=e.target.value.trim().toLocaleLowerCase('es'),filtered=!q?users:users.filter(u=>[u.fullName,u.email].some(x=>String(x||'').toLocaleLowerCase('es').includes(q)));document.getElementById('userCount').textContent=filtered.length;document.getElementById('userList').innerHTML=filtered.length?filtered.map(userCard).join(''):'<div class="no-search-results">No encontramos usuarios con ese criterio.</div>';wire()};
}
function editUserModal(u){
 if(!u)return;
 openFormModal({title:`Editar usuario · ${u.fullName}`,body:`<label>Nombre completo<input name="name" value="${esc(u.fullName)}" required></label><label>Correo<input name="email" type="email" value="${esc(u.email)}" required></label><label>Rol<select name="role"><option value="TECHNICIAN" ${u.role==='TECHNICIAN'?'selected':''}>Técnico</option><option value="COMPANY_ADMIN" ${u.role==='COMPANY_ADMIN'?'selected':''}>Administrador</option></select></label>`,onSubmit:async(f,err)=>{const x=await req(`${api}/users/${u.id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({fullName:f.get('name'),email:f.get('email'),role:f.get('role')})});if(!x.r||!x.r.ok){err.textContent=x.j.error?.message||'No se pudo editar el usuario.';return false}renderUsers();return true}})
}
function passwordUserModal(u){
 if(!u)return;
 openFormModal({title:`Cambiar contraseña · ${u.fullName}`,body:`<label>Nueva contraseña<input name="password" type="password" minlength="8" placeholder="Mínimo 8 caracteres" required></label><label>Confirmar contraseña<input name="confirm" type="password" minlength="8" placeholder="Repite la contraseña" required></label>`,onSubmit:async(f,err)=>{const password=String(f.get('password')||''),confirmPassword=String(f.get('confirm')||'');if(password.length<8){err.textContent='La contraseña debe tener mínimo 8 caracteres.';return false}if(password!==confirmPassword){err.textContent='Las contraseñas no coinciden.';return false}const x=await req(`${api}/users/${u.id}/password`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({password})});if(!x.r||!x.r.ok){err.textContent=x.j.error?.message||'No se pudo cambiar la contraseña.';return false}return true}})
}

function reportVehicleDisplay(v){return `${v.plate}${v.internalNumber?` / ${v.internalNumber}`:''}`}
function resolveReportVehicleId(value){
 const q=String(value||'').trim().toLocaleLowerCase('es');if(!q)return null;
 const vehicles=window._reportVehicles||[];
 let v=vehicles.find(x=>reportVehicleDisplay(x).toLocaleLowerCase('es')===q);
 if(!v)v=vehicles.find(x=>String(x.plate||'').toLocaleLowerCase('es')===q||String(x.internalNumber||'').toLocaleLowerCase('es')===q);
 if(!v){const matches=vehicles.filter(x=>reportVehicleDisplay(x).toLocaleLowerCase('es').includes(q));if(matches.length===1)v=matches[0]}
 return v?.id||null
}
function excelEsc(v){return String(v??'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')}
function downloadExcel(filename,headers,rows){
 const table=`<table><thead><tr>${headers.map(h=>`<th>${excelEsc(h)}</th>`).join('')}</tr></thead><tbody>${rows.map(r=>`<tr>${r.map(c=>`<td>${excelEsc(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
 const html=`<!doctype html><html><head><meta charset="utf-8"></head><body>${table}</body></html>`;
 const blob=new Blob(['\ufeff',html],{type:'application/vnd.ms-excel;charset=utf-8'}),url=URL.createObjectURL(blob),link=document.createElement('a');
 link.href=url;link.download=`${filename}.xls`;document.body.appendChild(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000)
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
async function loadLatestServices(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando últimos servicios…</p>');
 const q=new URLSearchParams(),vehicleId=resolveReportVehicleId(f.get('vehicle'));if(vehicleId)q.set('vehicleId',vehicleId);if(f.get('service'))q.set('service',f.get('service'));
 const out=await req(`${api}/reports/latest-services?${q}`);if(!out.r||!out.r.ok){box.innerHTML=card('<p class="error">No se pudo cargar el reporte.</p>');return}
 let rows=out.j.data.rows||[];if(f.get('vehicle')&&!vehicleId){const text=String(f.get('vehicle')).toLocaleLowerCase('es');rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text))}
 const vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`<div class="report-overview"><article class="report-stat"><small>Últimos servicios</small><strong>${rows.length}</strong></article><article class="report-stat"><small>Vehículos mostrados</small><strong>${vehicleCount}</strong></article></div>
 ${card(`<div class="sectionhead report-result-head"><div><h2>Últimos servicios ejecutados</h2><p class="muted">Una sola fila por servicio y vehículo: siempre la ejecución más reciente.</p></div><div class="report-export-actions"><button id="excelreport" class="secondary">Descargar Excel</button><button id="printreport" class="secondary">Imprimir / PDF</button></div></div>${rows.length?`<div class="reporttable"><div class="reportrow reportheader"><span>Fecha</span><span>Vehículo</span><span>Servicio</span><span>Último km</span><span>Técnico</span><span>Próximo</span></div>${rows.map(x=>`<div class="reportrow"><span class="report-date">${new Date(x.serviceDate).toLocaleDateString('es-CO')}</span><span>${reportVehicleCell(x)}</span><span><b>${esc(x.serviceName)}</b>${x.notes?`<small>${esc(x.notes)}</small>`:''}</span><span><b>${fmt(x.mileage)} km</b></span><span>${esc(x.technician||'—')}</span><span><b>${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'—'}</b></span></div>`).join('')}</div>`:reportEmpty('No hay servicios registrados con los filtros seleccionados.')}`)}`;
 wireReportExports('ultimos_servicios',['Fecha','Placa','Interno','Marca','Modelo','Servicio','Kilometraje','Técnico','Próximo'],rows.map(x=>[new Date(x.serviceDate).toLocaleDateString('es-CO'),x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,x.mileage,x.technician||'',x.nextDueMileage??(x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'')]))
}
async function loadReport(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando reporte…</p>');
 const q=new URLSearchParams({from:f.get('from'),to:f.get('to')}),vehicleId=resolveReportVehicleId(f.get('vehicle'));if(vehicleId)q.set('vehicleId',vehicleId);if(f.get('service'))q.set('service',f.get('service'));
 const out=await req(`${api}/reports/maintenance?${q}`);if(!out.r||!out.r.ok){box.innerHTML=card(`<p class="error">${esc(out.j.error?.message||'No se pudo cargar el reporte.')}</p>`);return}
 const d=out.j.data;let rows=d.rows||[];if(f.get('vehicle')&&!vehicleId){const text=String(f.get('vehicle')).toLocaleLowerCase('es');rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text))}
 const vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`<div class="report-overview"><article class="report-stat"><small>Servicios registrados</small><strong>${rows.length}</strong></article><article class="report-stat"><small>Vehículos mostrados</small><strong>${vehicleCount}</strong></article><article class="report-period"><small>Periodo consultado</small><b>${new Date(d.from).toLocaleDateString('es-CO')} — ${new Date(d.to).toLocaleDateString('es-CO')}</b></article></div>
 ${card(`<div class="sectionhead report-result-head"><div><h2>Historial de servicios</h2><p class="muted">${rows.length?'Resultados del periodo y filtros seleccionados.':'No hay mantenimientos registrados con estos filtros.'}</p></div><div class="report-export-actions"><button id="excelreport" class="secondary">Descargar Excel</button><button id="printreport" class="secondary">Imprimir / PDF</button></div></div>${rows.length?`<div class="reporttable"><div class="reportrow reportheader"><span>Fecha</span><span>Vehículo</span><span>Servicio</span><span>Km</span><span>Técnico</span><span>Próximo</span></div>${rows.map(x=>`<div class="reportrow"><span class="report-date">${new Date(x.serviceDate).toLocaleDateString('es-CO')}</span><span>${reportVehicleCell(x)}</span><span><b>${esc(x.serviceName)}</b>${x.notes?`<small>${esc(x.notes)}</small>`:''}</span><span><b>${fmt(x.mileage)} km</b></span><span>${esc(x.technician||'—')}</span><span><b>${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):'—'}</b></span></div>`).join('')}</div>`:reportEmpty('Prueba ampliando el periodo o quitando algún filtro.')}`)}`;
 wireReportExports('historial_servicios',['Fecha','Placa','Interno','Marca','Modelo','Servicio','Kilometraje','Técnico','Próximo','Observaciones'],rows.map(x=>[new Date(x.serviceDate).toLocaleDateString('es-CO'),x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,x.mileage,x.technician||'',x.nextDueMileage??(x.nextDueDate?new Date(x.nextDueDate).toLocaleDateString('es-CO'):''),x.notes||'']))
}
async function loadStatusReport(f){
 const box=document.getElementById('reportresult');box.innerHTML=card('<p class="muted">Cargando estado actual…</p>');
 const out=await req(api+'/dashboard');if(!out.r||!out.r.ok){box.innerHTML=card('<p class="error">No se pudo cargar el reporte por estado.</p>');return}
 const vehicleId=resolveReportVehicleId(f.get('vehicle')),service=String(f.get('service')||''),status=String(f.get('status')||''),text=String(f.get('vehicle')||'').toLocaleLowerCase('es');
 let rows=(out.j.data.priorities||[]).filter(x=>(!vehicleId||x.vehicleId===vehicleId)&&(!service||x.serviceName===service)&&(!status||x.status===status));
 if(f.get('vehicle')&&!vehicleId)rows=rows.filter(x=>`${x.plate} ${x.internalNumber||''}`.toLocaleLowerCase('es').includes(text));
 const labels={OVERDUE:'Vencido',DUE_SOON:'Próximo',UP_TO_DATE:'Al día',NO_BASELINE:'Sin historial'},vehicleCount=new Set(rows.map(x=>x.vehicleId)).size;
 box.innerHTML=`<div class="report-overview"><article class="report-stat"><small>Servicios mostrados</small><strong>${rows.length}</strong></article><article class="report-stat"><small>Vehículos mostrados</small><strong>${vehicleCount}</strong></article></div>
 ${card(`<div class="sectionhead report-result-head"><div><h2>Estado actual de mantenimiento</h2><p class="muted">Situación actual de cada servicio según kilometraje e historial.</p></div><div class="report-export-actions"><button id="excelreport" class="secondary">Descargar Excel</button><button id="printreport" class="secondary">Imprimir / PDF</button></div></div>${rows.length?`<div class="reporttable status-report-table"><div class="reportrow status-report-row reportheader"><span>Vehículo</span><span>Servicio</span><span>Estado</span><span>Actual</span><span>Próximo</span><span>Distancia</span></div>${rows.map(x=>`<div class="reportrow status-report-row"><span>${reportVehicleCell(x)}</span><span><b>${esc(x.serviceName)}</b></span><span>${statusBadge(x.status)}</span><span><b>${fmt(x.currentMileage)} km</b></span><span>${x.nextDueMileage!=null?`${fmt(x.nextDueMileage)} km`:'—'}</span><span>${x.remainingKm==null?'—':x.remainingKm<0?`${fmt(Math.abs(x.remainingKm))} km vencido`:`Faltan ${fmt(x.remainingKm)} km`}</span></div>`).join('')}</div>`:reportEmpty('No hay servicios con el estado y filtros seleccionados.')}`)}`;
 wireReportExports('reporte_por_estado',['Placa','Interno','Marca','Modelo','Servicio','Estado','Km actual','Próximo km','Distancia'],rows.map(x=>[x.plate,x.internalNumber||'',x.brand,x.model,x.serviceName,labels[x.status]||x.status,x.currentMileage,x.nextDueMileage??'',x.remainingKm??'']))
}

async function renderDashboard(vehicles=[]){
 const main=document.getElementById('main');main.innerHTML=card('<h1>Centro de Control</h1><p class="muted">Cargando estado de la flota…</p>');
 const out=await req(api+'/dashboard');if(!out.r||!out.r.ok){main.innerHTML=card('<h1>Centro de Control</h1><p class="error">No se pudo cargar el tablero.</p>');return}
 const d=out.j.data,rows=d.priorities||[],updates=d.todayUpdates||[];
 const staleMileage=(vehicles||[]).filter(v=>{const t=new Date(v.mileageUpdatedAt).getTime();return Number.isFinite(t)&&Date.now()-t>24*60*60*1000}).sort((x,y)=>new Date(x.mileageUpdatedAt)-new Date(y.mileageUpdatedAt));
 const serviceCounts={UP_TO_DATE:0,DUE_SOON:0,OVERDUE:0,NO_BASELINE:0};rows.forEach(x=>{if(serviceCounts[x.status]!=null)serviceCounts[x.status]++});
 const totalServices=Object.values(serviceCounts).reduce((x,y)=>x+y,0)||1;
 const pct=s=>Math.round((serviceCounts[s]||0)*100/totalServices);
 const common=[...rows.reduce((m,x)=>(m.set(x.serviceName,(m.get(x.serviceName)||0)+1),m),new Map()).entries()].sort((x,y)=>y[1]-x[1]).slice(0,5);
 const urgent=rows.filter(x=>x.status==='OVERDUE'||x.status==='DUE_SOON').slice(0,5);
 const sourceLabel=x=>x==='PUBLIC_QR'?'Conductor':x==='TECHNICIAN'?'Técnico':x==='MAINTENANCE'?'Técnico':'Sistema';
 main.innerHTML=`<div class="cc-head"><div><h1>Centro de Control</h1><p>Resumen general de la flota</p></div><button id="refreshdash" class="secondary">Actualizar</button></div>
 <section class="cc-kpis">
  <article class="cc-kpi blue"><div class="cc-kpi-icon">${uiIcon('vehicle')}</div><div><small>Vehículos activos</small><strong>${d.total}</strong><span>Total en flota</span></div></article>
  <article class="cc-kpi orange clickable" data-status="DUE_SOON"><div class="cc-kpi-icon">${uiIcon('service')}</div><div><small>Próximos servicios</small><strong>${d.dueSoon}</strong><span>Vehículos próximos</span></div></article>
  <article class="cc-kpi red clickable" data-status="OVERDUE"><div class="cc-kpi-icon">${uiIcon('alert')}</div><div><small>En alerta</small><strong>${d.overdue}</strong><span>Vehículos vencidos</span></div></article>
  <article class="cc-kpi slate clickable" id="staleMileageCard"><div class="cc-kpi-icon">${uiIcon('gauge')}</div><div><small>Sin kilometraje hoy</small><strong>${staleMileage.length}</strong><span>Más de 1 día sin actualizar</span></div></article>
 </section>
 <section class="cc-grid">
  <article class="cc-panel"><div class="cc-panel-head"><h2>Próximos servicios</h2><button class="textbtn" id="showAllDue">Ver todos</button></div><div class="cc-list">${urgent.length?urgent.map(x=>`<button class="cc-service-row dashmaint" data-vehicle="${x.vehicleId}"><span class="cc-service-icon">${serviceIcon(x.serviceName)}</span><span class="cc-service-main"><small>${x.internalNumber?`Interno ${esc(x.internalNumber)}`:''}</small><b>${esc(x.plate)}</b><em>${esc(x.serviceName)}</em></span><span class="cc-service-due ${x.status==='OVERDUE'?'badtext':''}">${x.remainingKm==null?'—':x.remainingKm<0?`Vencido ${fmt(Math.abs(x.remainingKm))} km`:`Faltan ${fmt(x.remainingKm)} km`}<small>${x.nextDueMileage!=null?`Próximo ${fmt(x.nextDueMileage)} km`:''}</small></span></button>`).join(''):'<p class="muted cc-empty">No hay servicios próximos ni vencidos.</p>'}</div></article>
  <article class="cc-panel cc-search-panel"><div class="cc-panel-head"><h2>Buscar vehículo</h2></div><div class="cc-searchbox">${uiIcon('search')}<input id="ccVehicleSearch" placeholder="Buscar por placa o número interno" aria-label="Buscar por placa o número interno" autocomplete="off"></div><h3>Vehículos</h3><div id="ccVehicleResults" class="cc-vehicle-results">${vehicles.slice(0,4).map(v=>ccVehicleResult(v)).join('')}</div><button id="goVehicles" class="textbtn cc-all-link">Ir a la lista de vehículos →</button></article>
 </section>
 <section class="cc-bottom">
  <article class="cc-panel cc-status-panel"><div class="cc-panel-head"><h2>Estado De Servicios</h2></div><div class="cc-status-content"><div class="cc-donut" style="background:conic-gradient(#22a447 0 ${pct('UP_TO_DATE')}%,#f5ad22 ${pct('UP_TO_DATE')}% ${pct('UP_TO_DATE')+pct('DUE_SOON')}%,#e52f35 ${pct('UP_TO_DATE')+pct('DUE_SOON')}% ${pct('UP_TO_DATE')+pct('DUE_SOON')+pct('OVERDUE')}%,#98a2b3 0)"><div><small>Total servicios</small><strong>${totalServices}</strong></div></div><div class="cc-legend"><span><i class="green-dot"></i>Al día <b>${serviceCounts.UP_TO_DATE}</b></span><span><i class="orange-dot"></i>Próximo <b>${serviceCounts.DUE_SOON}</b></span><span><i class="red-dot"></i>Vencido <b>${serviceCounts.OVERDUE}</b></span><span><i class="gray-dot"></i>Sin historial <b>${serviceCounts.NO_BASELINE}</b></span></div><div class="cc-common"><h3>Servicios más comunes</h3>${common.map(x=>`<div><span>${esc(x[0])}</span><b>${x[1]}</b></div>`).join('')}</div></div></article>
  <article class="cc-panel cc-actions"><div class="cc-panel-head"><h2>Acciones rápidas</h2></div><div class="cc-action-grid"><button data-go="vehicles">${uiIcon('vehicle')}<span>Vehículos</span></button><button data-go="plans">${uiIcon('service')}<span>Planes</span></button><button data-go="reports">${uiIcon('reports')}<span>Reportes</span></button><button data-go="users">${uiIcon('users')}<span>Usuarios</span></button></div></article>
 </section>
 <section id="ccDetail" class="cc-detail" hidden></section>`;
 const renderStatus=status=>{const filtered=rows.filter(x=>x.status===status),labels={OVERDUE:'Vencidos',DUE_SOON:'Próximos'};const box=document.getElementById('ccDetail');box.hidden=false;box.innerHTML=card(`<div class="sectionhead"><div><h2>${labels[status]}</h2><p class="muted">${filtered.length} servicio${filtered.length===1?'':'s'} en este estado.</p></div><button id="closeCcDetail" class="secondary">Cerrar</button></div>${filtered.length?`<div class="prioritytable">${filtered.map(x=>`<div class="priorityrow"><span><b>${esc(x.plate)}</b><small>${x.internalNumber?`Interno ${esc(x.internalNumber)}`:''}</small></span><span><b class="service-name-icon">${serviceNameWithIcon(x.serviceName)}</b></span><span>${statusBadge(x.status)}</span><span>${x.remainingKm==null?'—':x.remainingKm<0?`${fmt(Math.abs(x.remainingKm))} km vencido`:`Faltan ${fmt(x.remainingKm)} km`}</span><span><button class="dashmaint" data-vehicle="${x.vehicleId}">Ver</button></span></div>`).join('')}</div>`:''}`);document.getElementById('closeCcDetail').onclick=()=>box.hidden=true;box.querySelectorAll('.dashmaint').forEach(b=>b.onclick=()=>maintenanceView(b.dataset.vehicle));box.scrollIntoView({behavior:'smooth',block:'start'})};
 const renderStaleMileage=()=>{const box=document.getElementById('ccDetail');box.hidden=false;box.innerHTML=card(`<div class="sectionhead"><div><h2>Vehículos sin kilometraje actualizado</h2><p class="muted">${staleMileage.length} vehículo${staleMileage.length===1?'':'s'} con más de 1 día desde la última lectura.</p></div><button id="closeCcDetail" class="secondary">Cerrar</button></div>${staleMileage.length?`<div class="stale-mileage-list">${staleMileage.map(v=>{const days=Math.floor((Date.now()-new Date(v.mileageUpdatedAt).getTime())/86400000);return `<button class="stale-mileage-row cc-vehicle-result" data-vehicle="${v.id}"><span><small>${v.internalNumber?`Interno ${esc(v.internalNumber)}`:''}</small><b>${esc(v.plate)}</b><em>${esc(v.brand)} ${esc(v.model)}</em></span><span><strong>${fmt(v.currentMileage)} km</strong><small>Última lectura: ${new Date(v.mileageUpdatedAt).toLocaleString('es-CO')}</small><em>${days} día${days===1?'':'s'} sin actualizar</em></span></button>`}).join('')}</div>`:'<div class="report-empty"><b>Todo al día</b><small>Todos los vehículos tienen una lectura de kilometraje de las últimas 24 horas.</small></div>'}`);document.getElementById('closeCcDetail').onclick=()=>box.hidden=true;wireCcVehicles();box.scrollIntoView({behavior:'smooth',block:'start'})};
 document.querySelectorAll('.cc-kpi.clickable[data-status]').forEach(k=>k.onclick=()=>renderStatus(k.dataset.status));document.getElementById('staleMileageCard').onclick=renderStaleMileage;document.getElementById('showAllDue').onclick=()=>renderStatus(d.overdue?'OVERDUE':'DUE_SOON');
 document.querySelectorAll('.dashmaint').forEach(b=>b.onclick=()=>maintenanceView(b.dataset.vehicle));document.getElementById('refreshdash').onclick=()=>renderDashboard(vehicles);document.getElementById('goVehicles').onclick=()=>adminShell('vehicles');document.querySelectorAll('[data-go]').forEach(b=>b.onclick=()=>adminShell(b.dataset.go));
 const input=document.getElementById('ccVehicleSearch'),results=document.getElementById('ccVehicleResults');input.oninput=()=>{const q=input.value.trim().toUpperCase(),found=!q?vehicles.slice(0,4):vehicles.filter(v=>String(v.plate||'').toUpperCase().includes(q)||String(v.internalNumber||'').toUpperCase().includes(q)).slice(0,6);results.innerHTML=found.length?found.map(v=>ccVehicleResult(v)).join(''):'<p class="muted cc-empty">No se encontró ningún vehículo.</p>';wireCcVehicles()};
 wireCcVehicles()
}
function ccVehicleResult(v){return `<button class="cc-vehicle-result" data-vehicle="${v.id}"><span><small>${v.internalNumber?`Interno ${esc(v.internalNumber)}`:''}</small><b>${esc(v.plate)}</b><em>${esc(v.brand)} ${esc(v.model)}</em></span><strong>${fmt(v.currentMileage)} km</strong></button>`}
function wireCcVehicles(){document.querySelectorAll('.cc-vehicle-result').forEach(b=>b.onclick=()=>maintenanceView(b.dataset.vehicle))}



function upperLive(input){input.addEventListener('input',()=>{const pos=input.selectionStart;input.value=input.value.toUpperCase();try{input.setSelectionRange(pos,pos)}catch{}})}
function titleCaseValue(v){return String(v||'').trim().toLocaleLowerCase('es').replace(/(^|[\s\-\/])([a-záéíóúüñ])/g,(m,p,c)=>p+c.toLocaleUpperCase('es'))}
function titleLive(input){input.addEventListener('blur',()=>{input.value=titleCaseValue(input.value)})}
function integerOnly(input){input.setAttribute('inputmode','numeric');input.addEventListener('input',()=>{input.value=input.value.replace(/[^\d]/g,'')})}

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

function moduleTitle(icon,title,subtitle){
 return `<div class="module-title"><span class="module-title-icon">${uiIcon(icon)}</span><div><h1>${esc(title)}</h1>${subtitle?`<p class="muted">${esc(subtitle)}</p>`:''}</div></div>`
}
function renderVehicles(vehicles,plans,isAdmin=true){
 const main=document.getElementById('main'),catalog=vehicleCatalog(vehicles,plans);
 main.innerHTML=(isAdmin?card(`<div class="top vehicle-page-head">${moduleTitle('vehicle','Vehículos','Administra la flota y consulta el mantenimiento de cada unidad.')}<button type="button" id="showArchivedVehicles" class="secondary mobile-archived-btn">Ver archivados</button></div><div class="vehicle-create-title"><h2>Nuevo vehículo</h2><p class="muted">Completa los datos. El plan es opcional; también puedes crear servicios propios para este vehículo.</p></div><form id="create" class="vehicle-create-grid"><label>Placa<input name="plate" class="force-upper" placeholder="ABC-123" autocomplete="off" required></label><label>Número interno<input name="internal" class="force-upper" placeholder="Opcional" autocomplete="off"></label>${catalogFields(catalog,'vehicle')}<label>Kilometraje<input name="km" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="134500" required></label><label>Plan de mantenimiento <small class="optional-label">Opcional</small><select name="planVersionId"><option value="">Sin plan por ahora</option>${plans.map(p=>`<option value="${p.versionId}">${esc(p.name)} V${p.versionNumber}</option>`).join('')}</select></label><div class="vehicle-create-action"><button>Crear vehículo</button></div></form><p id="err" class="error"></p>`):card(`<h1>Vehículos</h1><p class="muted">Consulta el estado y registra los mantenimientos realizados.</p>`))+`<div class="vehicle-list-head"><h2><span id="vehicleCount">${vehicles.length}</span> vehículo${vehicles.length===1?'':'s'} activo${vehicles.length===1?'':'s'}</h2><div class="vehicle-search"><input id="vehicleSearch" type="search" placeholder="Buscar por placa o interno" aria-label="Buscar por placa o interno" autocomplete="off"></div></div><div id="vehicleList" class="list vehicle-list">${vehicles.map(v=>vehicleCard(v,plans,isAdmin)).join('')}</div>`;
 const vehicleSearch=document.getElementById('vehicleSearch');
 if(vehicleSearch)vehicleSearch.oninput=()=>{const q=vehicleSearch.value.trim().toUpperCase();const filtered=!q?vehicles:vehicles.filter(v=>String(v.plate||'').toUpperCase().includes(q)||String(v.internalNumber||'').toUpperCase().includes(q));document.getElementById('vehicleList').innerHTML=filtered.length?filtered.map(v=>vehicleCard(v,plans,isAdmin)).join(''):'<div class="no-search-results">No encontramos vehículos con esa placa o número interno.</div>';document.getElementById('vehicleCount').textContent=filtered.length;wireVehicleButtons(filtered,plans,isAdmin)};
 if(isAdmin){
  document.getElementById('showArchivedVehicles').onclick=()=>showArchivedVehicles(plans);
  const form=document.getElementById('create');wireVehicleCatalog(form,catalog,'vehicle');form.querySelectorAll('.force-upper').forEach(upperLive);form.querySelectorAll('.title-case').forEach(titleLive);form.querySelectorAll('.integer-km').forEach(integerOnly);
  form.onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target);const out=await req(api+'/vehicles',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({plate:String(f.get('plate')).trim().toUpperCase(),internalNumber:f.get('internal')?String(f.get('internal')).trim().toUpperCase():null,brand:titleCaseValue(f.get('brand')),model:titleCaseValue(f.get('model')),variant:null,currentMileage:Number(String(f.get('km')).replace(/\D/g,'')),planVersionId:f.get('planVersionId')||null})});if(!out.r||!out.r.ok){document.getElementById('err').textContent=out.j.error?.message||'No se pudo crear.';return}adminShell('vehicles')};
 }
 wireVehicleButtons(vehicles,plans,isAdmin);
}

async function showArchivedVehicles(plans){
 const out=await req(api+'/vehicles/archived');if(!out.r||!out.r.ok)return alert('No se pudieron cargar los vehículos archivados.');
 const rows=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Vehículos archivados</h1><p class="muted">Conservan todo su historial. Para volver a operación debes seleccionar un plan activo.</p></div><button id="backVehicles" class="secondary">Volver a activos</button></div>`)+`<div class="list">${rows.length?rows.map(v=>card(`<div class="top"><div><h3>${esc(v.plate)}${v.internalNumber?' / '+esc(v.internalNumber):''}</h3><p class="muted">${esc(v.brand)} ${esc(v.model)} · ${fmt(v.currentMileage)} km</p></div><div class="actions"><select id="reactplan-${v.id}"><option value="">Plan para reactivar…</option>${plans.map(p=>`<option value="${p.versionId}">${esc(p.name)} V${p.versionNumber}</option>`).join('')}</select><button class="reactvehicle" data-id="${v.id}">Reactivar</button></div></div>`)).join(''):card('<p class="muted">No hay vehículos archivados.</p>')}</div>`;
 document.getElementById('backVehicles').onclick=()=>adminShell('vehicles');
 document.querySelectorAll('.reactvehicle').forEach(b=>b.onclick=async()=>{const planVersionId=document.getElementById(`reactplan-${b.dataset.id}`).value;if(!planVersionId)return alert('Selecciona el plan con el que volverá a operación.');const x=await req(`${api}/vehicles/${b.dataset.id}/reactivate`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({planVersionId})});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo reactivar.');adminShell('vehicles')});
}
async function showArchivedPlans(){
 const out=await req(api+'/maintenance-plans/archived');if(!out.r||!out.r.ok)return alert('No se pudieron cargar los planes archivados.');
 const rows=out.j.data||[],main=document.getElementById('main');
 main.innerHTML=card(`<div class="top"><div><h1>Planes archivados</h1><p class="muted">Puedes reactivarlos sin perder sus servicios ni versiones.</p></div><button id="backPlans" class="secondary">Volver a activos</button></div>`)+`<div class="list">${rows.length?rows.map(p=>card(`<div class="top"><div><h3>${esc(p.name)}</h3><p class="muted">${esc(p.brand)} ${esc(p.model)}</p></div><button class="reactplan" data-id="${p.id}">Reactivar</button></div>`)).join(''):card('<p class="muted">No hay planes archivados.</p>')}</div>`;
 document.getElementById('backPlans').onclick=()=>adminShell('plans');
 document.querySelectorAll('.reactplan').forEach(b=>b.onclick=async()=>{const x=await req(`${api}/maintenance-plans/${b.dataset.id}/reactivate`,{method:'PATCH'});if(!x.r||!x.r.ok)return alert(x.j.error?.message||'No se pudo reactivar.');adminShell('plans')});
}
async function archiveVehicle(id,plate){
 if(!confirm(`¿Archivar el vehículo ${plate}?\n\nNo se borrará su historial. El QR quedará desactivado y el vehículo dejará de aparecer en la operación activa.`))return;
 const out=await req(`${api}/vehicles/${id}/archive`,{method:'PATCH'});if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo archivar el vehículo.');adminShell('vehicles');
}
async function archivePlan(id,name){
 if(!confirm(`¿Archivar el plan "${name}"?\n\nNo se borrará su historial. Solo puede archivarse si ningún vehículo activo lo está usando.`))return;
 const out=await req(`${api}/maintenance-plans/${id}/archive`,{method:'PATCH'});if(!out.r||!out.r.ok)return alert(out.j.error?.message||'No se pudo archivar el plan.');adminShell('plans');
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

function vehicleCard(v,plans,isAdmin=true){
 return `<article class="vehicle vehicle-card"><div class="vehicle-content"><div class="vehicle-main"><div class="vehicle-title"><strong>${esc(v.plate)}${v.internalNumber?` <span class="vehicle-slash">/</span> ${esc(v.internalNumber)}`:''}</strong></div><div class="vehicle-model">${esc(v.brand)} ${esc(v.model)}</div><div class="vehicle-meta"><div><small>Kilometraje</small><b>${fmt(v.currentMileage)} km</b></div><div><small>Plan de mantenimiento</small><b>${v.planName?`${esc(v.planName)} · V${v.planVersion}`:'Servicios individuales / Sin plan'}</b></div></div><div class="vehicle-actions-main"><button class="maintbtn" data-vehicle="${v.id}">Estado de mantenimiento</button><button class="historybtn secondary" data-vehicle="${v.id}">Historial</button>${isAdmin?`<button class="kmhistorybtn secondary" data-vehicle="${v.id}">Kilometrajes</button>`:''}</div>${isAdmin?`<div class="vehicle-actions-admin"><button class="editvehicle textbtn" data-vehicle="${v.id}">Editar vehículo</button><button class="archivevehicle textbtn dangertext" data-vehicle="${v.id}" data-plate="${esc(v.plate)}">Archivar</button></div>`:''}</div></div>${v.qrToken?`<div class="qr vehicle-qr"><img src="${api}/public/qr/${encodeURIComponent(v.qrToken)}.svg"><div class="qr-actions"><a class="button secondary" href="/v/${encodeURIComponent(v.qrToken)}">Abrir QR</a><button class="secondary labelbtn" data-vehicle="${v.id}">Imprimir etiqueta</button></div></div>`:''}</article>`;
}
async function printQrLabel(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/qr-label`);if(!out.r||!out.r.ok)return alert('No se pudo preparar la etiqueta QR.');
 const v=out.j.data,qr=`${api}/public/qr/${encodeURIComponent(v.qrToken)}.svg`;let w=window.open('','_blank');if(!w){return alert('Safari bloqueó la ventana de impresión. Activa temporalmente las ventanas emergentes para AutoControl QR e inténtalo de nuevo.')}
 w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Etiqueta QR ${esc(v.plate)}</title><style>
 @page{size:60mm 60mm;margin:0}*{box-sizing:border-box}html,body{width:60mm;height:60mm;font-family:Arial,sans-serif;margin:0;color:#111;overflow:hidden}.label{width:60mm;height:60mm;border:1.2px solid #111;border-radius:2.5mm;padding:1.5mm;margin:auto;text-align:center;display:flex;flex-direction:column;align-items:center}.brand{font-size:10px;font-weight:900;letter-spacing:.4px}.tag{font-size:8px;margin:0 0 2px}.company{font-size:7px;font-weight:700;margin:0}.plate-label{font-size:8px;font-weight:bold}.plate{font-size:17px;font-weight:900;letter-spacing:1px;margin:0}.internal{font-size:11px;font-weight:bold;margin-bottom:.5mm}.qrimg{width:36mm;height:36mm}.instruction{font-size:8px;font-weight:800;margin-top:.5mm}.sub{font-size:6px;margin:0}.vehicle{font-size:9px;margin-top:3mm;color:#444}@media print{button{display:none}.label{break-inside:avoid}}button{margin:20px auto;display:block;padding:10px 18px}</style></head><body><div class="label"><div class="brand">AUTOCONTROL QR</div><div class="company">${esc(v.companyName)}</div><div class="plate">${esc(v.plate)}${v.internalNumber?` / ${esc(v.internalNumber)}`:''}</div><img class="qrimg" src="${qr}"><div class="instruction">Escanee para actualizar kilometraje</div><div class="sub">Mantenga este código visible y en buen estado.</div></div><button onclick="window.print()">Imprimir / Guardar PDF</button><script>window.addEventListener('load',()=>setTimeout(()=>window.print(),450));<\/script></body></html>`);w.document.close();
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
async function addService(versionId,planName='Plan'){
 const wrap=openFormModal({
  title:`Agregar servicio · ${planName}`,
  body:`<label>Servicio<input name="name" placeholder="Ej. Aceite de motor" required></label><div class="service-interval-grid"><label>Intervalo por kilometraje<input name="interval" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="Ej. 10000" required></label><label>Prealerta<input name="pre" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" value="1000"></label></div><label>Especificación <small class="optional-label">Opcional</small><input name="spec" placeholder="Ej. 5W-30"></label><p class="muted">Ejemplo: intervalo 10.000 km y prealerta 1.000 km avisará cuando falten 1.000 km para el servicio.</p>`,
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
  body:`<label>Servicio<input name="name" value="${esc(service.name)}" required></label><div class="service-interval-grid"><label>Intervalo por kilometraje<input name="interval" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" value="${service.intervalKm??''}" required></label><label>Prealerta<input name="pre" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" value="${service.prealertKm??0}"></label></div><label>Especificación <small class="optional-label">Opcional</small><input name="spec" value="${esc(service.specification||'')}" placeholder="Ej. 5W-30"></label><p class="muted">Este cambio modifica la configuración futura del plan. Los mantenimientos ya registrados conservan los datos históricos con los que fueron ejecutados.</p>`,
  onSubmit:async(f,err)=>{
    const interval=Number(String(f.get('interval')).replace(/\D/g,'')),pre=Number(String(f.get('pre')||'0').replace(/\D/g,''));
    if(!interval){err.textContent='Ingresa un intervalo de kilometraje válido.';return false}
    const out=await req(`${api}/plan-services/${service.id}`,{method:'PATCH',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),specification:f.get('spec')||null,intervalKm:interval,intervalMonths:null,prealertKm:pre,prealertDays:null})});
    if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo actualizar el servicio.';return false}
    showServices(versionId,planName);return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly)
}

async function maintenanceView(vehicleId){
 const out=await req(`${api}/vehicles/${vehicleId}/maintenance-status`);if(!out.r||!out.r.ok)return alert('No se pudo cargar el estado.');const d=out.j.data,user=JSON.parse(localStorage.getItem('user')||'{}'),isAdmin=user.role==='COMPANY_ADMIN';
 if(d.overallStatus==='NO_PLAN' && !isAdmin){document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><h1>${esc(d.plate)}</h1><p><b>${fmt(d.currentMileage)} km</b></p><div class="no-search-results"><b>Sin servicios configurados</b><small>El administrador todavía no ha creado servicios para este vehículo.</small></div>`);document.getElementById('back').onclick=()=>adminShell('vehicles');return}
 const rows=d.services.map(s=>`<article class="statusrow"><div><strong class="service-name-icon">${serviceNameWithIcon(s.name)}</strong>${statusBadge(s.status)}<small>${s.nextDueMileage!=null?`Próximo: ${fmt(s.nextDueMileage)} km`:''}${s.remainingKm!=null?` · ${s.remainingKm>0?'Faltan':'Vencido'} ${fmt(Math.abs(s.remainingKm))} km`:''}</small></div><div class="rowactions">${s.status==='NO_BASELINE' && (JSON.parse(localStorage.getItem('user')||'{}').role==='COMPANY_ADMIN')?`<button class="baselinebtn secondary" data-service="${s.serviceId}">Cargar último servicio</button>`:''}<button class="registerbtn" data-service="${s.serviceId}" data-name="${esc(s.name)}" data-status="${s.status}">Registrar mantenimiento</button></div></article>`).join('');
 document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><div class="maintenance-page-head"><div><h1>${esc(d.plate)}</h1><p><b>${fmt(d.currentMileage)} km</b> ${d.overallStatus==='NO_PLAN'?'<span class="badge mutedb">Servicios individuales</span>':statusBadge(d.overallStatus)} ${d.hasIncompleteHistory?'<span class="badge mutedb">⚫ Historial incompleto</span>':''}</p></div>${isAdmin && (d.individualServices||d.overallStatus==='NO_PLAN')?'<button id="addVehicleService">+ Agregar servicio</button>':''}</div><h2>Estado de mantenimiento</h2>${rows||'<div class="no-search-results"><b>Aún no hay servicios</b><small>Agrega solamente los mantenimientos que deseas controlar para este vehículo.</small></div>'}`);
 document.getElementById('back').onclick=()=>adminShell('vehicles');const addVehicleServiceBtn=document.getElementById('addVehicleService');if(addVehicleServiceBtn)addVehicleServiceBtn.onclick=()=>addIndividualVehicleService(vehicleId);document.querySelectorAll('.baselinebtn').forEach(b=>b.onclick=()=>loadBaseline(vehicleId,b.dataset.service));document.querySelectorAll('.registerbtn').forEach(b=>b.onclick=()=>{if(b.dataset.status==='UP_TO_DATE'&&!confirm(`Este servicio está AL DÍA y todavía no corresponde por kilometraje o tiempo.\n\n¿Estás seguro de registrar "${b.dataset.name}" ahora?`))return;registerMaintenance(vehicleId,d.currentMileage,b.dataset.service,b.dataset.name)});
}

function addIndividualVehicleService(vehicleId){
 const wrap=openFormModal({
  title:'Agregar servicio al vehículo',
  body:`<label>Servicio<input name="name" placeholder="Ej. Aceite Motor" required></label><div class="service-interval-grid"><label>Intervalo por kilometraje<input name="interval" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="10000" required></label><label>Prealerta<input name="pre" class="integer-km" type="text" inputmode="numeric" pattern="[0-9]*" placeholder="1000"></label></div><label>Especificación <small class="optional-label">Opcional</small><input name="spec" placeholder="Ej. 5W-30"></label><p class="muted">Este servicio pertenecerá únicamente a este vehículo. No crea un Plan de Mantenimiento para otros vehículos.</p>`,
  onSubmit:async(f,err)=>{
    const interval=Number(String(f.get('interval')).replace(/\D/g,'')),pre=Number(String(f.get('pre')||'0').replace(/\D/g,''));
    if(!interval){err.textContent='Ingresa un intervalo válido.';return false}
    const out=await req(`${api}/vehicles/${vehicleId}/individual-services`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:f.get('name'),category:'General',specification:f.get('spec')||null,intervalKm:interval,intervalMonths:null,prealertKm:pre,prealertDays:null})});
    if(!out.r||!out.r.ok){err.textContent=out.j.error?.message||'No se pudo agregar el servicio.';return false}
    maintenanceView(vehicleId);return true
  }
 });wrap.querySelectorAll('.integer-km').forEach(integerOnly)
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
 const rows=out.j.data||[];
 document.getElementById('main').innerHTML=card(`<button id="back" class="secondary">← Volver</button><div class="dashboard-head"><div><h1>Historial de kilometraje</h1><p class="muted">Las lecturas del QR conservan su fecha y hora automáticas.</p></div><button id="addhistorical">+ Cargar lectura histórica</button></div>${rows.length?`<div class="historylist">${rows.map(x=>`<article class="historyrow"><div><strong>${fmt(x.mileage)} km</strong><span>${new Date(x.createdAt).toLocaleString('es-CO')}</span><small>${esc(sourceLabel(x.source))}${x.isExceptional?' · 🟠 Lectura excepcional':''}</small></div></article>`).join('')}</div>`:'<p class="muted">No hay lecturas todavía.</p>'}`);
 document.getElementById('back').onclick=()=>adminShell('vehicles');
 document.getElementById('addhistorical').onclick=()=>addHistoricalMileage(vehicleId);
}
function sourceLabel(s){return ({INITIAL:'Lectura inicial',PUBLIC_QR:'QR público',TECHNICIAN:'Técnico',ADMIN_HISTORICAL:'Carga histórica',ADMIN_CORRECTION:'Corrección administrativa',IMPORT:'Importación'})[s]||s}
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

const path=location.pathname;if(path.startsWith('/v/'))publicView(path.split('/v/')[1]);else if(token())adminShell('dashboard');else loginView();
