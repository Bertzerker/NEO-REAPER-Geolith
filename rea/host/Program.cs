// SPDX-License-Identifier: MIT
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Buffers.Binary;

var options=new Dictionary<string,string>();for(int i=0;i<args.Length;i+=2){if(i+1>=args.Length)throw new Exception("Every option requires a value");options.Add(args[i],args[i+1]);}
string Required(string n)=>options.GetValueOrDefault(n)??throw new Exception("Required: "+n);
int port=int.Parse(options.GetValueOrDefault("--port","8910"));if(port<1024||port>65535)throw new Exception("Port must be 1024..65535");
var machine=new Machine(Required("--rom"),Required("--firmware"),options.GetValueOrDefault("--system","AES")=="MVS"?1:0,int.Parse(options.GetValueOrDefault("--region","0")),Path.GetFullPath(options.GetValueOrDefault("--data",Path.Combine(AppContext.BaseDirectory,"../session"))));
string web=Path.GetFullPath(options.GetValueOrDefault("--web",Path.Combine(AppContext.BaseDirectory,"../public")));
var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();Console.WriteLine($"NEO REAPER - Geolith http://127.0.0.1:{port}/ (loopback only)");
var storeLock=new object();int clients=0;long streamSent=0,streamSkipped=0;
while(true){var context=await listener.GetContextAsync();_=Task.Run(()=>Handle(context));}

async Task Handle(HttpListenerContext ctx){var q=ctx.Request;var r=ctx.Response;r.Headers["X-Content-Type-Options"]="nosniff";r.Headers["Cache-Control"]="no-store";r.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
 try{
  if(q.Headers["Host"]!=$"127.0.0.1:{port}")throw new ApiError(403,"Invalid Host");
  string? origin=q.Headers["Origin"];if(origin!=null&&origin!=$"http://127.0.0.1:{port}")throw new ApiError(403,"Invalid Origin");
  string path=q.Url!.AbsolutePath;
  if(path=="/stream"){if(!q.IsWebSocketRequest)throw new ApiError(400,"WebSocket required");if(origin!=$"http://127.0.0.1:{port}")throw new ApiError(403,"WebSocket Origin required");if(Interlocked.Increment(ref clients)>4){Interlocked.Decrement(ref clients);throw new ApiError(429,"Four viewers maximum");}try{var ws=(await ctx.AcceptWebSocketAsync(null)).WebSocket;async Task Receive(){try{byte[] b=new byte[1];while(ws.State==WebSocketState.Open){var m=await ws.ReceiveAsync(b,CancellationToken.None);if(m.MessageType==WebSocketMessageType.Close){await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"Closed",CancellationToken.None);return;}await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,"Server-only stream",CancellationToken.None);return;}}catch{}}var receiver=Receive();string last="";long previousSequence=0;while(ws.State==WebSocketState.Open&&!receiver.IsCompleted){var c=machine.Latest;if(c!=null&&c.Id!=last){var meta=JsonSerializer.SerializeToUtf8Bytes(c.Meta(),Data.Json);var png=Data.Atlas(c);var packet=new byte[4+meta.Length+png.Length];BinaryPrimitives.WriteInt32LittleEndian(packet,meta.Length);meta.CopyTo(packet,4);png.CopyTo(packet,4+meta.Length);using var timeout=new CancellationTokenSource(2000);await ws.SendAsync(packet,WebSocketMessageType.Binary,true,timeout.Token);long sequence=long.Parse(c.Id[(c.Id.LastIndexOf('-')+1)..]);if(previousSequence>0)Interlocked.Add(ref streamSkipped,Math.Max(0,sequence-previousSequence-1));previousSequence=sequence;Interlocked.Increment(ref streamSent);last=c.Id;}await Task.Delay(65);}ws.Dispose();}finally{Interlocked.Decrement(ref clients);}return;}
  JsonElement body=default;
  if(q.HttpMethod=="POST"){
   if(origin!=null&&origin!=$"http://127.0.0.1:{port}")throw new ApiError(403,"Invalid Origin");if(q.Headers["X-REA-Token"]!=machine.Token)throw new ApiError(403,"Fresh session control token required");if(q.ContentType?.Split(';')[0]!="application/json")throw new ApiError(415,"POST requires JSON");
   using var ms=new MemoryStream();byte[] buf=new byte[8192];int n;while((n=await q.InputStream.ReadAsync(buf))>0){if(ms.Length+n>65536)throw new ApiError(413,"JSON body exceeds 64 KiB");ms.Write(buf,0,n);}using var doc=JsonDocument.Parse(ms.ToArray());body=doc.RootElement.Clone();
   if(path=="/api/control"){var result=await machine.Enqueue(body).WaitAsync(TimeSpan.FromSeconds(120));await Json(result);return;}
   if(path=="/api/lease"){await Json(machine.Lease(Machine.Str(body,"capture"),Machine.Str(body,"owner"),Machine.Str(body,"action")=="release"));return;}
   if(path=="/api/comments"){
    Note note;lock(storeLock){string id=Machine.Str(body,"id");Note? previous=null;if(id.Length>0){id=Data.Id(id);previous=Data.Read<Note>(Path.Combine(machine.Root,"comments",id+".json"));if(Machine.Int(body,"revision")!=previous.Revision)throw new ApiError(409,"Comment revision conflict; local draft preserved");}else id=Data.NewId();
     string title=Machine.Str(body,"title"),category=Machine.Str(body,"category","observation"),md=Machine.Str(body,"markdown"),js=Machine.Str(body,"json","{}"),mermaid=Machine.Str(body,"mermaid");if(title.Length>160||md.Length>24000||js.Length>12000||mermaid.Length>8000)throw new ApiError(400,"Comment field exceeds limit");if(category is not ("observation" or "hypothesis"))throw new ApiError(400,"Invalid category");using var _=JsonDocument.Parse(js);
     string capture=previous?.Capture??Machine.Str(body,"capture");if(capture.Length==0)throw new ApiError(400,"Explicit displayed capture required for a new comment");var c=machine.Get(capture);if(c.Movie!=null&&!File.Exists(Path.Combine(machine.Root,"movies",Data.Id(c.Movie)+".json")))throw new ApiError(409,"Save the movie before archiving its capture");Data.Archive(machine.Root,c);note=new(id,(previous?.Revision??0)+1,title,category,md,js,mermaid,c.Id,c.Movie,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());Data.Save(Path.Combine(machine.Root,"revisions",id,"r"+note.Revision+".json"),note);Data.Save(Path.Combine(machine.Root,"comments",id+".json"),note);
    }await Json(note);return;
   }
   throw new ApiError(404,"Unknown mutation route");
  }
  if(q.HttpMethod!="GET")throw new ApiError(405,"GET or POST required");
  string Arg(string n,string def="")=>q.QueryString[n]??def;
  int Number(string n,int def,int min,int max){if(!int.TryParse(Arg(n,def.ToString()),out int v)||v<min||v>max)throw new ApiError(400,"Invalid "+n);return v;}
  Capture Selected(){string id=Arg("capture");if(id.Length==0)throw new ApiError(400,"Explicit capture ID required");return machine.Get(id);}
  if(path=="/api/session"){await Json(machine.Status());return;}
  if(path=="/api/metrics"){var metrics=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(machine.Metrics(),Data.Json))!.AsObject();metrics["streamDeliveries"]=Interlocked.Read(ref streamSent);metrics["streamSkippedCaptures"]=Interlocked.Read(ref streamSkipped);metrics["streamClients"]=clients;await Json(metrics);return;}
  if(path=="/api/history"){await Json(machine.History());return;}
  if(path=="/api/snapshot"){await Json(machine.Get(Arg("capture")).Meta());return;}
  if(path=="/api/live"){await Bytes(Data.Atlas(Selected()),"image/png");return;}
  if(path=="/api/image"){var c=Selected();int view=Number("view",0,0,2);await Bytes(Data.Png(Data.Crop(c.Planes[view]),304,224,view==0),"image/png");return;}
  if(path=="/api/memory"||path=="/api/diff"){
   var c=Selected();int d=Number("domain",0,0,13);ReadOnlyMemory<byte> data=d<8?c.Memory[d]:machine.Asset(c,d-8);int offset=Number("offset",0,0,data.Length),length=Number("length",256,0,4096);length=Math.Min(length,data.Length-offset);var bytes=data.Slice(offset,length).ToArray();Capture? baseline=null;if(path=="/api/diff"){baseline=machine.Get(Arg("baseline"));if(Arg("baseline")==""||baseline.Identity!=c.Identity)throw new ApiError(409,"Explicit compatible baseline required");}await Json(new{capture=c.Id,domain=d<8?Machine.Domains[d]:Machine.AssetDomains[d-8],offset,length,total=data.Length,unit="bytes",byteOrder=d is 2 or 3?"big-endian 16-bit words":d==8?"original NEO word-swapped program storage":d>8?"immutable physical asset bytes":"bytes in bus order",immutable=d>=8,bytes,hex=Convert.ToHexStringLower(bytes),baseline=baseline?.Id,changed=baseline is null||d>=8?[]:Enumerable.Range(0,length).Where(i=>bytes[i]!=baseline.Memory[d][offset+i]).Select(i=>offset+i).ToArray()});return;
  }
  if(path=="/api/pixel"){
   var c=Selected();int x=Number("x",0,0,303),y=Number("y",0,0,223),v=Number("view",0,0,2),i=(y+24)*320+x+8,winner=c.Winner[i];var b=c.Planes[v];int layer=v==0?winner-1:v-1;int pal=layer<0?-1:BinaryPrimitives.ReadUInt16LittleEndian(c.Palette.AsSpan((layer*320*264+i)*2));uint tile=layer<0?uint.MaxValue:BinaryPrimitives.ReadUInt32LittleEndian(c.Tiles.AsSpan((layer*320*264+i)*4));await Json(new{capture=c.Id,frame=c.Frame,view=v,x,y,nativeX=x+8,nativeY=y+24,rgba=new int[]{b[i*4+2],b[i*4+1],b[i*4],v==0?255:b[i*4+3]},winner=new[]{"Backdrop","Sprite","FIX"}[winner],paletteEntry=pal==65535?-1:pal,paletteBank=pal<0||pal==65535?(int?)null:pal/4096,paletteIndexInBank=pal<0||pal==65535?(int?)null:pal%4096,tile=tile==uint.MaxValue?(uint?)null:tile,hardwareObject=winner==1||v==1?BinaryPrimitives.ReadUInt16LittleEndian(c.Objects.AsSpan(i*2)):(int?)null,scope="Raster-time admitted samples; sprite plane is the last sprite sample before FIX",fetchAddress="unavailable",cpuDmaProvenance="unavailable",gameCollision="unavailable"});return;
  }
  if(path=="/api/registers"){var c=Selected();await Json(new{capture=c.Id,names=new[]{"PC","SR","D0","D1","D2","D3","D4","D5","D6","D7","A0","A1","A2","A3","A4","A5","A6","A7","Palette bank","VRAM address","VRAM bank","VRAM modulo (signed16)","Shadow","Auto counter","Auto disable","Auto reload","Auto timer","Boundary scanline","Boundary M68K cycles in line"}.Take(c.Registers.Length),values=c.Registers,evidence="Boundary CPU/LSPC registers; pixel palette samples are raster-time evidence"});return;}
  if(path=="/api/sprites"){
   var c=Selected();var m=c.Memory[2];int W(int word)=>BinaryPrimitives.ReadUInt16BigEndian(m.AsSpan(word*2));var bounds=new Dictionary<int,int[]>();for(int y=0;y<224;y++)for(int x=0;x<304;x++){int i=(y+24)*320+x+8;int obj=BinaryPrimitives.ReadUInt16LittleEndian(c.Objects.AsSpan(i*2));if(obj==65535)continue;if(!bounds.TryGetValue(obj,out var b))bounds[obj]=[x,y,x,y];else{b[0]=Math.Min(b[0],x);b[1]=Math.Min(b[1],y);b[2]=Math.Max(b[2],x);b[3]=Math.Max(b[3],y);}}
   await Json(new{capture=c.Id,evidence="Boundary strip controls are memory diagnostics. Bounds enclose captured admitted sprite samples before FIX, including gaps; no collision geometry.",bounds=bounds.Select(b=>new{id=b.Key,x=b.Value[0],y=b.Value[1],width=b.Value[2]-b.Value[0]+1,height=b.Value[3]-b.Value[1]+1}),objects=Enumerable.Range(1,381).Where(i=>(W(0x8200+i)&63)!=0||(W(0x8200+i)&64)!=0).Select(i=>new{id=i,x=(W(0x8400+i)>>7)&511,y=(512-(W(0x8200+i)>>7))&511,heightTiles=W(0x8200+i)&63,chained=(W(0x8200+i)&64)!=0,hZoom=(W(0x8000+i)>>8)&15,vZoom=W(0x8000+i)&255}).ToArray()});return;
  }
  if(path=="/api/comments"){await Json(List<Note>("comments"));return;}
  if(path=="/api/checkpoints"){await Json(List<Checkpoint>("checkpoints").Select(c=>new{id=c.Id,name=c.Name,frame=c.Frame,compatible=c.Identity==machine.Identity}));return;}
  if(path=="/api/movies"){await Json(List<Movie>("movies").Select(m=>new{id=m.Id,name=m.Name,frame=m.Frame,rows=m.Rows.Count,compatible=m.Identity==machine.Identity}));return;}
  if(path=="/api/inputs"){var m=Data.Read<Movie>(Path.Combine(machine.Root,"movies",Data.Id(Arg("id"))+".json"));int start=Number("start",0,0,m.Rows.Count),n=Number("length",60,0,600);await Json(new{id=m.Id,convention="row 0 supplies both pad masks for first geo_exec after embedded starting state; hashes describe its completed boundary",rows=m.Rows.Skip(start).Take(n)});return;}
  if(path=="/api/export"){
   var c=Selected();var files=new SortedDictionary<string,byte[]>{{"capture.json",JsonSerializer.SerializeToUtf8Bytes(c,Data.Json)},{"capture-meta.json",JsonSerializer.SerializeToUtf8Bytes(c.Meta(),Data.Json)},{"identity.json",JsonSerializer.SerializeToUtf8Bytes(new{c.Identity,manifest=Data.Read<JsonElement>(Path.Combine(machine.Root,"identities",Data.Id(c.Identity)+".json"))},Data.Json)},{"atlas.png",Data.Atlas(c)}};
   foreach(var n in List<Note>("comments").Where(n=>n.Capture==c.Id)){string revisions=Path.Combine(machine.Root,"revisions",Data.Id(n.Id));if(Directory.Exists(revisions)){foreach(var revision in Directory.GetFiles(revisions,"*.json").Take(1000))files[$"notes/{n.Id}-{Path.GetFileName(revision)}"]=File.ReadAllBytes(revision);}else files[$"notes/{n.Id}-r{n.Revision}.json"]=JsonSerializer.SerializeToUtf8Bytes(n,Data.Json);}
   if(c.Movie!=null){string movie=Path.Combine(machine.Root,"movies",Data.Id(c.Movie)+".json");if(File.Exists(movie))files["movie.json"]=File.ReadAllBytes(movie);}
   files["hashes.json"]=JsonSerializer.SerializeToUtf8Bytes(files.ToDictionary(p=>p.Key,p=>Data.Hash(p.Value)),Data.Json);
   using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){foreach(var f in files){using var s=zip.CreateEntry(f.Key,CompressionLevel.Fastest).Open();s.Write(f.Value);}}r.Headers["Content-Disposition"]="attachment; filename=neo-reaper-evidence.zip";await Bytes(ms.ToArray(),"application/zip");return;
  }
  string? file=path switch{"/"=>"index.html","/app.mjs"=>"app.mjs","/style.css"=>"style.css",_=>path.StartsWith("/vendor/")&&path[8..] is "marked.umd.js" or "purify.min.js" or "mermaid.min.js"?path[1..]:null};
  if(file==null||!File.Exists(Path.Combine(web,file)))throw new ApiError(404,"Unknown route");await Bytes(File.ReadAllBytes(Path.Combine(web,file)),file.EndsWith(".css")?"text/css":file.EndsWith(".html")?"text/html":"text/javascript");
 }catch(Exception e){try{r.StatusCode=e is ApiError a?a.Status:e is FileNotFoundException?404:e is JsonException or FormatException or InvalidOperationException?400:500;await Json(new{error=e is TimeoutException?"Control outcome unknown; query /api/session before any retry":e.Message});}catch{}}
 finally{try{r.Close();}catch{}}
 async Task Json(object o)=>await Bytes(JsonSerializer.SerializeToUtf8Bytes(o,Data.Json),"application/json");
 async Task Bytes(byte[] b,string type){r.ContentType=type;r.ContentLength64=b.Length;await r.OutputStream.WriteAsync(b);}
}
IEnumerable<T> List<T>(string folder){string p=Path.Combine(machine.Root,folder);return Directory.Exists(p)?Directory.GetFiles(p,"*.json").Take(1000).Select(Data.Read<T>).ToArray():[];}
