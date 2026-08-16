function loginView(message=''){
 app.innerHTML=card(`<div class="login-brand"><span class="login-brand-mark">${uiIcon('brand')}</span><span class="eyebrow">AUTOCONTROL QR · V31.6</span></div><h1>Iniciar sesión</h1>${message?`<p class="error">${esc(message)}</p>`:''}<form id="login"><label>Correo<input name="email" type="email" autocomplete="username" placeholder="correo@empresa.com"></label><label>Contraseña<input name="password" type="password" autocomplete="current-password" placeholder="Contraseña"></label><button id="loginBtn">Ingresar</button><button type="button" id="forgotPassword" class="textbtn login-help">¿Olvidaste tu contraseña?</button><div id="forgotInfo" class="login-forgot-info" hidden><strong>Recuperación de acceso</strong><p>Si eres Técnico, solicita el restablecimiento al Administrador de tu empresa. Si eres Administrador de Empresa, contacta al Administrador General de AutoControl QR.</p></div><p id="err" class="error"></p></form>`,'narrow');
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
