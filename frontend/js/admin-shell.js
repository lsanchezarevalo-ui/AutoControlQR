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
   <div class="sidebar-brand"><div class="sidebar-mark">${uiIcon('brand')}</div><div><strong>AUTOCONTROL QR</strong><small>Control de mantenimiento</small></div></div>
   <nav class="sidebar-nav">${nav.map(x=>`<button data-view="${x[0]}" class="sidebtn ${view===x[0]?'active':''}">${uiIcon(x[1])}<span>${x[2]}</span></button>`).join('')}</nav>
   <div class="sidebar-bottom"><button id="logout" class="sidebtn">${uiIcon('settings')}<span>Salir</span></button><small>v31.6</small></div>
 </aside>
 <div class="app-workspace"><header class="topbar"><button type="button" id="mobileMenu" class="mobile-menu-btn" aria-label="Abrir menú">☰</button><div class="top-company">${company.logoDataUrl?`<div class="top-company-logo"><img src="${esc(company.logoDataUrl)}" alt=""></div>`:`<div class="top-company-logo fallback">${esc(String(company.name||'E').charAt(0).toUpperCase())}</div>`}<strong>${esc(company.name)}</strong></div><div class="top-user"><span class="top-avatar">${esc(String(me.fullName||me.name||'A').charAt(0).toUpperCase())}</span><div><strong>${esc(me.fullName||me.name||'Usuario')}</strong><small>Administrador</small></div></div></header><main id="main"></main></div></div>`;
 document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>adminShell(b.dataset.view));document.getElementById('logout').onclick=()=>{localStorage.clear();loginView()};const mobileMenu=document.getElementById('mobileMenu'),sidebar=document.querySelector('.sidebar');if(mobileMenu&&sidebar){mobileMenu.onclick=()=>sidebar.classList.toggle('mobile-open');document.addEventListener('click',e=>{if(innerWidth<=800&&sidebar.classList.contains('mobile-open')&&!sidebar.contains(e.target)&&e.target!==mobileMenu)sidebar.classList.remove('mobile-open')},{once:true})};
 if(view==='dashboard')renderDashboard(vehicles);else if(view==='plans')renderPlans(plans,vehicles);else if(view==='reports')renderReports(vehicles);else if(view==='users')renderUsers();else if(view==='notifications')renderNotifications();else if(view==='settings')renderCompanySettings();else renderVehicles(vehicles,plans,true)
}
