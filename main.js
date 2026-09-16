function emptyItem(){ return {title:'',beco:'',ref:'',photo:'',price:'',ek:'',quickService:false}; }
function emptyPage(name='Neue Kategorie'){ return {name,items:Array.from({length:15},()=>emptyItem())}; }

let data={pages:[emptyPage('Kategorie 1')],library:{}};
let currentPage=0;
let currentItem=null;
let dragSourceIndex=null;

const sheet=document.getElementById('sheet');
const modal=document.getElementById('modal');
const pageSelect=document.getElementById('pageSelect');
const printMenu=document.getElementById('printMenu');
const infoModal=document.getElementById('infoModal');

function openModal(el){
  el.style.display='flex';
  const box=el.querySelector('.modal-box,.info-box,.photo-picker-box,.custom-dialog-box');
  if(box){box.style.animation='none';box.offsetHeight;box.style.animation='';}
}

function esc(s=''){
  return s.replaceAll('&','&amp;')
          .replaceAll('<','&lt;')
          .replaceAll('>','&gt;')
          .replaceAll('"','&quot;')
          .replaceAll("'","&#39;");
}
function qr(ref){
  return 'https://api.qrserver.com/v1/create-qr-code/?size=140x140&data='+encodeURIComponent(ref);
}
function items(){ return data.pages[currentPage].items; }

function resolvePhoto(key){
  if(!key) return '';
  return data.library[key]||'';
}
function addToLibrary(dataUrl){
  for(const [k,v] of Object.entries(data.library)){
    if(v===dataUrl) return k;
  }
  const key='lib-'+Date.now()+'-'+Math.random().toString(36).slice(2,7);
  data.library[key]=dataUrl;
  return key;
}

function setScale(v){
  document.documentElement.style.setProperty('--preview',v);
  document.getElementById('zoomValue').textContent=Math.round(parseFloat(v)*100)+'%';
}
function refreshPages(){
  pageSelect.innerHTML=data.pages.map((p,i)=>
    `<option value="${i}" ${i===currentPage?'selected':''}>${esc(p.name)}</option>`
  ).join('');
  updateWorkspaceContext();
}
function changePage(v){
  currentPage=parseInt(v,10)||0;
  refreshPages();
  render();
}

async function addPage(){
  const name=await showPrompt('Name der neuen Kategorie?','Neue Kategorie');
  if(name===false) return;
  data.pages.push(emptyPage(name.trim()||'Neue Kategorie'));
  currentPage=data.pages.length-1;
  refreshPages();
  render();
}

const categoryModal=document.getElementById('categoryModal');
function openCategoryModal(){
  closeAllMenus();
  document.getElementById('catEditName').value=data.pages[currentPage].name;
  openModal(categoryModal);
}
function closeCategoryModal(){ categoryModal.style.display='none'; }
async function saveCategoryModal(){
  const name=document.getElementById('catEditName').value.trim();
  if(name) data.pages[currentPage].name=name;
  closeCategoryModal();
  refreshPages();
}
async function deleteCategoryFromModal(){
  if(data.pages.length===1){
    await showAlert('Mindestens eine Kategorie muss bleiben.');
    return;
  }
  const ok=await showDangerConfirm(`Kategorie „${data.pages[currentPage].name}“ löschen?`);
  if(!ok) return;
  data.pages.splice(currentPage,1);
  if(currentPage>=data.pages.length) currentPage=data.pages.length-1;
  closeCategoryModal();
  refreshPages();
  render();
}

function closeAllMenus(){
  printMenu.style.display='none';
}

function togglePrintMenu(e){
  e.stopPropagation();
  const open = printMenu.style.display==='block';
  closeAllMenus();
  printMenu.style.display = open ? 'none' : 'block';
}
function closePrintMenu(){ printMenu.style.display='none'; }

/* Info Popup */
function openInfo(){
  closeAllMenus();
  openModal(infoModal);
}
function closeInfo(){
  infoModal.style.display='none';
}

function exportJson(){
  const blob=new Blob([JSON.stringify(data,null,2)],{type:'application/json'});
  const a=document.createElement('a');
  a.href=URL.createObjectURL(blob);
  a.download='furnituren-daten.json';
  a.click();
  URL.revokeObjectURL(a.href);
}
function importJson(event){
  const file=event.target.files&&event.target.files[0];
  if(!file) return;

  const reader=new FileReader();
  reader.onload=e=>{
    try{
      const parsed=JSON.parse(e.target.result);
      if(!parsed.pages||!Array.isArray(parsed.pages)||!parsed.pages.length) throw new Error();

      const importedLibrary={};
      if(parsed.library&&typeof parsed.library==='object'){
        Object.assign(importedLibrary,parsed.library);
      }

      parsed.pages.forEach(p=>{
        if(!Array.isArray(p.items)) p.items=[];
        while(p.items.length<15) p.items.push(emptyItem());
        p.items=p.items.slice(0,15).map(it=>{
          let photo=it.photo||'';
          // migrate legacy base64 photos into library
          if(photo.startsWith('data:')){
            const existing=Object.keys(importedLibrary).find(k=>importedLibrary[k]===photo);
            if(existing){
              photo=existing;
            } else {
              const key='lib-'+Date.now()+'-'+Math.random().toString(36).slice(2,7);
              importedLibrary[key]=photo;
              photo=key;
            }
          }
          return {title:it.title||'',beco:it.beco||'',ref:it.ref||'',photo,price:it.price||'',ek:it.ek||'',quickService:!!it.quickService};
        });
        p.name=p.name||'Kategorie';
      });

      parsed.library=importedLibrary;
      data=parsed;
      currentPage=0;
      refreshPages();
      render();
    }catch{
      alert('Ungültige JSON-Datei.');
    }
    event.target.value='';
  };
  reader.readAsText(file);
}

function cell(item,i){
  const title=item.title?esc(item.title):'';
  const beco=item.beco?`Beco: ${esc(item.beco)}`:'';

  const photoUrl=resolvePhoto(item.photo);
  const photo=photoUrl
    ? `<img src="${photoUrl}" alt="Foto" style="display:block"><div class="ph" style="display:none">Foto</div>`
    : `<img src="" alt="Foto"><div class="ph">Foto</div>`;

  const qrSource=item.quickService?item.title:item.ref;
  const qrHtml=qrSource
    ? `<div class="visual-slot qr-box"><img src="${qr(qrSource)}" alt="QR"></div><div class="qr-ref">${item.quickService?'':esc(item.ref)}</div>`
    : `<div class="visual-slot"><div class="no-qr">kein QR</div></div><div class="qr-ref"></div>`;

  const hasPrice=!!item.price;
  const priceHtml=hasPrice
    ? `<div class="price has-fixed"><span class="fixed-price">${esc(item.price)} €</span></div>`
    : `<div class="price"><span class="euro">€</span></div>`;
  const ekHtml=item.ek?`<div class="ek-price">EK: ${esc(item.ek)}</div>`:'';
  const qsHtml=item.quickService?'<div class="qs-stamp"><span class="qs-stamp-inner">Quick Service</span></div>':'';

  return `
    <td class="draggable-cell"
        draggable="true"
        data-index="${i}"
        ondragstart="onCellDragStart(event)"
        ondragover="onCellDragOver(event)"
        ondragleave="onCellDragLeave(event)"
        ondrop="onCellDrop(event)"
        ondragend="onCellDragEnd(event)">
      <button class="edit" onclick="openEditor(${i})" aria-label="Bearbeiten">✎</button>
      ${qsHtml}
      ${ekHtml}
      <div class="box">
        <div>
          <div class="title">${title}</div>
          <div class="beco">${beco}</div>
        </div>
        <div class="middle">
          <div class="media-col">
            <div class="visual-slot">
              <div class="photo" onclick="openPhotoPicker(${i})">
                ${photo}
              </div>
            </div>
            <div class="qr-ref"></div>
          </div>
          <div class="media-col">${qrHtml}</div>
        </div>
        ${priceHtml}
      </div>
    </td>
  `;
}

function onCellDragStart(e){
  const cell=e.currentTarget;
  const idx=parseInt(cell.dataset.index,10);
  dragSourceIndex=Number.isFinite(idx)?idx:null;
  cell.classList.add('drag-source');
  e.dataTransfer.effectAllowed='move';
  e.dataTransfer.setData('text/plain',String(dragSourceIndex));
}
function onCellDragOver(e){
  e.preventDefault();
  const cell=e.currentTarget;
  cell.classList.add('drag-over');
  e.dataTransfer.dropEffect='move';
}
function onCellDragLeave(e){ e.currentTarget.classList.remove('drag-over'); }
function onCellDrop(e){
  e.preventDefault();
  const targetCell=e.currentTarget;
  targetCell.classList.remove('drag-over');

  const targetIndex=parseInt(targetCell.dataset.index,10);
  const sourceFromData=parseInt(e.dataTransfer.getData('text/plain'),10);
  const sourceIndex=Number.isFinite(sourceFromData)?sourceFromData:dragSourceIndex;

  if(!Number.isFinite(sourceIndex) || !Number.isFinite(targetIndex)) return;
  if(sourceIndex===targetIndex) return;

  const list=items();
  const tmp=list[sourceIndex];
  list[sourceIndex]=list[targetIndex];
  list[targetIndex]=tmp;

  render();
}
function onCellDragEnd(){
  dragSourceIndex=null;
  document.querySelectorAll('td.drag-over, td.drag-source').forEach(el=>{
    el.classList.remove('drag-over','drag-source');
  });
}

function renderTableForPage(pageIndex){
  const list=data.pages[pageIndex].items;
  let html='';
  for(let r=0;r<3;r++){
    html+='<tr>';
    for(let c=0;c<5;c++){
      const i=r*5+c;
      html+=cell(list[i],i);
    }
    html+='</tr>';
  }
  return `<table>${html}</table>`;
}
function buildAllPagesPrintHtml(){
  return data.pages.map((p,idx)=>`
    <div class="page print-page">
      <div class="category-title">${esc(p.name)}</div>
      <div class="measure">35 mm</div>
      ${renderTableForPage(idx)}
    </div>
  `).join('');
}
function printAllPages(){
  const wrap=document.querySelector('.preview-scale');
  if(!wrap) return;

  const original=wrap.innerHTML;
  wrap.innerHTML=buildAllPagesPrintHtml();

  window.print();

  setTimeout(()=>{
    wrap.innerHTML=original;
    render();
  },100);
}

function render(){
  let html='';
  const list=items();
  for(let r=0;r<3;r++){
    html+='<tr>';
    for(let c=0;c<5;c++){
      const i=r*5+c;
      html+=cell(list[i],i);
    }
    html+='</tr>';
  }
  sheet.innerHTML=html;
  updateWorkspaceContext();
}

function updateWorkspaceContext(){
  const page=data.pages[currentPage];
  if(!page) return;
  const name=page.name||'Kategorie';
  const populated=page.items.filter(item=>item.title||item.beco||item.ref||item.photo||item.price||item.ek).length;
  const title=document.getElementById('workspaceTitle');
  const sheetCategory=document.getElementById('sheetCategory');
  const completed=document.getElementById('completedCount');
  if(title) title.textContent=name;
  if(sheetCategory) sheetCategory.textContent=name;
  if(completed) completed.textContent=populated;
}

function openEditor(i){
  currentItem=i;
  const item=items()[i];
  document.getElementById('fTitle').value=item.title||'';
  document.getElementById('fBeco').value=item.beco||'';
  document.getElementById('fRef').value=item.ref||'';
  document.getElementById('fPrice').value=item.price||'';
  document.getElementById('fEk').value=item.ek||'';
  document.getElementById('fQuickService').checked=!!item.quickService;
  syncQsState();
  openModal(modal);
}
function syncQsState(){
  const qs=document.getElementById('fQuickService').checked;
  const refInput=document.getElementById('fRef');
  refInput.disabled=qs;
  refInput.style.opacity=qs?'0.4':'';
  refInput.style.background=qs?'#f0f0f0':'';
}
function closeEditor(){
  modal.style.display='none';
  currentItem=null;
}
function saveEditor(){
  if(currentItem===null) return;
  const item=items()[currentItem];
  item.title=document.getElementById('fTitle').value.trim();
  item.beco=document.getElementById('fBeco').value.trim();
  item.ref=document.getElementById('fRef').value.trim();
  item.price=document.getElementById('fPrice').value.trim();
  item.ek=document.getElementById('fEk').value.trim();
  item.quickService=document.getElementById('fQuickService').checked;
  closeEditor();
  render();
}
function cleanLibraryKey(key){
  if(!key) return;
  const used=data.pages.some(p=>p.items.some(it=>it.photo===key));
  if(!used) delete data.library[key];
}
function removePhotoFromPicker(){
  if(currentItem===null) return;
  const key=items()[currentItem].photo;
  items()[currentItem].photo='';
  cleanLibraryKey(key);
  closePhotoPicker();
  render();
}
async function resetItem(){
  if(currentItem===null) return;
  const ok=await showDangerConfirm('Artikel komplett zurücksetzen? Alle Felder werden geleert.');
  if(!ok) return;
  const item=items()[currentItem];
  const oldPhoto=item.photo;
  Object.assign(item,emptyItem());
  cleanLibraryKey(oldPhoto);
  closeEditor();
  render();
}
const photoPickerModal=document.getElementById('photoPickerModal');

function openPhotoPicker(i){
  currentItem=i;
  renderPhotoPickerGrid();
  openModal(photoPickerModal);
}
function closePhotoPicker(){
  photoPickerModal.style.display='none';
}
function renderPhotoPickerGrid(){
  const grid=document.getElementById('photoLibGrid');
  const removeBtn=document.getElementById('removePhotoBtn');
  if(removeBtn) removeBtn.hidden=!(currentItem!==null&&items()[currentItem]?.photo);
  const keys=Object.keys(data.library);
  if(!keys.length){
    grid.innerHTML='<p class="lib-empty">Noch keine Fotos in der Bibliothek.</p>';
    return;
  }
  const current=currentItem!==null?items()[currentItem]?.photo:'';
  grid.innerHTML=keys.map(key=>`
    <div class="lib-thumb${key===current?' lib-selected':''}" onclick="selectLibPhoto('${key}')">
      <img src="${data.library[key]}" alt="Foto">
    </div>
  `).join('');
}
function selectLibPhoto(key){
  if(currentItem===null) return;
  items()[currentItem].photo=key;
  closePhotoPicker();
  render();
}
function triggerNewPhotoUpload(){
  document.getElementById('libPhotoUpload').click();
}
function handleLibPhotoUpload(event){
  const file=event.target.files&&event.target.files[0];
  if(!file) return;
  const reader=new FileReader();
  reader.onload=e=>{
    const key=addToLibrary(e.target.result);
    if(currentItem!==null) items()[currentItem].photo=key;
    closePhotoPicker();
    render();
  };
  reader.readAsDataURL(file);
  event.target.value='';
}

document.addEventListener('click',e=>{
  if(!e.target.closest('.menu-wrap')) closeAllMenus();
});
modal.addEventListener('click',e=>{
  if(e.target===modal) closeEditor();
});
infoModal.addEventListener('click',e=>{
  if(e.target===infoModal) closeInfo();
});
photoPickerModal.addEventListener('click',e=>{
  if(e.target===photoPickerModal) closePhotoPicker();
});
categoryModal.addEventListener('click',e=>{
  if(e.target===categoryModal) closeCategoryModal();
});
document.getElementById('catEditName').addEventListener('keydown',e=>{
  if(e.key==='Enter') saveCategoryModal();
});

// ── Custom Dialog ────────────────────────────────────────────────────────────
let _cdResolve=null;
function _showDialog(msg,inputVal,showCancel,dangerOk,okLabel){
  document.getElementById('cdMsg').textContent=msg;
  const inp=document.getElementById('cdInput');
  if(inputVal!==null){inp.style.display='';inp.value=inputVal||'';}
  else{inp.style.display='none';}
  document.getElementById('cdCancel').style.display=showCancel?'':'none';
  const okBtn=document.getElementById('cdOk');
  okBtn.textContent=okLabel||'OK';
  okBtn.className='btn '+(dangerOk?'btn-danger':'btn-primary');
  openModal(document.getElementById('customDialog'));
  if(inputVal!==null) setTimeout(()=>{inp.focus();inp.select();},60);
  return new Promise(r=>{_cdResolve=r;});
}
function showAlert(msg){return _showDialog(msg,null,false,false,'OK');}
function showConfirm(msg){return _showDialog(msg,null,true,false,'OK');}
function showDangerConfirm(msg){return _showDialog(msg,null,true,true,'Löschen');}
function showPrompt(msg,def){return _showDialog(msg,def??'',true,false,'OK');}

document.getElementById('cdOk').addEventListener('click',()=>{
  document.getElementById('customDialog').style.display='none';
  if(_cdResolve){
    const inp=document.getElementById('cdInput');
    _cdResolve(inp.style.display==='none'?true:inp.value);
    _cdResolve=null;
  }
});
document.getElementById('cdCancel').addEventListener('click',()=>{
  document.getElementById('customDialog').style.display='none';
  if(_cdResolve){_cdResolve(false);_cdResolve=null;}
});
document.getElementById('cdInput').addEventListener('keydown',e=>{
  if(e.key==='Enter') document.getElementById('cdOk').click();
  if(e.key==='Escape') document.getElementById('cdCancel').click();
});
document.getElementById('customDialog').addEventListener('click',e=>{
  if(e.target===document.getElementById('customDialog')) document.getElementById('cdCancel').click();
});

refreshPages();
setScale(document.getElementById('zoomRange').value);
render();
initPinchZoom();
initTouchDrag();

// ── Touch: Pinch-to-Zoom on preview ──────────────────────────────────────────
function initPinchZoom(){
  const wrap=document.querySelector('.preview-wrap');
  let startDist=null,startScale=null;

  wrap.addEventListener('touchstart',e=>{
    if(e.touches.length===2){
      const t=e.touches;
      startDist=Math.hypot(t[1].clientX-t[0].clientX,t[1].clientY-t[0].clientY);
      startScale=parseFloat(document.documentElement.style.getPropertyValue('--preview'))||1;
      e.preventDefault();
    }
  },{passive:false});

  wrap.addEventListener('touchmove',e=>{
    if(e.touches.length===2&&startDist!==null){
      e.preventDefault();
      const t=e.touches;
      const dist=Math.hypot(t[1].clientX-t[0].clientX,t[1].clientY-t[0].clientY);
      const scale=Math.min(2.2,Math.max(0.8,startScale*(dist/startDist)));
      setScale(scale);
      document.getElementById('zoomRange').value=scale;
    }
  },{passive:false});

  wrap.addEventListener('touchend',e=>{
    if(e.touches.length<2){startDist=null;startScale=null;}
  });
}

// ── Touch: Long-press Drag & Drop on cells ───────────────────────────────────
function initTouchDrag(){
  let src=null,clone=null,active=false,startPos=null,timer=null;

  function cleanup(){
    clearTimeout(timer);timer=null;
    if(clone){clone.remove();clone=null;}
    document.querySelectorAll('td.drag-over,td.drag-source').forEach(el=>
      el.classList.remove('drag-over','drag-source'));
    src=null;active=false;startPos=null;
  }

  sheet.addEventListener('touchstart',e=>{
    const td=e.target.closest('td[data-index]');
    if(!td||e.target.closest('.edit,.photo')) return;
    src=parseInt(td.dataset.index,10);
    startPos={x:e.touches[0].clientX,y:e.touches[0].clientY};
    active=false;
    // long-press von 450ms aktiviert den Drag
    timer=setTimeout(()=>{
      active=true;
      const r=td.getBoundingClientRect();
      clone=td.cloneNode(true);
      Object.assign(clone.style,{
        position:'fixed',width:r.width+'px',height:r.height+'px',
        left:r.left+'px',top:r.top+'px',
        opacity:'0.82',pointerEvents:'none',zIndex:'9999',
        transform:'scale(1.05)',boxShadow:'0 6px 24px rgba(0,0,0,.3)',
        transition:'transform .1s'
      });
      document.body.appendChild(clone);
      td.classList.add('drag-source');
    },450);
  },{passive:true});

  sheet.addEventListener('touchmove',e=>{
    if(src===null) return;
    if(!active){
      // mehr als 8px Bewegung vor Long-Press → kein Drag, normales Scrollen
      if(Math.hypot(e.touches[0].clientX-startPos.x,e.touches[0].clientY-startPos.y)>8){
        clearTimeout(timer);timer=null;src=null;
      }
      return;
    }
    e.preventDefault();
    const touch=e.touches[0];
    clone.style.left=(touch.clientX-clone.offsetWidth/2)+'px';
    clone.style.top=(touch.clientY-clone.offsetHeight/2)+'px';
    clone.style.visibility='hidden';
    const el=document.elementFromPoint(touch.clientX,touch.clientY);
    clone.style.visibility='';
    document.querySelectorAll('td.drag-over').forEach(el=>el.classList.remove('drag-over'));
    const targetTd=el?.closest('td[data-index]');
    if(targetTd&&parseInt(targetTd.dataset.index,10)!==src)
      targetTd.classList.add('drag-over');
  },{passive:false});

  sheet.addEventListener('touchend',e=>{
    if(!active){cleanup();return;}
    const touch=e.changedTouches[0];
    const el=document.elementFromPoint(touch.clientX,touch.clientY);
    const targetTd=el?.closest('td[data-index]');
    if(targetTd){
      const ti=parseInt(targetTd.dataset.index,10);
      if(Number.isFinite(ti)&&ti!==src){
        const list=items();
        [list[src],list[ti]]=[list[ti],list[src]];
        cleanup();render();return;
      }
    }
    cleanup();
  },{passive:true});

  sheet.addEventListener('touchcancel',cleanup,{passive:true});
}
