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
