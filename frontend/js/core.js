const api='/api/v1';
const app=document.getElementById('app');
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
const fmt=n=>Number(n).toLocaleString('es-CO');
const token=()=>localStorage.getItem('token');
function card(x,cls=''){return `<section class="card ${cls}">${x}</section>`}
async function req(url,opt={}){const headers={...(opt.headers||{})};const hadToken=!!token();if(hadToken)headers.Authorization='Bearer '+token();try{const r=await fetch(url,{...opt,headers,cache:'no-store'});let j={};try{j=await r.json()}catch{}if(r.status===401&&hadToken){localStorage.clear();loginView('La sesión expiró. Vuelve a iniciar sesión.')}return{r,j,networkError:null}}catch(e){return{r:null,j:{},networkError:e}}}
function uiIcon(type,cls=''){
 const paths={
  brand:'<rect x="3.5" y="3.5" width="7" height="7" rx="1.6"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.6"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.6"/><path d="M14 16.2l2.4 2.4L21 14"/>',
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

function moduleTitle(icon,title,subtitle){
 return `<div class="module-title"><span class="module-title-icon">${uiIcon(icon)}</span><div><h1>${esc(title)}</h1>${subtitle?`<p class="muted">${esc(subtitle)}</p>`:''}</div></div>`
}
function upperLive(input){input.addEventListener('input',()=>{const pos=input.selectionStart;input.value=input.value.toUpperCase();try{input.setSelectionRange(pos,pos)}catch{}})}
function titleCaseValue(v){return String(v||'').trim().toLocaleLowerCase('es').replace(/(^|[\s\-\/])([a-záéíóúüñ])/g,(m,p,c)=>p+c.toLocaleUpperCase('es'))}
function titleLive(input){input.addEventListener('blur',()=>{input.value=titleCaseValue(input.value)})}
function integerOnly(input){input.setAttribute('inputmode','numeric');input.addEventListener('input',()=>{input.value=input.value.replace(/[^\d]/g,'')})}

function excelEsc(v){return String(v??'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')}
function downloadExcel(filename,headers,rows){
 const table=`<table><thead><tr>${headers.map(h=>`<th>${excelEsc(h)}</th>`).join('')}</tr></thead><tbody>${rows.map(r=>`<tr>${r.map(c=>`<td>${excelEsc(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
 const html=`<!doctype html><html><head><meta charset="utf-8"></head><body>${table}</body></html>`;
 const blob=new Blob(['﻿',html],{type:'application/vnd.ms-excel;charset=utf-8'}),url=URL.createObjectURL(blob),link=document.createElement('a');
 link.href=url;link.download=`${filename}.xls`;document.body.appendChild(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000)
}
