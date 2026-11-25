'use strict';
const $ = s => document.querySelector(s);
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = (n, digits=1) => n===null||n===undefined||Number.isNaN(Number(n)) ? '—' : Number(n).toLocaleString('tr-TR',{minimumFractionDigits:digits,maximumFractionDigits:digits});
const months = ['','Ocak','Şubat','Mart','Nisan','Mayıs','Haziran','Temmuz','Ağustos','Eylül','Ekim','Kasım','Aralık'];
const statuses = {running:'Çalışıyor',stopped:'Duruş',maintenance:'Bakım',fault:'Arıza',nodata:'Veri yok',open:'Açık',planned:'Planlandı',waiting:'Bekliyor',closed:'Kapalı','Açık':'Açık','Kapalı':'Kapalı','İnceleniyor':'İnceleniyor'};
const icons = {overview:'<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',turbines:'<path d="M12 8v14M12 8 5 3M12 8l8-3M12 8l-3 8"/><circle cx="12" cy="8" r="2"/>',performance:'<path d="M3 17 8 12l5 3 8-10M3 3v18h18"/>',analysis:'<path d="M4 19V5m0 14h16M8 15V9m4 6V7m4 8v-3"/>',faults:'<path d="m12 3 10 18H2L12 3Z"/><path d="M12 9v5m0 3v1"/>',work:'<path d="m9 3-1 3-3 1-2 5 2 5 3 1 1 3h6l1-3 3-1 2-5-2-5-3-1-1-3Z"/><circle cx="12" cy="12" r="3"/>',shift:'<path d="M4 3h16v18H4zM8 8h8M8 12h8M8 16h6"/>',reports:'<path d="M4 3h16v18H4zM8 17v-4m4 4V9m4 8V6"/>',docs:'<path d="M5 3h9l5 5v13H5zM14 3v6h5M8 13h8m-8 4h6"/>',alerts:'<path d="M18 8a6 6 0 0 0-12 0c0 8-3 8-3 10h18c0-2-3-2-3-10M10 21h4"/>',sources:'<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 4 16 4 16 0V5M4 12c0 4 16 4 16 0"/>',permits:'<path d="M5 4h14v18H5zM9 2h6v4H9zM8 13l3 3 5-6"/>',finance:'<path d="M4 19V5m0 14h16M7 15l3-4 3 2 4-6"/>',import:'<path d="M12 3v12m0 0 4-4m-4 4-4-4M5 21h14"/>'};
const pageTitles = {overview:'Genel Bakış',turbines:'Türbin İzleme',performance:'Üretim & Performans',analysis:'Yıllık ve Mevsimsel Analiz',alerts:'Anomali & Bildirimler',faults:'Arıza Yönetimi',work:'Bakım & İş Emirleri',shift:'Vardiya Defteri',permits:'İş İzinleri',org:'Organizasyon',reports:'Raporlar',docs:'Dokümanlar',sources:'Veri Kaynakları & Tag Eşleme',forecast:'Üretim Tahmini',finance:'Enerji mutabakatı',import:'Excel aktarımı'};
function icon(name){return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">${icons[name]||icons.overview}</svg>`;}
function toast(text){const el=$('#toast');el.textContent=text;el.classList.remove('hidden');clearTimeout(toast.timer);toast.timer=setTimeout(()=>el.classList.add('hidden'),4200);}
function chip(status){const key=status==='Açık'?'open':status==='Kapalı'?'closed':status==='İnceleniyor'?'planned':status;return `<span class="status-chip ${esc(key)}">${esc(statuses[status]||status)}</span>`;}
function fmtTs(iso){if(!iso)return '—';const s=String(iso).replace('Z','').replace('T',' ');const [d,t='']=s.split(' ');const [y,m,day]=d.split('-').map(Number);if(!y)return esc(iso);return `${day} ${months[m]||m} ${y}${t?' '+t.slice(0,5):''}`;}
function heading(title,subtitle,action=''){return `<div class="page-heading"><div><h1>${title}</h1><p>${subtitle}</p></div>${action}</div>`;}
function table(headers,rows){return `<div class="table-wrap"><table><thead><tr>${headers.map(h=>`<th scope="col">${h}</th>`).join('')}</tr></thead><tbody>${rows.length?rows.join(''):`<tr><td colspan="${headers.length}">Kayıt yok.</td></tr>`}</tbody></table></div>`;}

const state = {mode:null,me:null,plants:[],executive:false,view:'portfolio',plantCode:null,page:'overview',live:null,turbineView:'cards',turbineFilter:'all',turbineSearch:'',period:'today',focus:null};

async function api(path, opts={}){
  const headers = opts.body && !(opts.body instanceof FormData) ? {'Content-Type':'application/json',...(opts.headers||{})} : (opts.headers||{});
  const res = await fetch(path, {credentials:'same-origin', ...opts, headers});
  if(res.status===401){showLogin();const err=new Error('auth');err.auth=true;throw err;}
  const text = await res.text();
  let data=null; try{data=text?JSON.parse(text):null;}catch{data={error:text};}
  if(!res.ok){const msg=data?.error||(data?.errors||[]).join(' ')||'İstek başarısız.';throw new Error(msg);}
  return data;
}

function showLogin(){$('#login').classList.remove('hidden');$('#app').classList.add('hidden');}
function showApp(){$('#login').classList.add('hidden');$('#app').classList.remove('hidden');}

function setupLogin(mode){
  state.mode=mode;
  const dev=mode.mode==='Dev';
  $('#dev-form').classList.toggle('hidden', !dev);
  $('#entra-login').classList.toggle('hidden', dev);
  if(dev){
    $('#login-lead').textContent='Yerel geliştirme girişi. Parola ortamda Auth__DevPassword ile değişir.';
    $('#login-note').textContent='Bu anahtar yalnız geliştirme içindir. Üretimde Auth__Mode=Entra olur. Salt okunur izleme; kumanda yoktur.';
    $('#login-hint').innerHTML='<strong>Yerel:</strong> deniz ve ahmet tüm santraller, fatih yalnız Gaziosmanpaşa RES. Geliştirme parolası README’de.';
  } else if(!mode.entraReady){
    $('#login-lead').textContent='Kurumsal giriş henüz yapılandırılmadı.';
    $('#login-note').textContent='AzureAd__TenantId, AzureAd__ClientId ve AzureAd__ClientSecret App Settings’e yazılınca Microsoft ile giriş açılır. Ingest anahtarı ayrıdır ve bu ekranı beklemez.';
    $('#entra-login').classList.add('hidden');
  } else {
    $('#login-lead').textContent='ARTIBIR GRUP hesabınla giriş yap.';
    $('#login-note').textContent='Yetki, Entra gruplarındadır. Üst yönetim tüm santralleri, santral grubu yalnız kendi santralini görür. Finans sayfası yalnız üst yönetimdedir.';
    $('#login-hint').textContent='Grup talebi gelmezse hiç santral görünmez. Oturumu kapatıp yeniden girmek grup eşlemesini yeniler.';
  }
}

async function devLogin(){
  $('#login-error').textContent='';
  try{
    const res=await fetch('/api/auth/dev-login',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:$('#demo-name').value,password:$('#demo-pass').value})});
    const data=await res.json().catch(()=>({}));
    if(!res.ok){$('#login-error').textContent=data.error||'Giriş yapılamadı.';return;}
    await enter();
  }catch(ex){$('#login-error').textContent=ex.message||'Giriş yapılamadı.';}
}

async function enter(){
  state.me=await api('/api/me');
  const pack=await api('/api/plants');
  state.plants=pack.plants||[];
  state.executive=!!pack.executive||!!state.me.executive;
  const initials=(state.me.name||'?').split(/\s+/).map(p=>p[0]).join('').slice(0,2).toLocaleUpperCase('tr-TR');
  $('#user-button').textContent=initials||'?';
  $('#user-button').title=`${state.me.name} · oturumu kapat`;
  $('#role-label').textContent=state.executive?'Üst yönetim':'Santral';
  showApp();
  const h=(location.hash||'').replace(/^#/,'');
  if(!h){route(state.executive?'portfolio':(state.plants[0]?.code||'gop'));return;}
  route(h);
}

function plantBy(code){return state.plants.find(p=>p.code===code);}
function canSee(code){return !!plantBy(code);}

function route(hash){
  const parts=(hash||'').split('/').filter(Boolean);
  if(!parts.length||parts[0]==='portfolio'){
    if(!state.executive){
      const own=state.plants[0];
      if(own){state.view='plant';state.plantCode=own.code;state.page='overview';}
      else {state.view='denied';state.plantCode=null;}
    }
    else {state.view='portfolio';state.plantCode=null;}
  } else if(plantBy(parts[0])||['gop','yilmaz','yenikoy','kucukkoy'].includes(parts[0])){
    if(!canSee(parts[0])){state.view='denied';state.plantCode=parts[0];}
    else {state.view='plant';state.plantCode=parts[0];state.page=pageTitles[parts[1]]?parts[1]:'overview';}
  } else if(pageTitles[parts[0]]&&state.plants[0]){
    state.view='plant';state.plantCode=state.plants[0].code;state.page=parts[0];
  }
  const next=state.view==='portfolio'?'portfolio':state.view==='denied'?(state.plantCode||state.plants[0]?.code||'gop'):(state.plantCode+'/'+(state.page==='overview'?'':state.page)).replace(/\/$/,'');
  if((location.hash||'#')!=='#'+next){try{history.replaceState(null,'','#'+next);}catch(e){}}
  render();
}

function navItem(id,title,active){return `<button class="nav-item ${active?'active':''}" data-page="${id}" ${active?'aria-current="page"':''}>${icon(id)}<span>${title}</span></button>`;}

function renderNav(){
  if(state.view==='portfolio'){
    $('#nav').innerHTML=`<div class="nav-group">PORTFÖY</div>${navItem('overview','Portföy özeti',true)}`;
    return;
  }
  const p=plantBy(state.plantCode);
  const back=state.executive?`<button class="secondary back-btn" data-portfolio="1">← Portföye dön</button>`:'';
  if(!p||p.sampleOnly){
    $('#nav').innerHTML=back+`<div class="nav-group">${esc((p?.name||'Santral').toLocaleUpperCase('tr-TR'))}</div>${navItem('overview','Ana sayfa',true)}`;
    return;
  }
  const exec=state.executive;
  const groups=[
    ['GENEL',[['overview','Genel Bakış'],['turbines','Türbin İzleme'],['performance','Üretim & Performans'],['analysis','Yıllık ve Mevsimsel Analiz'],['forecast','Üretim Tahmini']]],
    ['İŞLETME',[['alerts','Anomali & Bildirimler'],['faults','Arıza Yönetimi'],['shift','Vardiya Defteri'],['work','Bakım & İş Emirleri'],['permits','İş İzinleri'],['org','Organizasyon']]],
    ['YÖNETİM',[['reports','Raporlar'],['docs','Dokümanlar'],['sources','Veri Kaynakları & Tag Eşleme'],...(exec?[['finance','Enerji mutabakatı'],['import','Excel aktarımı']]:[])]]
  ];
  $('#nav').innerHTML=back+groups.map(([g,items])=>`<div class="nav-group">${g}</div>${items.map(([id,title])=>navItem(id,title,state.page===id)).join('')}`).join('');
}

function chrome(){
  const p=plantBy(state.plantCode);
  if(state.executive) $('#side-brand').innerHTML='İŞLETME<br>YÖNETİM<small>4 santral · 3 RES 1 GES</small>';
  else if(p) $('#side-brand').innerHTML=`${esc(p.name.toLocaleUpperCase('tr-TR'))}<small>İşletme Yönetim Sistemi</small>`;
  const live=state.live;
  const snap=live?.snapshot;
  const pill=$('#live-pill');
  if(state.view==='plant'&&p&&!p.sampleOnly&&snap){
    pill.innerHTML=`<i class="dot"></i> ${snap.stale?'Son okuma':'Canlı'} ${fmtTs(snap.readerLocalTime).split(' ').slice(-1)}`;
  } else if(state.view==='plant'&&p?.sampleOnly){
    pill.innerHTML='<i class="dot"></i> Örnek veri';
  } else pill.innerHTML='<i class="dot"></i> İzleme';
  const prefix=$('#crumb-prefix');
  if(state.executive&&state.view!=='portfolio') prefix.innerHTML=`<a href="#portfolio" class="link-button">Portföy</a> <span>/</span> ${p?esc(p.name)+' <span>/</span> ':''}`;
  else if(p&&state.view==='plant') prefix.innerHTML=`${esc(p.name)} <span>/</span> `;
  else prefix.innerHTML='';
  $('#breadcrumb').textContent=state.view==='portfolio'?'Portföy özeti':state.view==='denied'?'Yetkisiz':(pageTitles[state.page]||'Ana sayfa');
  $('#foot-plant').textContent=state.view==='portfolio'?'Portföy':(p?.name||'Yetkisiz');
  $('#foot-meta').textContent=snap?`Son okuma ${fmtTs(snap.readerLocalTime)} · ${esc(snap.server||'')} · ${snap.goodCount??'—'}/${snap.readCount??'—'} Good`:'Salt okunur · uzaktan kumanda yok';
  document.title=(state.view==='portfolio'?'Portföy':p?.name||'İzleme')+' · Artıbir Enerji';
}

async function render(){
  renderNav();
  chrome();
  const main=$('#main');
  try{
    if(state.view==='denied'){main.innerHTML=denied();return;}
    if(state.view==='portfolio'){main.innerHTML=portfolio();return;}
    const p=plantBy(state.plantCode);
    if(!p){main.innerHTML=`<section class="card denied"><h2>Santral bulunamadı</h2></section>`;return;}
    if(p.sampleOnly){main.innerHTML=samplePage(p);return;}
    if(['overview','turbines','performance'].includes(state.page)) await ensureLive();
    const screens={overview:overview,turbines:turbinesPage,performance:()=>performancePage(),analysis:()=>analysisPage(),faults:()=>faultsPage(),shift:()=>shiftPage(),sources:()=>sourcesPage(),finance:()=>financePage(),import:()=>importPage(),forecast:infoForecast,alerts:infoAlerts,work:infoWork,permits:infoPlain('İş İzinleri','İş izni bu sürümde uzaktan açılmaz. Sahadaki izin süreci değişmez.'),org:infoPlain('Organizasyon','Kadrolar bu sürümde tutulmuyor. Kişisel veri örneği gösterilmez.'),reports:infoPlain('Raporlar','Hazır rapor paketi yok. Üretim ve mutabakat ekranları veritabanındaki kayıtlardan gelir.'),docs:infoPlain('Dokümanlar','Belge arşivi bu sürümde yok.')};
    const html=await screens[state.page]();
    main.innerHTML=strip()+html;
    bindCharts();
  }catch(ex){
    if(ex.auth)return;
    main.innerHTML=`<section class="card"><div class="card-content"><p class="danger">${esc(ex.message)}</p></div></section>`;
  }
  $('#sidebar').classList.remove('open');$('#scrim').classList.add('hidden');
}

function strip(){
  const p=plantBy(state.plantCode);
  const live=state.live;
  if(state.page==='forecast') return `<div class="sample-strip"><span class="source-tag">örnek veri</span> Üretim tahmini bu sürümde yok. Model rakamı gösterilmez.${live?.windMin!=null?` Anlık rüzgâr ${fmt(live.windMin)}–${fmt(live.windMax)} m/s.`:''}</div>`;
  if(state.page==='analysis'||state.page==='finance'||state.page==='import') return `<div class="sample-strip"><span class="source-tag hist">Excel · saatlik üretim</span> TÜRBİN DATA aktarımı · Türkiye saati</div>`;
  if(!live?.snapshot) return `<div class="sample-strip"><span class="source-tag">OPC-DA</span> ${esc(p.name)} için henüz okuma yok. Okuyucu <code>--post</code> ile bağlanınca veriler burada görünür.</div>`;
  const stale=live.snapshot.stale?' · gecikmiş':'';
  return `<div class="sample-strip"><span class="source-tag">OPC-DA · santral okuması</span> ${fmtTs(live.snapshot.readerLocalTime)}${stale} · ${esc(live.snapshot.method||'')} · ${live.snapshot.goodCount}/${live.snapshot.readCount} Good</div>`;
}

async function ensureLive(){
  if(state.live && state.live.plant?.code===state.plantCode && state.live._at && Date.now()-state.live._at<8000) return;
  const data=await api('/api/plants/'+state.plantCode+'/live');
  data._at=Date.now();
  state.live=data;
  chrome();
}

function portfolio(){
  const cards=state.plants.map(p=>{
    const ges=p.kind==='GES';
    const live=p.live;
    const sample=p.sampleOnly;
    const loc=p.location && !(p.technology||'').includes(p.location)?' · '+esc(p.location):'';
    const img=ges?'/assets/portfolio-ges.jpg':'/assets/portfolio-res.jpg';
    const mw=live?.mw!=null?fmt(live.mw,2):'—';
    const wind=live?.windMin!=null?`${fmt(live.windMin)}–${fmt(live.windMax)}`:'—';
    const foot=sample?'<span class="status-chip nodata">örnek veri</span>':'';
    const maint=live?.maintenance?`<span class="status-chip maintenance">${live.maintenance} bakım</span>`:'';
    const run=live?`<span class="status-chip running">${live.running}/${live.total} çalışıyor</span>`:'';
    return `<button class="card plant-card ${ges?'ges-card':''}" data-plant="${esc(p.code)}"><div class="plant-visual"><img src="${img}" alt=""></div><h3>${esc(p.name)}<span class="plant-badge ${ges?'ges':'res'}">${esc(p.kind)}</span></h3><div class="plant-meta">${esc(p.technology||'')}${loc}</div><div class="plant-stats"><div><small>Anlık güç</small><b>${mw}</b> MW</div><div><small>${ges?'Durum':'Rüzgâr'}</small><b>${sample?'—':wind}</b> ${ges?'':'m/s'}</div><div><small>Türbin</small><b>${p.turbineCount??'—'}</b></div></div><div class="plant-foot">${sample?foot:(run+' '+maint)}<span class="muted">${sample?'canlı ölçüm yok':'Aç →'}</span></div></button>`;
  }).join('');
  const gop=state.plants.find(p=>p.code==='gop');
  const note=gop?.live?`Gaziosmanpaşa RES POM ${fmt(gop.live.mw,2)} MW. Diğer santraller örnek veridir; portföy toplamına katılmaz.`:'Gaziosmanpaşa RES için henüz ölçüm yok. Örnek santraller toplama katılmaz.';
  return heading('Portföy özeti','Canlı ölçümü olan santral ile örnek kayıtlar ayrı durur')+
    `<section class="card insight-lead focus-card" style="margin-bottom:20px"><div class="card-content"><p style="font-size:15px;margin:0">${note}</p></div></section>`+
    `<div class="greet-head"><h2>Santrallerimiz</h2><span class="muted small">Örnek veri etiketli kartların rakamı yoktur</span></div><div class="plant-grid">${cards}</div>`+
    `<p class="note">Yılmaz RES, Yeniköy RES ve Küçükköy GES için okuyucu bağlı değil. Kartlar yer tutucudur; üretim uydurulmaz.</p>`;
}

function denied(){
  const own=state.plants[0];
  return `<section class="card denied"><h2>Bu santrale yetkin yok</h2><p class="muted">Hesabın yalnız tanımlı olduğun santrali görebilir.</p>${own?`<button class="primary" data-plant="${esc(own.code)}" style="margin-top:12px">${esc(own.name)} sayfasına dön</button>`:''}</section>`;
}

function samplePage(p){
  return heading(p.name,`${p.kind} · ${esc(p.technology||'')} · örnek kayıt`)+
    `<div class="sample-strip"><span class="source-tag">örnek veri</span> Canlı ölçüm bağlı değil. Bu ekranda rakam gösterilmez.</div>`+
    `<section class="card"><div class="card-head"><h3>Bağlantı yok</h3><span class="status-chip nodata">örnek veri</span></div><div class="card-content"><p>${esc(p.name)} kataloğa kayıtlı. OPC okuyucusu ve Excel geçmişi henüz bu santrale bağlanmadı. Portföy toplamına katılmaz.</p></div></section>`;
}

function overview(){
  const live=state.live;
  const pom=live?.pom||{};
  const conn=live?.connection||{};
  const mw=pom.activeKw==null?'—':fmt(pom.activeKw/1000,2);
  const today=live?.today||{};
  const wind=live?.windMin==null?'—':`${fmt(live.windMin)}–${fmt(live.windMax)}`;
  const t=live?.turbines||[];
  const maint=t.filter(x=>x.status==='maintenance').map(x=>x.code).join(', ')||'yok';
  return heading('Genel Bakış', esc(live.plant.name)+' · son santral okuması', `<span class="date-label">${live.snapshot?fmtTs(live.snapshot.readerLocalTime):'Okuma yok'}</span>`)+
    `<section class="card insight-lead focus-card" style="margin-bottom:20px"><div class="card-head"><h3>Kısaca</h3><span class="source-tag">OPC-DA · santral okuması</span></div><div class="card-content"><p>${conn.status?'Bağlantı: '+esc(conn.status)+'. ':''}<strong>${live.running??0} türbin çalışıyor</strong> (durum 3). Bakım (durum 2): <strong>${esc(maint)}</strong>. POM / santral sayacı <strong>${mw} MW</strong>${pom.activeKw!=null?` (${fmt(pom.activeKw)} kW)`:''}. Türbin Grid.Power toplamı ${fmt(live.turbineSumKw)} kW. Rüzgâr ${wind} m/s. ${esc(today.note||'')}</p></div></section>`+
    `<div class="kpis">
      <section class="card kpi focus-card"><div class="kpi-top">Anlık güç <span class="kpi-icon">↗</span></div><div class="kpi-value">${mw} <small>MW</small></div><div class="kpi-foot">POM01.Electrical.ActivePower</div></section>
      <section class="card kpi"><div class="kpi-top">Bugünkü enerji <span class="kpi-icon">ϟ</span></div><div class="kpi-value">${today.mwh==null?'—':fmt(today.mwh,1)} <small>MWh</small></div><div class="kpi-foot">${today.approximate?'yaklaşık POM integrali':'aktarılmış saatler'}</div></section>
      <section class="card kpi"><div class="kpi-top">Çalışan / bakım</div><div class="kpi-value">${live.running??0} <small>/ ${live.maintenance??0}</small></div><div class="kpi-foot">Durum 3 / durum 2 · arıza türetilmez</div></section>
      <section class="card kpi"><div class="kpi-top">Rüzgâr</div><div class="kpi-value" style="font-size:26px">${wind} <small>m/s</small></div><div class="kpi-foot">Okunan türbin aralığı</div></section>
    </div>`+
    `<section class="card"><div class="card-head"><h3>Türbinler</h3><button class="secondary" data-page="turbines">Tümünü aç</button></div>${table(['Türbin','Durum','Güç kW','Rüzgâr','Durum kodu'], t.map(x=>`<tr><td><button class="table-action" data-turbine="${esc(x.code)}">${esc(x.code)}</button></td><td>${chip(x.status)}</td><td>${fmt(x.powerKw)}</td><td>${fmt(x.wind)}</td><td>${x.state??'—'}</td></tr>`))}</section>`;
}

function turbinesPage(){
  const list=(state.live?.turbines||[]).filter(t=>(state.turbineFilter==='all'||t.status===state.turbineFilter)&&t.code.toLowerCase().includes(state.turbineSearch.toLowerCase()));
  const cards=list.map(t=>{
    const bar=Math.max(0,Math.min(100,(t.powerKw||0)/3600*100));
    return `<button class="card turbine-card" data-turbine="${esc(t.code)}"><div class="turbine-top"><strong>${esc(t.code)}</strong>${chip(t.status)}</div><div class="turbine-icon">✳</div><div class="turbine-bottom"><div class="turbine-power">${fmt(t.powerKw,0)} <small>kW</small></div><div class="turbine-wind">${fmt(t.wind)} m/s</div></div><div class="mini-bar"><span style="width:${bar}%"></span></div><div class="turbine-source"><span>${esc(t.wtg)}</span><span>durum ${t.state??'—'}</span></div></button>`;
  }).join('');
  const rows=list.map(t=>`<tr><td><button class="table-action" data-turbine="${esc(t.code)}">${esc(t.code)}</button></td><td>${chip(t.status)}</td><td>${fmt(t.powerKw)}</td><td>${fmt(t.wind)}</td><td>${fmt(t.rpm)}</td><td>${fmt(t.pitch)}</td><td>${fmt(t.bearing,0)}</td></tr>`).join('');
  return heading('Türbin İzleme', `${state.live?.turbines?.length||0} türbin · son okuma`)+
    `<div class="toolbar"><input id="turbine-search" aria-label="Türbin ara" placeholder="Türbin ara…" value="${esc(state.turbineSearch)}"><select id="turbine-filter" aria-label="Türbin durumu">${['all','running','maintenance','stopped','nodata'].map(s=>`<option value="${s}" ${s===state.turbineFilter?'selected':''}>${s==='all'?'Tüm durumlar':statuses[s]}</option>`).join('')}</select><div class="view-toggle"><button data-view="cards" class="${state.turbineView==='cards'?'active':''}" type="button">Kartlar</button><button data-view="table" class="${state.turbineView==='table'?'active':''}" type="button">Liste</button></div></div>`+
    (state.turbineView==='cards'?`<div class="turbine-grid">${cards||'<div class="card empty-result">Eşleşen türbin yok.</div>'}</div>`:`<section class="card">${table(['Türbin','Durum','Güç kW','Rüzgâr','Rotor','Pitch','Ana yatak °C'], rows.split('</tr>').filter(Boolean).map(r=>r+'</tr>'))}</section>`);
}

function openTurbine(code){
  const t=(state.live?.turbines||[]).find(x=>x.code===code);
  if(!t)return;
  $('#modal-root').innerHTML=`<div class="modal-overlay" data-close="1"><div class="modal" role="dialog" aria-modal="true"><div class="modal-head"><h2>${esc(t.code)} · ${esc(t.wtg)}</h2><button class="secondary" data-close="1" type="button">Kapat</button></div><div class="modal-body"><dl class="modal-detail"><div><dt>Durum</dt><dd>${chip(t.status)} <span class="muted">${t.state??'—'}</span></dd></div><div><dt>Güç</dt><dd>${fmt(t.powerKw)} kW</dd></div><div><dt>Rüzgâr</dt><dd>${fmt(t.wind)} m/s · ${fmt(t.windDir,0)}°</dd></div><div><dt>Rotor / pitch</dt><dd>${fmt(t.rpm)} d/d · ${fmt(t.pitch)}°</dd></div><div><dt>Ana yatak RE</dt><dd>${fmt(t.bearing,0)} °C</dd></div><div><dt>Dış hava / nacelle</dt><dd>${fmt(t.ambient,0)} / ${fmt(t.nacelle,0)} °C</dd></div><div><dt>Olası güç</dt><dd>${fmt(t.possibleKw)} kW</dd></div></dl><p class="note">OperationStateInt 2 bakım, 3 çalışıyor. Kötü kalite arıza kaydı açmaz. Bu pencereden komut gönderilmez.</p></div></div></div>`;
}

async function performancePage(){
  const period=state.period;
  const powerish=period==='today'||period==='week';
  let body='';
  if(powerish){
    const data=await api(`/api/plants/${state.plantCode}/power?range=${period}&resolution=${period==='today'?'10min':'hour'}`);
    body=data.error?`<p class="note">${esc(data.error)}</p>`:chartCard(period==='today'?'Gün içi güç':'Son 7 gün güç', data.note, (data.points||[]).map(p=>({t:p.t,v:p.value/1000})), 'MW');
  } else {
    const bucket=period==='month'?'day':period==='year'?'month':'year';
    const data=await api(`/api/plants/${state.plantCode}/production?range=${period}&bucket=${bucket}`);
    const meter=(data.series||[]).find(s=>s.series==='meter')||(data.series||[]).find(s=>s.series==='turbine_total');
    body=chartCard('Üretim', data.note, (meter?.points||[]).map(p=>({t:p.t,v:p.mwh})), 'MWh');
  }
  const avail=await api(`/api/plants/${state.plantCode}/availability?range=${period==='years'?'year':period}`);
  const bars=(state.live?.turbines||[]).map(t=>`<div class="horizontal-bar"><span>${esc(t.code)}</span><div><i style="width:${Math.max(0,Math.min(100,(t.powerKw||0)/3600*100))}%"></i></div><strong>${fmt(t.powerKw,0)}</strong></div>`).join('');
  const availRows=(avail.turbines||[]).map(t=>`<tr><td>${esc(t.code)}</td><td>${t.runningRatio==null?'—':fmt(t.runningRatio*100,1)+' %'}</td><td>${t.maintenanceRatio==null?'—':fmt(t.maintenanceRatio*100,1)+' %'}</td><td>${t.samples}</td></tr>`).join('');
  return heading('Üretim & Performans','POM güç serisi ve aktarılmış saatlik üretim')+
    `<div class="toolbar"><div class="view-toggle">${[['today','Bugün'],['week','Son 7 gün'],['month','Son 30 gün'],['year','Son 12 ay'],['years','Yıllar']].map(([id,label])=>`<button type="button" data-period="${id}" class="${period===id?'active':''}">${label}</button>`).join('')}</div></div>`+
    body+
    `<div class="grid-two equal"><section class="card"><div class="card-head"><h3>Türbin anlık güç</h3><span class="muted">kW · görsel ölçek</span></div><div class="card-content">${bars||'<p class="note">Anlık güç yok.</p>'}</div></section><section class="card"><div class="card-head"><h3>Çalışır durum oranı</h3></div><div class="card-content"><p class="note">${esc(avail.definition||'')} Santral: ${avail.plantRunningRatio==null?'—':fmt(avail.plantRunningRatio*100,1)+' %'}.</p></div>${table(['Türbin','Çalışır','Bakım','Örnek'], availRows?availRows.split('</tr>').filter(Boolean).map(r=>r+'</tr>'):[])}</section></div>`;
}

function chartCard(title, note, points, unit){
  return `<section class="card" style="margin-bottom:20px"><div class="card-head"><div><h3>${title}</h3><p>${esc(note||'')}</p></div><span class="pill">${esc(unit)}</span></div><div class="card-content">${lineChart(points, unit)}</div></section>`;
}

function lineChart(points, unit){
  const vals=points.filter(p=>p.v!=null&&!Number.isNaN(p.v));
  if(!vals.length) return `<p class="note">Bu aralıkta çizilecek yeterli nokta yok.</p>`;
  if(vals.length===1) return `<p class="kpi-value" style="margin:8px 0">${fmt(vals[0].v,1)} <small>${esc(unit)}</small></p><p class="note">${esc(String(vals[0].t).replace('T',' ').slice(0,16))}</p>`;
  const W=680,H=230,L=46,R=12,T=16,B=32;
  const max=Math.max(...vals.map(p=>p.v),0.001)*1.15;
  const min=Math.min(0,...vals.map(p=>p.v));
  const span=max-min||1;
  const px=i=>L+i*(W-L-R)/Math.max(vals.length-1,1);
  const py=v=>T+(H-T-B)*(1-(v-min)/span);
  const pts=vals.map((p,i)=>`${px(i)},${py(p.v)}`).join(' ');
  const yTicks=[0,0.5,1].map(f=>{const v=min+span*f;const y=py(v);return `<line class="grid" x1="${L}" x2="${W-R}" y1="${y}" y2="${y}"/><text x="${L-6}" y="${y+4}" text-anchor="end">${fmt(v,v<10?1:0)}</text>`;}).join('');
  const step=Math.ceil(vals.length/6);
  const labels=vals.map((p,i)=>{if(i%step!==0)return '';const lab=String(p.t);const short=lab.length>12?lab.slice(5,16).replace('T',' '):lab;return `<text x="${px(i)}" y="${H-8}" text-anchor="middle">${esc(short)}</text>`;}).join('');
  return `<svg class="chart" viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(unit)} grafiği"><defs><linearGradient id="area-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#32d6a0" stop-opacity=".22"/><stop offset="100%" stop-color="#32d6a0" stop-opacity="0"/></linearGradient></defs>${yTicks}<polygon class="area" points="${L},${H-B} ${pts} ${px(vals.length-1)},${H-B}"/><polyline class="line" points="${pts}"/>${labels}</svg>`;
}

async function analysisPage(){
  const data=await api(`/api/plants/${state.plantCode}/production?range=years&bucket=month`);
  const years=await api(`/api/plants/${state.plantCode}/production?range=years&bucket=year`);
  const pick=list=> (list||[]).find(s=>s.series==='meter')||(list||[]).find(s=>s.series==='turbine_total');
  const monthly=pick(data.series);
  const yearly=pick(years.series);
  const byYear={};
  (monthly?.points||[]).forEach(p=>{const y=String(p.t).slice(0,4);(byYear[y]||=[]).push(p);});
  const yearKeys=Object.keys(byYear);
  if(!state.focus) state.focus=yearKeys.at(-1)||'all';
  const shown=state.focus==='all'?(monthly?.points||[]):(byYear[state.focus]||[]);
  const rows=shown.map(p=>`<tr><td>${fmtTs(p.t).replace(/\s+\d{2}:\d{2}$/,'')}</td><td>${fmt(p.mwh,1)}</td></tr>`);
  return heading('Yıllık ve Mevsimsel Analiz','Aktarılmış saatlik üretimin ay ve yıl toplamı')+
    `<div class="toolbar"><div class="view-toggle">${['all',...yearKeys].map(y=>`<button type="button" data-year="${y}" class="${state.focus===y?'active':''}">${y==='all'?'Tümü':y}</button>`).join('')}</div></div>`+
    chartCard('Yıllık üretim', years.note, (yearly?.points||[]).map(p=>({t:String(p.t).slice(0,4),v:p.mwh})), 'MWh')+
    `<section class="card"><div class="card-head"><h3>Aylık ${state.focus==='all'?'':esc(state.focus)}</h3><span class="muted">${monthly?'sayaç veya türbin toplamı':'veri yok'}</span></div>${table(['Dönem','MWh'], rows)}</section>`+
    `<p class="note">Rüzgâr ve Vestas availability bu Excel sayfasında yok; uydurulmaz. Çalışır durum oranı Üretim ekranındadır ve yalnız okunan aralık için geçerlidir.</p>`;
}

async function faultsPage(){return logPage(false);}
async function shiftPage(){return logPage(true);}

async function logPage(shifts){
  const path=shifts?'shifts':'faults';
  const data=await api(`/api/plants/${state.plantCode}/`+path);
  const rows=(data.entries||[]).map(e=>`<tr><td>${esc(e.display)}</td><td>${esc(e.turbineCode||'Santral')}</td><td>${fmtTs(e.occurredAt)}</td><td>${esc(e.description)}</td><td>${esc(e.authorName||e.responsible||'')}</td><td>${chip(e.status)}</td><td><button class="table-action" data-status="${esc(path)}" data-id="${e.id}" data-next="${e.status==='Kapalı'?'Açık':'Kapalı'}" type="button">${e.status==='Kapalı'?'Aç':'Kapat'}</button></td></tr>`);
  const title=shifts?'Vardiya Defteri':'Arıza Yönetimi';
  return heading(title, shifts?'Saha notu. Kumanda komutu değildir.':'Kayıt defteri. OPC durumundan arıza türetilmez.')+
    `<section class="card" style="margin-bottom:16px"><div class="card-head"><h3>Yeni kayıt</h3></div><div class="card-content"><form data-log="${path}" class="form-grid">
      <div class="field"><label>Türbin</label><input name="turbineCode" placeholder="T07, Santral…" maxlength="32"></div>
      <div class="field"><label>Zaman (TR)</label><input name="occurredAt" placeholder="2026-11-06 14:36" required></div>
      <div class="field" style="grid-column:1/-1"><label>Açıklama</label><input name="description" required maxlength="2000"></div>
      <div class="field"><label>${shifts?'Yazan':'Sorumlu'}</label><input name="authorName" maxlength="160"></div>
      <div class="field"><label>Durum</label><select name="status"><option>Açık</option><option>İnceleniyor</option><option>Kapalı</option></select></div>
      <div style="grid-column:1/-1"><button class="primary" type="submit">Kaydet</button></div>
    </form><p class="note">CSV aktarımı: sütunlar türbin;tarih;açıklama;sorumlu;durum. <input type="file" data-csv="${path}" accept=".csv,text/csv"></p></div></section>`+
    `<section class="card"><div class="card-head"><h3>Kayıtlar</h3><span class="muted">${(data.entries||[]).length}</span></div>${table(['Kayıt','Ekipman','Zaman','Açıklama',shifts?'Yazan':'Sorumlu','Durum',''], rows)}</section>`;
}

async function sourcesPage(){
  const data=await api('/api/plants/'+state.plantCode+'/signals');
  const q=(state.turbineSearch||'').toLowerCase();
  const rows=(data.signals||[]).filter(s=>!q||s.itemId.toLowerCase().includes(q)).slice(0,400).map(s=>`<tr><td><code>${esc(s.itemId)}</code></td><td>${esc(s.signalName)}</td><td>${esc(s.unit||'')}</td><td>${esc(s.value??'—')}</td><td>${s.capturedAt?chip(s.good?'running':'nodata'):'—'}</td><td>${fmtTs(s.capturedAt)}</td></tr>`);
  return heading('Veri Kaynakları & Tag Eşleme', `${data.count||0} etiket · ingest ile büyür`)+
    `<div class="toolbar"><input id="signal-search" placeholder="Etiket ara…" value="${esc(state.turbineSearch)}"></div>`+
    `<section class="card">${table(['Item','Sinyal','Birim','Son değer','Kalite','Zaman'], rows)}</section>`+
    `<p class="note">OperationStateInt: 2 bakımda, 3 çalışıyor. POM sayacı <code>OZYEL.POM01.Electrical.ActivePower</code>. Eşleme tablosu OPC’ye yazmaz.</p>`;
}

async function financePage(){
  const data=await api('/api/plants/'+state.plantCode+'/finance');
  const rows=(data.months||[]).map(m=>`<tr><td>${months[m.month]} ${m.year}</td><td>${fmt(m.meterMwh,1)}</td><td>${fmt(m.turbineTotalMwh,1)}</td><td>${fmt(m.differenceMwh,1)}</td></tr>`);
  return heading('Enerji mutabakatı','Sayaç, türbin toplamı ve fark · yalnız üst yönetim')+
    `<section class="card"><div class="card-head"><h3>Aylık MWh</h3></div>${table(['Ay','Sayaç','Türbin toplam','Fark'], rows)}</section>`+
    `<p class="note">${esc(data.note||'')}</p>`;
}

function importPage(){
  const options=state.plants.filter(p=>!p.sampleOnly||p.code==='gop'||state.executive).map(p=>`<option value="${esc(p.code)}">${esc(p.name)}</option>`).join('');
  return heading('Excel aktarımı','Aylık işletme defteri · TÜRBİN DATA')+
    `<section class="card"><div class="card-content"><form id="import-form"><div class="form-grid"><div class="field"><label>Santral</label><select name="plant">${options}</select></div><div class="field"><label>Beklenen yıl</label><input name="year" inputmode="numeric" placeholder="2025"></div><div class="field"><label>Beklenen ay</label><input name="month" inputmode="numeric" placeholder="1"></div></div><label class="upload-zone"><strong>Aylık dosyaları seç</strong><span>Birden fazla .xlsx · 4–5 yıl tek seferde seçilebilir</span><input type="file" name="files" accept=".xlsx,.xls" multiple required></label><button class="primary" style="margin-top:14px" type="submit">Aktar</button></form><div id="import-result"></div><p class="note">Tarih yalnız günün ilk satırında olabilir. SAAT bazen saat, bazen tarih-saat olarak durur. FARK formülü hesaplı değilse sayaç farkından MWh üretilir. Aynı dosya özeti tekrar aktarılmaz. Ay uyuşmazlığı kaydı silmez; uyarı olarak işaretlenir.</p></div></section>`;
}

function infoForecast(){return heading('Üretim Tahmini','Model bu sürümde yok')+`<section class="card"><div class="card-content"><p>Örnek hava ve fiziksel model rakamları gösterilmez. Anlık rüzgâr türbin kartlarındadır.</p></div></section>`;}
function infoAlerts(){return heading('Anomali & Bildirimler','Doğrulanmış kural motoru yok')+`<div class="notification-note"><strong>Bakım, alarm değildir.</strong><p>Durum 2 bakım kaydıdır. Kötü OPC kalitesi tek başına arıza açmaz. Arıza defteri elle veya CSV ile tutulur.</p></div>`;}
function infoWork(){return heading('Bakım & İş Emirleri','Uzaktan iş emri yok')+`<section class="card"><div class="card-content"><p>Bu sürüm iş emri oluşturmaz ve sahaya komut göndermez. Vardiya ve arıza notları kendi ekranlarında durur.</p></div></section>`;}
function infoPlain(title,text){return heading(title,'')+`<section class="card"><div class="card-content"><p>${esc(text)}</p></div></section>`;}

function bindCharts(){}

document.addEventListener('click', async e=>{
  const t=e.target.closest('[data-page],[data-plant],[data-portfolio],[data-view],[data-period],[data-year],[data-turbine],[data-close],[data-status],#enter-demo,#user-button,#menu-toggle,#scrim');
  if(!t)return;
  if(t.id==='enter-demo'){devLogin();return;}
  if(t.id==='user-button'){location.href='/api/auth/signout';return;}
  if(t.id==='menu-toggle'){$('#sidebar').classList.add('open');$('#scrim').classList.remove('hidden');return;}
  if(t.id==='scrim'||t.dataset.close){$('#sidebar').classList.remove('open');$('#scrim').classList.add('hidden');if(t.dataset.close)$('#modal-root').innerHTML='';return;}
  if(t.dataset.portfolio){route('portfolio');return;}
  if(t.dataset.plant){route(t.dataset.plant);return;}
  if(t.dataset.page){if(state.view!=='plant'){route('portfolio');return;}state.page=t.dataset.page;route(state.plantCode+'/'+state.page);return;}
  if(t.dataset.view){state.turbineView=t.dataset.view;render();return;}
  if(t.dataset.period){state.period=t.dataset.period;render();return;}
  if(t.dataset.year){state.focus=t.dataset.year;render();return;}
  if(t.dataset.turbine){openTurbine(t.dataset.turbine);return;}
  if(t.dataset.status){
    try{await api(`/api/plants/${state.plantCode}/${t.dataset.status}/${t.dataset.id}`,{method:'PATCH',body:JSON.stringify({status:t.dataset.next})});toast('Durum güncellendi');render();}
    catch(ex){if(!ex.auth)toast(ex.message);}
  }
});

document.addEventListener('change', e=>{
  if(e.target.id==='turbine-filter'){state.turbineFilter=e.target.value;render();}
  if(e.target.id==='signal-search'){state.turbineSearch=e.target.value;render();}
});
document.addEventListener('input', e=>{
  if(e.target.id==='turbine-search'||e.target.id==='signal-search'){
    state.turbineSearch=e.target.value;
    const id=e.target.id, pos=e.target.selectionStart;
    render().then(()=>{const el=$('#'+id);if(el){el.focus();try{el.setSelectionRange(pos,pos);}catch(err){}}});
  }
});

document.addEventListener('submit', async e=>{
  const form=e.target;
  if(form.dataset.log){
    e.preventDefault();
    const body=Object.fromEntries(new FormData(form).entries());
    try{await api(`/api/plants/${state.plantCode}/${form.dataset.log}`,{method:'POST',body:JSON.stringify(body)});toast('Kaydedildi');render();}
    catch(ex){if(!ex.auth)toast(ex.message);}
    return;
  }
  if(form.id==='import-form'){
    e.preventDefault();
    const fd=new FormData();
    const plant=form.plant.value, year=form.year.value, month=form.month.value;
    fd.set('plant', plant);
    if(year)fd.set('year', year);
    if(month)fd.set('month', month);
    for(const file of form.files.files)fd.append('files', file);
    const box=$('#import-result');
    box.innerHTML='<p class="note">Aktarılıyor…</p>';
    try{
      const res=await fetch('/api/admin/excel',{method:'POST',body:fd,credentials:'same-origin'});
      const data=await res.json();
      if(!res.ok) throw new Error(data.error||'Aktarım başarısız');
      const rows=(data.results||[]).map(r=>`<tr><td>${esc(r.file)}</td><td>${r.imported?'Aktarıldı':r.skippedDuplicate?'Aynı dosya':esc(r.error||'Olmadı')}</td><td>${r.rows||0}</td><td>${r.year||'—'}-${r.month||'—'}</td><td>${r.mismatch?'Uyuşmazlık':'Uyumlu'}</td><td>${esc((r.warnings||[]).join(' '))}</td></tr>`);
      box.innerHTML=`<div class="import-preview">${table(['Dosya','Sonuç','Satır','Dönem','Kontrol','Not'], rows)}</div>`;
      toast('Aktarım bitti');
    }catch(ex){box.innerHTML=`<div class="import-errors"><p>${esc(ex.message)}</p></div>`;}
  }
});

document.addEventListener('change', async e=>{
  const input=e.target;
  if(!input.dataset||!input.dataset.csv||!input.files?.[0])return;
  const text=await input.files[0].text();
  const lines=parseCsv(text);
  const body=lines.map(cols=>({turbineCode:cols[0],occurredAt:cols[1],description:cols[2],authorName:cols[3],status:cols[4]||'Açık'}));
  try{const res=await api(`/api/plants/${state.plantCode}/${input.dataset.csv}/bulk`,{method:'POST',body:JSON.stringify(body)});toast(`${res.created} satır aktarıldı`);render();}
  catch(ex){if(!ex.auth)toast(ex.message);}
});

function parseCsv(text){
  const rows=[];let row=[],cur='',q=false;
  const s=text.replace(/^\uFEFF/,'');
  for(let i=0;i<s.length;i++){
    const c=s[i];
    if(q){if(c==='"'){if(s[i+1]==='"'){cur+='"';i++;}else q=false;}else cur+=c;continue;}
    if(c==='"'){q=true;continue;}
    if(c===';'||c===','){row.push(cur.trim());cur='';continue;}
    if(c==='\n'){row.push(cur.trim());if(row.some(Boolean))rows.push(row);row=[];cur='';continue;}
    if(c!=='\r')cur+=c;
  }
  row.push(cur.trim());if(row.some(Boolean))rows.push(row);
  if(rows.length&&/tarih|açıklama|aciklama/i.test(rows[0].join(' ')))rows.shift();
  return rows.filter(r=>r.length>=3);
}

window.addEventListener('hashchange',()=>{if(state.me)route((location.hash||'').replace(/^#/,''));});
document.addEventListener('keydown',e=>{
  if(e.key!=='Escape')return;
  $('#modal-root').innerHTML='';
  $('#sidebar').classList.remove('open');
  $('#scrim').classList.add('hidden');
});

setInterval(()=>{
  if(!state.me||state.view!=='plant')return;
  if(!['overview','turbines'].includes(state.page))return;
  state.live=null;
  render();
}, 20000);

fetch('/api/auth/mode').then(r=>r.json()).then(async mode=>{
  setupLogin(mode);
  const me=await fetch('/api/me',{credentials:'same-origin'});
  if(me.status===401){showLogin();return;}
  await enter();
}).catch(()=>showLogin());
