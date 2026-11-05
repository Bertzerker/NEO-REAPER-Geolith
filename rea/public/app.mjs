// SPDX-License-Identifier: MIT
const $=id=>document.getElementById(id);let session,current,held=false,saved=false,invalid=false,baseline=null,serial=0,dirty=false,note=null,draftCapture=null,viewer=false,presented=0,started=performance.now(),ws;
const owner=crypto.randomUUID(),baseOwner=crypto.randomUUID();
const draftTab=sessionStorage.getItem('neo-reaper-draft-tab')||crypto.randomUUID();sessionStorage.setItem('neo-reaper-draft-tab',draftTab);
const draftKey='neo-reaper-comment-draft-v1-'+draftTab;
let lastMeasure=null,capturedBounds=null,capturedBoundsId=null;
let pickedStrip=null,priorityCapture=null,priorityStrips=[],priorityRange=null,priorityRequest=0;
let priorityPoint=null,priorityPixelStrips=[],lastWheel=-Infinity;
let pickingChain=false;
let pickingLive=false,pendingLivePick=null;
let followingSprite=null, spriteTrackVersion=null, spriteTracks=[];
let catalogSample=null,trackedStream=null,trackedSequence=null,draftAttachment=null,attachmentTicket=0;
const catalogReady=()=>session?.capabilities?.spriteTileCatalog===true;
const priorityView=()=>$('view').value==='priority';
const spriteView=()=>$('view').value.startsWith('follow:');
const selectedTrack=()=>spriteTracks.find(track=>$('view').value===`follow:${track.id}`);
const chainPickView=()=>['0','1'].includes($('view').value);
function message(s){$('message').textContent=s||'';}
async function api(path,body){const r=await fetch(path,body?{method:'POST',headers:{'Content-Type':'application/json','X-REA-Token':session?.token},body:JSON.stringify(body)}:{});const j=await r.json();if(!r.ok)throw Error(j.error||r.status);return j;}
const safe=fn=>async(...args)=>{try{await fn(...args)}catch(e){message(e.message)}};
function tab(name){for(const el of document.querySelectorAll('.tab'))el.hidden=el.id!==name;for(const b of document.querySelectorAll('[data-tab]'))b.setAttribute('aria-selected',String(b.dataset.tab===name));}
document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>tab(b.dataset.tab));tab('movies');

const helperPath='/api/sprite-helpers';
const helperFiles=$('helper-files'),helperName=$('helper-name'),helperText=$('helper-json');
let helper=null,helperBusy=false;
function helperDirty(){
  return helper&&(helperName.value!==helper.file||helperText.value!==helper.text);
}
function helperControls(){
  helperFiles.disabled=$('helper-reload').disabled=$('helper-new').disabled=helperBusy;
  $('helper-new-range').disabled=helperBusy||!priorityRange;
  helperName.disabled=helperBusy||!helper||Boolean(helper.revision);
  helperText.disabled=helperBusy||!helper;
  $('helper-save').disabled=helperBusy||!helper||!helperName.value||(!helperDirty()&&Boolean(helper.revision));
  $('helper-delete').disabled=helperBusy||!helper?.revision;
  $('helper-draft').textContent=helper&&(helperDirty()||!helper.revision)?'Unsaved helper draft':'';
}
function showHelper(document){
  helper=document;
  helperName.value=helper?.file??'';
  helperText.value=helper?.text??'';
  if(helper)helper.text=helperText.value;
  helperFiles.value=helper?.revision?helper.file:'';
  helperControls();
}
const discardHelper=()=>!helperDirty()||confirm('Discard unsaved changes to this helper?');
const helperStatus=text=>$('helper-status').textContent=text;
const helperAction=fn=>async()=>{
  if(helperBusy)return;
  helperBusy=true;
  helperStatus('');
  helperControls();
  try{await fn();}
  catch(e){helperStatus(e.message);}
  finally{helperBusy=false;helperControls();}
};
async function loadHelperList(selected=helper?.file){
  const files=await api(helperPath);
  helperFiles.replaceChildren(new Option('Select a JSON file',''),...files.map(file=>new Option(file,file)));
  helperFiles.value=files.includes(selected)?selected:'';
  return files;
}
async function openHelper(file){
  showHelper(file?await api(`${helperPath}?file=${encodeURIComponent(file)}`):null);
}
$('helper-tab').addEventListener('click',helperAction(async()=>{
  const files=await loadHelperList();
  if(!helper&&files.length)await openHelper(files[0]);
}));
helperFiles.onchange=helperAction(async()=>{
  const file=helperFiles.value;
  helperFiles.value=helper?.revision?helper.file:'';
  if(discardHelper())await openHelper(file);
});
$('helper-reload').onclick=helperAction(async()=>{
  if(!discardHelper())return;
  if(catalogReady()){
    await api(helperPath,{action:'reload'});
    await poll();
  }
  const files=await loadHelperList();
  await openHelper(files.includes(helper?.file)?helper.file:files[0]);
  helperStatus('Read the latest JSON from disk.');
});
async function newHelper(range=null){
  if(!discardHelper())return;
  if(!session)throw Error('Connect to a cartridge first.');
  const {cartridge,system,region}=session.manifest;
  const base=cartridge.name.replace(/\.neo$/i,'').toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'').slice(0,64)||'game';
  const files=(await loadHelperList()).map(file=>file.toLowerCase());
  const prefix=`catalog-${base}-${system.toLowerCase()}-${region}`;
  let file=prefix+'.json';
  for(let i=2;files.includes(file);i++)file=`${prefix}-${i}.json`;
  const sprites=range?[{id:'sprite-1',name:'Sprite 1',selectors:[{firstStrip:range.first,lastStrip:range.last}]}]:[];
  const data={schemaVersion:1,game:cartridge.name,cartridgeSha256:cartridge.sha256,system,region,sprites,tiles:[]};
  showHelper({file,revision:'',text:JSON.stringify(data,null,2)+'\n'});
}
$('helper-new').onclick=helperAction(()=>newHelper());
$('helper-new-range').onclick=helperAction(()=>newHelper(priorityRange));
helperName.oninput=helperText.oninput=helperControls;
$('helper-save').onclick=helperAction(async()=>{
  const result=await api(helperPath,{action:'save',file:helperName.value,revision:helper.revision,text:helperText.value});
  showHelper(result);
  helperStatus(`Saved ${result.file}.`);
  await loadHelperList(result.file);
  await poll();
});
$('helper-delete').onclick=helperAction(async()=>{
  if(!confirm(`Delete ${helper.file} and all its catalog entries?`))return;
  const {file}=await api(helperPath,{action:'delete',file:helper.file,revision:helper.revision});
  showHelper(null);
  helperStatus(`Deleted ${file}.`);
  await loadHelperList();
  await poll();
});
helperControls();

const catalogLabel=entry=>entry.kind==='sprite'?'Sprite group':entry.source==='fix'?'FIX tile':'Sprite tile';
function catalogList(){
  const query=$('catalog-search').value.trim().toLowerCase();
  const list=$('catalog-list');
  list.replaceChildren();
  if(!catalogReady())return;
  for(const entry of spriteTracks){
    if(![entry.name,entry.id,catalogLabel(entry),entry.notes,...entry.labels].some(text=>text?.toLowerCase().includes(query)))continue;
    const row=document.createElement('div');
    row.className='row';
    row.append(document.createTextNode(`${entry.name} · ${catalogLabel(entry)} · ${entry.selectors.length} selectors `));
    button(row,'Show all matches',async()=>{
      $('view').value=`follow:${entry.id}`;
      await $('view').onchange();
    });
    button(row,'Edit catalog JSON',helperAction(async()=>{
      if(discardHelper())await openHelper(entry.file);
    }));
    const details=document.createElement('details'),summary=document.createElement('summary'),selectors=document.createElement('pre');
    summary.textContent='Selectors and evidence';
    selectors.textContent=`${entry.evidence}\n${entry.labels.join(', ')}\n${entry.notes??''}\n${JSON.stringify(entry.selectors,null,2)}`;
    details.append(summary,selectors);
    row.append(details);
    list.append(row);
  }
}
$('catalog-search').oninput=catalogList;
function catalogSeedEntries(){
  const kind=$('catalog-kind').value;
  const choices=spriteTracks.filter(entry=>entry.kind===kind&&entry.source===catalogSample?.source);
  $('catalog-entry').replaceChildren(new Option('Create a named entry',''),...choices.map(entry=>new Option(entry.name,entry.id)));
  $('catalog-name').disabled=false;
}
$('catalog-kind').onchange=catalogSeedEntries;
$('catalog-entry').onchange=()=>$('catalog-name').disabled=Boolean($('catalog-entry').value);
$('catalog-add').onclick=safe(async()=>{
  if(!discardHelper())return;
  const view=$('view').value,x=$('x').value,y=$('y').value;
  const capture=await pin();
  catalogSample=await api(`/api/catalog-sample?capture=${encodeURIComponent(capture)}&view=${view}&x=${x}&y=${y}`);
  const {source,tile,palette,bank,strip,cellX,cellY}=catalogSample;
  const location=source==='fix'?`FIX cell ${cellX}, ${cellY} · VRAM word 0x${catalogSample.vramWord.toString(16)}`:`Sprite strip ${strip}`;
  $('catalog-locator').textContent=`${location} · resolved tile 0x${tile.toString(16)} · palette ${palette} · palette bank ${bank} · capture pixel ${x}, ${y}`;
  $('catalog-kind').value='tile';
  $('catalog-kind').options[1].disabled=source==='fix';
  $('catalog-name').value='';
  $('catalog-palette').checked=false;
  catalogSeedEntries();
  $('catalog-seed').hidden=false;
  tab('sprite-helpers');
  $('catalog-name').focus();
});
$('catalog-save-seed').onclick=helperAction(async()=>{
  if(!catalogSample||!discardHelper())return;
  const kind=$('catalog-kind').value;
  const existing=spriteTracks.find(entry=>entry.id===$('catalog-entry').value);
  const {cartridge,system,region}=session.manifest;
  const file=existing?.file??`catalog-${cartridge.sha256.slice(0,16)}-${system.toLowerCase()}-${region}.json`;
  const files=await api(helperPath);
  const old=files.includes(file)?await api(`${helperPath}?file=${encodeURIComponent(file)}`):null;
  if(existing&&old?.revision!==existing.revision)throw Error('This catalog changed. Reload before adding a selector.');
  const profile=old?JSON.parse(old.text):{schemaVersion:1,game:cartridge.name,cartridgeSha256:cartridge.sha256,system,region};
  if(profile.cartridgeSha256.toLowerCase()!==cartridge.sha256.toLowerCase()||profile.system!==system||profile.region!==region)
    throw Error('This catalog belongs to another cartridge or machine configuration.');
  profile.schemaVersion=1;
  const bucket=kind==='tile'?'tiles':'sprites';
  const items=profile[bucket]??=[];
  const {source,tile,palette,bank}=catalogSample;
  const selector=kind==='tile'?{tile}:{firstTile:tile,lastTile:tile};
  if($('catalog-palette').checked){
    selector.palette=palette;
    if(kind==='tile')selector.bank=bank;
  }
  const entry=existing?items.find(item=>item.id===existing.id):{id:`${kind}-${crypto.randomUUID()}`,name:$('catalog-name').value.trim(),selectors:[]};
  if(!entry?.name)throw Error('Enter a name for the catalog entry.');
  if(!existing){
    if(kind==='tile')entry.source=source;
    items.push(entry);
  }
  if(!entry.selectors.some(rule=>JSON.stringify(rule)===JSON.stringify(selector)))entry.selectors.push(selector);
  const result=await api(helperPath,{action:'save',file,revision:old?.revision??'',text:JSON.stringify(profile,null,2)+'\n'});
  await loadHelperList(file);
  showHelper(result);
  $('catalog-seed').hidden=true;
  catalogSample=null;
  await poll();
  helperStatus(`Saved selector for ${entry.name}.`);
});
const canvas=$('screen'),gl=canvas.getContext('webgl',{alpha:false,antialias:false,preserveDrawingBuffer:true});if(!gl)throw Error('WebGL is required for the native atlas');
function shader(type,src){const s=gl.createShader(type);gl.shaderSource(s,src);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));return s;}
const program=gl.createProgram();gl.attachShader(program,shader(gl.VERTEX_SHADER,'attribute vec2 p;varying vec2 uv;void main(){uv=vec2((p.x+1.)/2.,(1.-p.y)/2.);gl_Position=vec4(p,0,1);}'));gl.attachShader(program,shader(gl.FRAGMENT_SHADER,'precision mediump float;varying vec2 uv;uniform sampler2D atlas;uniform vec2 region;uniform bool bgra;void main(){float y=region.x+uv.y*region.y;vec4 c=texture2D(atlas,vec2(uv.x,y));float q=mod(floor(uv.x*38.)+floor(uv.y*28.),2.);vec3 rgb=bgra?c.bgr:c.rgb;gl_FragColor=vec4(mix(vec3(.09+q*.04),rgb,y<1./3.?1.:c.a),1.);}'));gl.linkProgram(program);gl.useProgram(program);const buf=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,buf);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),gl.STATIC_DRAW);const p=gl.getAttribLocation(program,'p');gl.enableVertexAttribArray(p);gl.vertexAttribPointer(p,2,gl.FLOAT,false,0,0);const texture=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,texture);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);let hasTexture=false,lastAtlasStatus=0;
const regionUniform=gl.getUniformLocation(program,'region');
const bgraUniform=gl.getUniformLocation(program,'bgra');
function paintAtlas(){if(hasTexture&&!canvas.hidden)gl.drawArrays(gl.TRIANGLES,0,6);}
function draw(){
  const priority=priorityView();
  const follow=spriteView();
  const scale=$('scale').value;
  const width=scale==='fit'?'100%':`${304*Number(scale)}px`;
  canvas.hidden=priority||follow;
  $('bounds').hidden=priority||follow;
  $('overlay-label').hidden=priority||follow;
  $('chain-tools').hidden=!chainPickView();
  $('plane-help').hidden=priority||follow;
  $('sprite-follow-help').hidden=!follow;
  $('sprite-follow-screen').hidden=!follow;
  for(const id of ['priority-tools','priority-status','priority-help'])$(id).hidden=!priority;
  const image=$('priority-image');
  image.hidden=!priority||!image.hasAttribute('src');
  if(follow){
    $('sprite-follow-screen').style.width=width;
    $('sprite-follow-screen').style.maxWidth=scale==='fit'?'100%':'none';
    $('badge').textContent=`Follow ${followingSprite?.name??'sprite'} · live isolated animation`;
    $('sprite-follow-screen').setAttribute('aria-label',`Live isolated animation of ${followingSprite?.name??'sprite'}`);
    return;
  }
  if(priority){
    image.style.width=width;
    image.style.maxWidth=scale==='fit'?'100%':'none';
    $('badge').textContent='Sprite priority · raster-time samples before FIX';
    return;
  }
  const all=$('view').value==='all',view=all?0:Number($('view').value);
  const height=all?672:224;
  if(canvas.height!==height)canvas.height=height;
  gl.viewport(0,0,304,height);
  gl.uniform2f(regionUniform,all?0:view/3,all?1:1/3);
  paintAtlas();
  canvas.style.width=width;
  canvas.style.maxWidth=scale==='fit'?'100%':'none';
  $('badge').textContent=all?'Composite / Sprite plane / FIX · one synchronized atlas':view===0?'Captured output · 304 × 224 · native crop (8,24)':'Captured input · raster-time samples, transparency retained';
  const bounds=$('bounds');
  bounds.replaceChildren();
  bounds.style.left=canvas.offsetLeft+'px';
  bounds.style.top=canvas.offsetTop+'px';
  bounds.style.width=canvas.offsetWidth+'px';
  bounds.style.height=canvas.offsetHeight+'px';
  if($('overlay').checked&&capturedBoundsId===current?.id&&capturedBounds)drawBounds(capturedBounds);
}

function clearPriorityImage(){
  const image=$('priority-image');
  if(image.hasAttribute('src'))URL.revokeObjectURL(image.src);
  image.removeAttribute('src');
  image.hidden=true;
}

function adjacentStrip(direction){
  if(!priorityRange)return undefined;
  const strips=priorityPixelStrips.length?priorityPixelStrips:priorityStrips;
  if(direction<0)return strips.findLast(strip=>strip<priorityRange.first);
  return strips.find(strip=>strip>priorityRange.last);
}

function updatePriorityButtons(){
  helperControls();
  $('strip-prev').disabled=adjacentStrip(-1)===undefined;
  $('strip-next').disabled=adjacentStrip(1)===undefined;
  $('strip-show').disabled=priorityStrips.length===0;
}

function clearPriority(){
  ++priorityRequest;
  priorityCapture=null;
  priorityStrips=[];
  priorityPixelStrips=[];
  priorityPoint=null;
  priorityRange=null;
  clearPriorityImage();
  updatePriorityButtons();
}

function priorityActive(ticket,id){
  return ticket===priorityRequest&&priorityView()&&current?.id===id;
}

async function renderPriority(first,last,group=null){
  if(first>last)throw Error('First strip must be at or below last strip.');
  const id=priorityCapture,ticket=++priorityRequest;
  priorityRange={first,last};
  $('strip-first').value=first;
  $('strip-last').value=last;
  updatePriorityButtons();
  const label=group?`Sprite group · picked strip ${group.strip}`:
    first===last?`Strip ${first}`:`Strips ${first}–${last}`;
  $('priority-status').textContent=`Loading ${label.toLowerCase()}…`;
  const url=group?`${group.url}&image=1`:`/api/sprite-priority?capture=${id}&image=1&first=${first}&last=${last}`;
  const response=await fetch(url);
  if(!priorityActive(ticket,id))return;
  if(!response.ok){
    $('priority-status').textContent=(await response.json()).error;
    return;
  }
  const png=await response.blob();
  if(!priorityActive(ticket,id))return;
  const strips=priorityPixelStrips.length?priorityPixelStrips:priorityStrips;
  const count=group?group.strips.length:strips.filter(strip=>strip>=first&&strip<=last).length;
  const scope=priorityPixelStrips.length?`at pixel (${priorityPoint.x}, ${priorityPoint.y})`:'with pixels in this frame';
  const image=$('priority-image');
  clearPriorityImage();
  image.src=URL.createObjectURL(png);
  image.alt=`${label} at evidence frame ${current.frame}`;
  image.hidden=false;
  $('priority-status').textContent=`${label} · ${count} of ${strips.length} strips ${scope}`;
  message('');
}

async function loadPriority(id,group=null,point=null){
  clearPriority();
  if(!current.spritePriority){
    $('priority-status').textContent='Sprite priority samples are unavailable for this capture. Return to live and select a fresh frame.';
    return;
  }
  const ticket=priorityRequest;
  $('priority-status').textContent='Finding strips with pixels in this frame…';
  const pixel=point?`&x=${point.x}&y=${point.y}`:'';
  const result=await api(`/api/sprite-priority?capture=${id}${pixel}`);
  if(!priorityActive(ticket,id))return;
  priorityCapture=id;
  priorityStrips=result.strips;
  priorityPixelStrips=result.atPixel??[];
  priorityPoint=point;
  if(!priorityStrips.length){
    $('priority-status').textContent='No sprite strips drew pixels in this frame.';
    return;
  }
  if(group){
    await renderPriority(group.strips[0],group.strips.at(-1),group);
    return;
  }
  const strips=priorityPixelStrips.length?priorityPixelStrips:priorityStrips;
  const preferred=pickedStrip?.capture===id?pickedStrip.strip:null;
  const strip=point?strips.at(point.direction>0&&strips.length>1?-2:-1):
    strips.includes(preferred)?preferred:strips.at(-1);
  await renderPriority(strip,strip);
}

async function stepPriority(direction){
  const strip=adjacentStrip(direction);
  if(strip!==undefined)await renderPriority(strip,strip);
}
for(const [id,direction] of [['strip-prev',-1],['strip-next',1]])$(id).onclick=safe(()=>stepPriority(direction));
$('priority-tools').onsubmit=safe(async event=>{
  event.preventDefault();
  priorityPixelStrips=[];
  priorityPoint=null;
  await renderPriority(Number($('strip-first').value),Number($('strip-last').value));
});

function imagePixel(event,screen){
  const bounds=screen.getBoundingClientRect();
  const x=Math.floor((event.clientX-bounds.left)*screen.width/bounds.width);
  const y=Math.floor((event.clientY-bounds.top)*screen.height/bounds.height);
  return x>=0&&y>=0&&x<screen.width&&y<screen.height?{x,y}:null;
}
function wheelStep(event){
  if(event.ctrlKey||!event.deltaY||Math.abs(event.deltaX)>Math.abs(event.deltaY))return 0;
  event.preventDefault();
  const now=performance.now();
  if(now-lastWheel<65)return 0;
  lastWheel=now;
  return Math.sign(event.deltaY);
}
$('stage').addEventListener('wheel',safe(async event=>{
  if(priorityView()){
    const direction=wheelStep(event);
    if(direction)await stepPriority(-direction);
  }else if($('view').value==='1'&&current?.spritePriority&&event.target===canvas){
    const point=imagePixel(event,canvas),direction=wheelStep(event);
    if(!point||!direction)return;
    setPickingChain(false);
    const pinned=pin();
    $('view').value='priority';
    draw();
    const id=await pinned;
    if(priorityView()&&current?.id===id)await loadPriority(id,null,{...point,direction});
  }
}),{passive:false});

function setPickingChain(enabled){
  if(enabled)setPickingLive(false);
  pickingChain=enabled;
  $('pick-chain').setAttribute('aria-pressed',String(enabled));
  $('pick-help').hidden=!enabled;
  canvas.classList.toggle('picking-chain',pickingChain||pickingLive);
}

$('pick-chain').onclick=()=>setPickingChain(!pickingChain);

function setPickingLive(enabled){
  pickingLive=enabled;
  if(enabled)setPickingChain(false);
  else if(pendingLivePick){
    const pending=pendingLivePick;
    pendingLivePick=null;
    pending.close();
  }
  $('pick-live').setAttribute('aria-pressed',String(enabled));
  $('pick-live-help').hidden=!enabled;
  canvas.classList.toggle('picking-chain',pickingChain||pickingLive);
}
$('pick-live').onclick=()=>setPickingLive(!pickingLive);

function pickLiveSprite(){
  if(pendingLivePick||!current)return;
  const query=new URLSearchParams({capture:current.id,view:$('view').value,x:$('x').value,y:$('y').value,catalog:'1'});
  const socket=new WebSocket(`ws://${location.host}/stream/sprite?${query}`);
  pendingLivePick=socket;
  socket.binaryType='arraybuffer';
  socket.onclose=()=>{
    if(pendingLivePick!==socket)return;
    pendingLivePick=null;
    message('Could not start following. Click a sprite again.');
  };
  socket.onmessage=safe(async event=>{
    if(pendingLivePick!==socket||typeof event.data!=='string')return;
    const result=JSON.parse(event.data);
    pendingLivePick=null;
    const track=spriteTracks.find(track=>track.id===result.id);
    if(!track){
      socket.close();
      throw Error(result.error??'That helper has changed. Click the sprite again.');
    }
    setPickingLive(false);
    $('view').value=`follow:${track.id}`;
    await enterSpriteView(track,socket);
    trackedStream=result.stream??null;
  });
}

async function pickSpriteGroup(){
  const x=Number($('x').value),y=Number($('y').value),view=Number($('view').value);
  const pinned=pin(),ticket=serial;
  const id=await pinned;
  const url=`/api/sprite-group?capture=${id}&view=${view}&x=${x}&y=${y}`;
  const group=await api(url);
  if(ticket!==serial||!pickingChain||current?.id!==id)return;
  setPickingChain(false);
  $('view').value='priority';
  draw();
  status();
  await loadPriority(id,{...group,url});
}
async function show(meta,blob,ticket){const image=await createImageBitmap(blob);if(ticket!==serial){image.close();return;}current=meta;gl.bindTexture(gl.TEXTURE_2D,texture);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,image);gl.uniform1i(bgraUniform,0);hasTexture=true;image.close();draw();presented++;status();}
function showRawAtlas(meta,pixels){
  if(pixels.byteLength!==304*672*4)throw Error('Invalid live atlas length');
  current=meta;
  gl.bindTexture(gl.TEXTURE_2D,texture);
  gl.uniform1i(bgraUniform,1);
  if(hasTexture){
    gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,304,672,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
    paintAtlas();
  }else{
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,304,672,0,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
    hasTexture=true;
    draw();
  }
  presented++;
  const now=performance.now();
  if(now-lastAtlasStatus>=75){lastAtlasStatus=now;status();}
}
function status(){if(!session)return;
  if(document.body.classList.toggle('native-frontend',session.nativeFrontend===true)){
    const selected=document.querySelector('[role="tab"][aria-selected="true"]');
    if(selected?.hasAttribute('data-local-control'))tab('comments');
  }
$('status').textContent=`Server: ${viewer?'connected':'disconnected'} · Native: ${session.producer} · Machine: ${session.machine} · frame ${session.frame}${session.nativeFrontend?'':` · movie ${session.movie.mode}`}`;$('evidence').textContent=current?`${invalid?'INVALID':saved?'SAVED':held?'FROZEN':'LIVE'} · evidence frame ${current.frame} · ${current.id} · age ${Math.max(0,(Date.now()-current.time)/1000).toFixed(1)}s`:session.nativeFrontend?'Waiting for a live frame from RetroArch.':'No fresh capture. Step a frame to produce native evidence.';$('freeze').textContent=held||saved?'Return to live':'Freeze inspection';if(current&&!invalid)$('export').href=`/api/export?capture=${encodeURIComponent(current.id)}`;else $('export').removeAttribute('href');for(const id of ['comment','inspect','read','baseline'])$(id).disabled=!current||invalid;$('inspect').disabled||=priorityView();$('freeze').disabled=!current&&!held&&!saved;
  $('pick-chain').disabled=!current?.spriteChains||invalid;
  $('pick-live').disabled=!current?.spriteChains||invalid||held||saved||spriteTracks.length===0;
  $('catalog-add').disabled=!catalogReady()||!current||invalid||!['0','1','2'].includes($('view').value);
  if(followingSprite){
    const screen=$('sprite-follow-screen');
    const frame=screen.dataset.frame;
    const name=followingSprite.name;
    const mode=screen.dataset.mode;
    const visible=screen.dataset.visible==='true';
    let state='Waiting for the sprite stream';
    if(mode==='0')state=visible?`${name} visible · click one to follow it`:`Waiting for ${name} to appear`;
    if(mode==='1')state=visible?`Following one ${name}`:`Looking for the selected ${name}`;
    if(mode==='2')state=`${name} lost or ambiguous · show all matches to pick again`;
    if(mode==='3')state=visible?'No sprite selected · click a visible sprite again':'Waiting for another match to pick';
    if(mode==='4')state=`Layer ${screen.dataset.layer} of ${screen.dataset.layerCount} · scroll to browse · click to follow`;
    $('sprite-show-all').disabled=mode===undefined||mode==='0';
    $('catalog-attach').disabled=!trackedStream||trackedSequence===null||!visible||!['0','1'].includes(mode);
    const context=['playback','finished'].includes(session.movie.mode)?'MOVIE REPLAY':'LIVE TRACK';
    $('evidence').textContent=`${context} · ${state}${frame?` · frame ${frame}`:''}`;
    $('freeze').textContent='Return to live';
    $('freeze').disabled=false;
  }
}
async function syncSpriteTracks(){
  const version=`${session.session}:${session.spriteTrackRevision}`;
  if(spriteTrackVersion===version)return;
  const tracks=catalogReady()?await api('/api/catalog'):
    (await api('/api/sprite-tracks')).map(track=>({...track,kind:'sprite',source:'sprite'}));
  if(version!==`${session.session}:${session.spriteTrackRevision}`||spriteTrackVersion===version)return;
  spriteTrackVersion=version;
  spriteTracks=tracks;
  const value=$('view').value;
  const group=$('sprite-views');
  group.replaceChildren(...tracks.map(track=>new Option(track.kind==='sprite'?`Follow ${track.name}`:
    `Follow ${track.name} · ${catalogLabel(track)}`,`follow:${track.id}`)));
  group.hidden=tracks.length===0;
  catalogList();
  if(tracks.some(track=>value===`follow:${track.id}`)){
    $('view').value=value;
    changeStream(selectedTrack());
  }else if(followingSprite){
    $('view').value='0';
    changeStream(null);
    draw();
  }
}
async function poll(){try{session=await api('/api/session');await syncSpriteTracks();viewer=true;$('rom-name').textContent=`${session.manifest.system} · ${session.manifest.cartridge.name}`;if(!held&&!saved&&!session.capture){current=null;hasTexture=false;gl.clear(gl.COLOR_BUFFER_BIT);}if(!$('domain').options.length)for(const d of session.domains){const o=document.createElement('option');o.value=d.id;o.textContent=d.name;$('domain').append(o);}status();const measured=await api('/api/metrics'),now=performance.now();if(lastMeasure&&lastMeasure.session===session.session){const seconds=(now-lastMeasure.time)/1000;measured.nativeFramesPerSecond=Math.max(0,(measured.executed-lastMeasure.executed)/seconds);measured.capturesPerSecond=Math.max(0,(measured.captured-lastMeasure.captured)/seconds);measured.uniqueBrowserPresentationsPerSecond=(presented-lastMeasure.presented)/seconds;}lastMeasure={time:now,session:session.session,executed:measured.executed,captured:measured.captured,presented};$('metrics').textContent=JSON.stringify(measured,null,2);$('presentations').textContent=`Unique atlas presentations: ${presented}; average ${(presented/((performance.now()-started)/1000)).toFixed(2)}/s. Source cadence and native step costs are separate above.`;}catch(e){viewer=false;status();message(e.message);}}
function paintTrackedPacket(packet,offset,screen){
  if(packet.byteLength-offset<17)throw Error('Short sprite packet');
  const view=new DataView(packet,offset);
  const x=view.getUint16(8,true),y=view.getUint16(10,true);
  const width=view.getUint16(12,true),height=view.getUint16(14,true);
  const mode=String(view.getUint8(16));
  const pixelBytes=width*height*4,preview=mode==='4';
  if(x+width>304||y+height>224||(width===0)!==(height===0)||view.byteLength!==17+pixelBytes+(preview?4:0)||Number(mode)>4)
    throw Error('Invalid sprite packet bounds');
  const context=screen.getContext('2d');
  const visible=String(width>0);
  const layer=preview?String(view.getUint16(17+pixelBytes,true)):'';
  const layerCount=preview?String(view.getUint16(19+pixelBytes,true)):'';
  context.clearRect(0,0,screen.width,screen.height);
  if(width)context.putImageData(new ImageData(new Uint8ClampedArray(packet,offset+17,pixelBytes),width,height),x,y);
  return {frame:view.getBigUint64(0,true).toString(),visible,mode,layer,layerCount};
}
function showFollowFrame(packet){
  const offset=catalogReady()?4:0;
  if(packet.byteLength<17+offset)throw Error('Short tracked packet');
  const screen=$('sprite-follow-screen'),firstPacket=offset>0&&trackedSequence===null;
  trackedSequence=offset?new DataView(packet).getUint32(0,true):null;
  const state=paintTrackedPacket(packet,offset,screen);
  const changed=firstPacket||Object.keys(state).some(key=>key!=='frame'&&screen.dataset[key]!==state[key]);
  Object.assign(screen.dataset,state);
  if(changed)status();
}
function sendSpritePick(screen,point,direction=0){
  const pick=new DataView(new ArrayBuffer(direction?13:12));
  pick.setBigUint64(0,BigInt(screen.dataset.frame),true);
  pick.setUint16(8,point.x,true);
  pick.setUint16(10,point.y,true);
  if(direction)pick.setInt8(12,direction);
  ws.send(pick);
}
const followScreen=$('sprite-follow-screen');
followScreen.onclick=event=>{
  if(ws?.readyState!==WebSocket.OPEN)return;
  if(followScreen.dataset.mode==='4'){
    ws.send(new Uint8Array([1]));
    return;
  }
  if(['1','2'].includes(followScreen.dataset.mode)){
    ws.send(new Uint8Array([0]));
    return;
  }
  if(followScreen.dataset.visible!=='true'||!['0','3'].includes(followScreen.dataset.mode))return;
  const point=imagePixel(event,followScreen);
  if(point)sendSpritePick(followScreen,point);
};
followScreen.addEventListener('wheel',event=>{
  if(ws?.readyState!==WebSocket.OPEN||!['0','3','4'].includes(followScreen.dataset.mode)||
     followScreen.dataset.visible!=='true')return;
  const point=imagePixel(event,followScreen),direction=wheelStep(event);
  if(point&&direction)sendSpritePick(followScreen,point,direction);
},{passive:false});
$('sprite-show-all').onclick=()=>{if(ws?.readyState===WebSocket.OPEN)ws.send(new Uint8Array([0]));};

function connect(){
  if(ws)return;
  const follow=followingSprite;
  const raw=!follow&&session?.nativeFrontend===true;
  const socket=new WebSocket(`ws://${location.host}/stream${follow?`/sprite?id=${encodeURIComponent(follow.id)}&pick=1&catalog=1`:raw?'?pixels=bgra':''}`);
  watchStream(socket,follow,raw);
}

function watchStream(socket,follow,raw=false){
  ws=socket;
  socket.binaryType='arraybuffer';
  socket.onmessage=safe(async event=>{
    if(ws!==socket)return;
    if(follow){
      if(typeof event.data==='string'){
        trackedStream=JSON.parse(event.data).stream??null;
        return;
      }
      showFollowFrame(event.data);
      return;
    }
    if(held||saved||document.hidden)return;
    const packet=event.data;
    if(packet.byteLength<8)throw Error('Short atlas packet');
    const length=new DataView(packet).getUint32(0,true);
    if(length>65536||length>packet.byteLength-4||packet.byteLength>4*1024*1024)
      throw Error('Invalid atlas packet bounds');
    const meta=JSON.parse(new TextDecoder().decode(new Uint8Array(packet,4,length)));
    const ticket=++serial;
    if(raw)showRawAtlas(meta,new Uint8Array(packet,4+length));
    else await show(meta,new Blob([packet.slice(4+length)],{type:'image/png'}),ticket);
  });
  socket.onclose=()=>{
    if(ws!==socket)return;
    ws=null;
    trackedStream=trackedSequence=null;
    viewer=false;
    status();
    setTimeout(connect,1500);
  };
}

function changeStream(follow,socket=null){
  if(follow===followingSprite)return;
  followingSprite=follow;
  trackedStream=trackedSequence=null;
  const screen=$('sprite-follow-screen');
  screen.getContext('2d').clearRect(0,0,screen.width,screen.height);
  delete screen.dataset.frame;
  delete screen.dataset.visible;
  delete screen.dataset.mode;
  ++serial;
  const previous=ws;
  ws=null;
  previous?.close();
  if(socket)watchStream(socket,follow);
  else connect();
}
async function pin(){if(!current)throw Error('No displayed capture');setPickingLive(false);held=true;serial++;const id=current.id;try{await api('/api/lease',{capture:id,owner});invalid=false;}catch(e){invalid=true;throw e;}status();return id;}
async function live(){
  await api('/api/lease',{owner,action:'release'});
  setPickingChain(false);
  setPickingLive(false);
  if(priorityView()||spriteView()){
    $('view').value='0';
    clearPriority();
    changeStream(null);
    draw();
  }
  held=saved=invalid=false;
  const ticket=++serial;
  const meta=await api('/api/snapshot');
  await show(meta,await (await fetch(`/api/live?capture=${meta.id}`)).blob(),ticket);
  status();
  message('');
}
$('freeze').onclick=safe(()=>held||saved||followingSprite?live():pin());
async function inspect(){const id=await pin(),ticket=++serial,x=Number($('x').value),y=Number($('y').value),view=$('view').value==='all'?0:Number($('view').value);const j=await api(`/api/pixel?capture=${id}&view=${view}&x=${x}&y=${y}`);if(ticket!==serial||current.id!==id)return;pickedStrip={capture:id,strip:j.hardwareObject};$('pixel').textContent=JSON.stringify(j,null,2);await memory();}
$('inspect').onclick=safe(inspect);canvas.onclick=safe(async e=>{if(!current)return;const r=canvas.getBoundingClientRect();$('x').value=Math.min(303,Math.floor((e.clientX-r.left)*304/r.width));let y=Math.floor((e.clientY-r.top)*canvas.height/r.height);if($('view').value==='all'){$('view').value=String(Math.floor(y/224));y%=224;draw();}$('y').value=Math.min(223,y);if(pickingLive)pickLiveSprite();else await (pickingChain?pickSpriteGroup():inspect());});
const keys={ArrowUp:1,ArrowDown:2,ArrowLeft:4,ArrowRight:8,KeyZ:16,KeyX:32,KeyA:64,KeyS:128,Enter:256,ShiftLeft:512};let mask=0;
canvas.onkeydown=safe(async e=>{if(!session?.nativeFrontend&&$('keyboard').checked&&keys[e.code]&&!pickingChain){e.preventDefault();mask|=keys[e.code];await control('pad',{p0:mask,p1:0});return;}const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];if(delta){e.preventDefault();$('x').value=Math.max(0,Math.min(303,Number($('x').value)+delta[0]));$('y').value=Math.max(0,Math.min(223,Number($('y').value)+delta[1]));}if(e.key==='Enter'){e.preventDefault();await (pickingChain?pickSpriteGroup():inspect());}});
canvas.onkeyup=safe(async e=>{if($('keyboard').checked&&keys[e.code]&&(mask&keys[e.code])){e.preventDefault();mask&=~keys[e.code];await control('pad',{p0:mask,p1:0});}});
canvas.onblur=safe(async()=>{if(mask){mask=0;await control('pad',{p0:0,p1:0});}});
async function control(action,extra={}){message('Command pending…');const r=await api('/api/control',{action,...extra});message(r.completed!==undefined?`Completed ${r.completed} / ${r.requested} frames`:action+' acknowledged');await poll();await lists();return r;}
document.querySelectorAll('[data-action]').forEach(b=>b.onclick=safe(()=>control(b.dataset.action)));$('speed').onchange=safe(()=>control('speed',{value:Number($('speed').value)}));$('experiment').onclick=safe(()=>control('step',{frames:Number($('frames').value),p0:Number($('p0').value),p1:Number($('p1').value)}));$('record').onclick=safe(()=>control('movie-record',{name:$('movie-name').value}));$('checkpoint-save').onclick=safe(()=>control('checkpoint-save',{name:$('checkpoint-name').value}));
async function memory(){if(!current)return;const id=current.id,ticket=serial,d=Number($('domain').value),o=Number($('offset').value),n=Number($('length').value);const j=await api(`/api/${baseline?'diff':'memory'}?capture=${id}&domain=${d}&offset=${o}&length=${n}${baseline?'&baseline='+baseline:''}`);if(ticket!==serial||current.id!==id)return;$('memory-info').textContent=`${j.domain} · byte offset ${j.offset} / ${j.total} · ${j.byteOrder} · baseline ${j.baseline||'none'}`;const changed=new Set(j.changed),hex=j.hex.match(/../g)||[];$('hex').replaceChildren();for(let i=0;i<hex.length;i+=16){$('hex').append((o+i).toString(16).padStart(6,'0')+'  ');for(let k=i;k<Math.min(i+16,hex.length);k++){const s=document.createElement(changed.has(o+k)?'mark':'span');s.textContent=hex[k]+' ';$('hex').append(s);}$('hex').append('\n');}const regs=await api(`/api/registers?capture=${id}`);if(ticket===serial)$('registers').textContent=regs.names.map((n,i)=>`${n.padEnd(3)} ${regs.values[i].toString(16).padStart(8,'0')}`).join('\n');}
$('read').onclick=safe(async()=>{await pin();await memory()});$('baseline').onclick=safe(async()=>{if(!current)return;await api('/api/lease',{capture:current.id,owner:baseOwner});baseline=current.id;message('Comparison baseline pinned: '+baseline)});
$('scale').onchange=draw;
async function enterSpriteView(track,socket=null){
  const release=held||saved;
  held=saved=invalid=false;
  current=null;
  hasTexture=false;
  changeStream(track,socket);
  $('catalog-evidence').textContent=track?.evidence??'';
  draw();
  status();
  message('');
  if(release)await api('/api/lease',{owner,action:'release'});
}
$('view').onchange=safe(async()=>{
  clearPriority();
  setPickingLive(false);
  if(!chainPickView())setPickingChain(false);
  if(spriteView()){
    await enterSpriteView(selectedTrack()??null);
    return;
  }
  changeStream(null);
  if(priorityView()&&!current){
    $('view').value='1';
    draw();
    status();
    throw Error('Step a frame or play to capture sprite strips.');
  }
  draw();
  status();
  if(priorityView()){
    const id=await pin();
    if(priorityView()&&current?.id===id)await loadPriority(id);
  }
});
$('overlay').onchange=safe(async()=>{draw();if(!$('overlay').checked||!current)return;const id=await pin();const j=await api(`/api/sprites?capture=${id}`);if(current.id!==id)return;capturedBounds=j.bounds||[];capturedBoundsId=id;drawBounds(capturedBounds);message(`${j.objects.length} boundary hardware strips; overlay encloses admitted sprite coverage before FIX.`);$('pixel').textContent=JSON.stringify(j,null,2);});
function draft(){return {id:note?.id,revision:note?.revision,capture:draftCapture,title:$('title').value,category:$('category').value,markdown:$('markdown').value,json:$('json').value,mermaid:$('mermaid').value,attachment:draftAttachment};}
function remember(){dirty=true;localStorage.setItem(draftKey,JSON.stringify(draft()));$('draft-state').textContent='Unsaved draft saved in this browser. Save or discard before changing notes.';drafts();}
function drafts(){const selected=$('recover-list').value;$('recover-list').replaceChildren();for(const key of Object.keys(localStorage).filter(k=>k.startsWith('neo-reaper-comment-draft-v1-')).slice(0,50)){try{const n=JSON.parse(localStorage.getItem(key)),o=document.createElement('option');o.value=key;o.textContent=`${n.title||'Untitled'} · ${key===draftKey?'this tab':'another tab or closed tab'}`;$('recover-list').append(o);}catch{}}if([...$('recover-list').options].some(o=>o.value===selected))$('recover-list').value=selected;$('recover-draft').disabled=!$('recover-list').options.length;}
$('recover-draft').onclick=safe(()=>{guard();const key=$('recover-list').value;if(!key.startsWith('neo-reaper-comment-draft-v1-'))throw Error('Select a browser draft');fill(JSON.parse(localStorage.getItem(key)));remember();$('draft-state').textContent='Recovered browser draft copy. Its original revision is preserved; save may report a conflict.';});
addEventListener('storage',drafts);
for(const id of ['title','category','markdown','json','mermaid'])$(id).addEventListener('input',remember);
async function showAttachment(attachment){
  const ticket=++attachmentTicket;
  const panel=$('note-attachment');
  panel.hidden=true;
  if(!attachment)return;
  const meta=typeof attachment==='string'?await api(`/api/catalog-observation?id=${encodeURIComponent(attachment)}`):attachment;
  const response=await fetch(`/api/catalog-observation?id=${encodeURIComponent(meta.id)}&pixels=1`);
  if(!response.ok)throw Error('The tracked frame could not be opened.');
  const packet=await response.arrayBuffer();
  if(ticket!==attachmentTicket)return;
  paintTrackedPacket(packet,0,$('note-attachment-image'));
  const row=meta.movieInput?` · movie input row ${meta.movieInput.row}`:'';
  const sample=meta.sample?` · tile 0x${meta.sample.tile.toString(16)}`:'';
  $('note-attachment-label').textContent=`${meta.entry.name} · ${catalogLabel(meta.entry)} · frame ${meta.frame}${row} · ${meta.selection}${sample} · ${meta.entry.evidence}`;
  panel.hidden=false;
}
function fill(n){
  note=n.id?n:null;
  draftCapture=n.capture;
  draftAttachment=n.attachment?.id??n.attachment??null;
  for(const id of ['title','category','markdown','json','mermaid'])$(id).value=n[id]??(id==='json'?'{}':id==='category'?'observation':'');
  $('draft-state').textContent=note?'Saved revision '+note.revision:'New comment for '+draftCapture;
  showAttachment(n.attachment).catch(e=>message(e.message));
}
function guard(){if(dirty)throw Error('Save or explicitly discard the current draft before changing notes.');}
$('comment').onclick=safe(async()=>{guard();await pin();fill({capture:current.id});tab('comments');remember();});
$('catalog-attach').onclick=safe(async()=>{
  guard();
  const attachment=await api('/api/catalog-pin',{stream:trackedStream,sequence:trackedSequence});
  fill({capture:attachment.capture,attachment});
  tab('comments');
  remember();
});
$('save-note').onclick=safe(async()=>{const n=await api('/api/comments',draft());note=n;dirty=false;localStorage.removeItem(draftKey);fill(n);drafts();await lists();message('Saved comment revision '+n.revision);});
$('discard').onclick=()=>{dirty=false;localStorage.removeItem(draftKey);note=null;fill({});drafts();$('draft-state').textContent='Draft discarded explicitly.';};
let previewSerial=0;
$('preview').onclick=safe(async()=>{const ticket=++previewSerial;$('note-preview').replaceChildren();const md=document.createElement('div');md.innerHTML=DOMPurify.sanitize(marked.parse($('markdown').value),{FORBID_TAGS:['img','iframe','style'],FORBID_ATTR:['style']});$('note-preview').append(md);const pre=document.createElement('pre');pre.textContent=JSON.stringify(JSON.parse($('json').value),null,2);$('note-preview').append(pre);const text=$('mermaid').value.trim();if(text){if(/%%\{|^---|click\s|https?:/m.test(text))throw Error('Mermaid directives, links and frontmatter are not permitted');mermaid.initialize({startOnLoad:false,securityLevel:'strict',htmlLabels:false,look:'classic',theme:'dark',maxTextSize:8000,flowchart:{htmlLabels:false}});const {svg}=await mermaid.render('diagram-'+crypto.randomUUID(),text);if(ticket!==previewSerial)return;const d=document.createElement('div');d.innerHTML=DOMPurify.sanitize(svg,{USE_PROFILES:{svg:true,svgFilters:true}});$('note-preview').append(d);}});
async function savedCapture(id){guard();held=saved=true;invalid=false;serial++;if(spriteView()){$('view').value='0';changeStream(null);}if(priorityView())clearPriority();await api('/api/lease',{capture:id,owner});const meta=await api('/api/snapshot?capture='+id);await show(meta,await (await fetch('/api/live?capture='+id)).blob(),serial);if(priorityView()&&current?.id===id)await loadPriority(id);await memory();}
async function lists(){for(const [route,container,render] of [['movies','movie-list',(m,row)=>{row.append(document.createTextNode(`${m.name} · ${m.rows} rows `));button(row,'Load paused',()=>control('movie-play',{id:m.id}));button(row,'Input log',async()=>{$('input-log').textContent=JSON.stringify(await api('/api/inputs?id='+m.id),null,2);});}],['checkpoints','checkpoint-list',(c,row)=>{row.append(document.createTextNode(`${c.name} · frame ${c.frame} · ${c.compatible?'compatible':'incompatible'} `));button(row,'Restore machine',()=>control('checkpoint-load',{id:c.id}));}],['comments','note-list',(n,row)=>{row.append(document.createTextNode(`${n.title||'Untitled'} · ${n.category} · r${n.revision} `));button(row,'Edit',()=>{guard();fill(n);tab('comments');});button(row,'Inspect saved frame',async()=>{await savedCapture(n.capture);await showAttachment(n.attachment);});}]]){const values=await api('/api/'+route);$(container).replaceChildren();for(const v of values){const row=document.createElement('div');row.className='row';render(v,row);$(container).append(row);}}}
function button(parent,label,fn){const b=document.createElement('button');b.textContent=label;b.onclick=safe(fn);parent.append(b);}
function drawBounds(bounds){const svg=$('bounds');svg.replaceChildren();const all=$('view').value==='all';svg.setAttribute('viewBox',`0 0 304 ${all?672:224}`);svg.setAttribute('preserveAspectRatio','xMidYMid meet');for(const b of bounds){const rect=document.createElementNS('http://www.w3.org/2000/svg','rect');for(const [k,v]of Object.entries({x:b.x,y:b.y+(all?224:0),width:b.width,height:b.height}))rect.setAttribute(k,v);const title=document.createElementNS(svg.namespaceURI,'title');title.textContent='Captured hardware strip '+b.id;rect.append(title);svg.append(rect);}}
addEventListener('resize',draw);
addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
setInterval(safe(async()=>{await poll();if(current&&held&&!saved)await api('/api/lease',{capture:current.id,owner});if(baseline)await api('/api/lease',{capture:baseline,owner:baseOwner});}),30000);setInterval(poll,1000);
const recovered=localStorage.getItem(draftKey);if(recovered){try{fill(JSON.parse(recovered));dirty=true;$('draft-state').textContent='Recovered unsaved draft; save or discard explicitly.';tab('comments');}catch{message('Draft recovery failed; browser storage preserved.');}}
await poll();await lists();drafts();
if(spriteView()&&selectedTrack()){
  changeStream(selectedTrack()??null);
  draw();
  status();
}else connect();
