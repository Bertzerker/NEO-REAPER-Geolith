// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
class Machine {
 public readonly string Session=Data.NewId(),Token=Data.NewId(),Root,Identity;public readonly object Manifest;
 readonly BlockingCollection<(Func<object>,TaskCompletionSource<object>)> commands=new(16);
 public readonly bool NativeFrontend;
 public volatile bool DesktopConnected;
 bool desktopBooted;
 long lastDesktopFrame;
 byte[] desktopIdentity=[];
 byte[][] desktopLegacyIdentities=[];
 int desktopStateSize;
 TaskCompletionSource? captureReady;
 int HistoryLimit=>NativeFrontend?16:8;
 public double FrameRate=>system==0?59.599484:59.185606;
 public int SampleRate=>system==0?55943:55555;
 readonly object sync=new();readonly Queue<Capture> history=new();readonly Dictionary<string,(Capture,long)> leases=new();
 public volatile bool Running,Cancel,Stepping;public volatile string MovieMode="idle";public volatile string? Failure;public volatile string? ExecutionError;public volatile bool Ready;public double Speed=1;
 public ulong Frame;public int Generation;long sequence,executed,captured;public Capture? Latest;public Movie? ActiveMovie;public int MovieRow;
 public readonly SpriteHelpers Helpers;
 public IReadOnlyDictionary<string,SpriteTrack> Tracks=>Helpers.Current.Tracks;
 int trackedViewers;
 public void TrackEvidence(bool open)=>Interlocked.Add(ref trackedViewers,open?1:-1);
 string? movieVersion;
 readonly Thread thread;readonly int system;readonly byte[] rom,bios;readonly List<double> frameCosts=new(),captureCosts=new();public uint Pad0,Pad1;
 public static readonly string[] Domains=["mainram","z80ram","vram","palette","nvram","cartram","extram","memcard"];
 public static readonly string[] AssetDomains=["P-ROM","S-ROM","M-ROM","V1-ROM","V2-ROM","C-ROM"];
 readonly (int Offset,int Length)[] assets=new (int,int)[6];
 public ReadOnlyMemory<byte> Asset(Capture c,int domain){if(c.Identity!=Identity)throw new ApiError(409,"Immutable assets unavailable for this saved build/configuration; supply its verified assets");var a=assets[domain];return rom.AsMemory(a.Offset,a.Length);}
 public Machine(string romPath,string biosPath,int sys,int region,string root,string helperDirectory,bool nativeFrontend=false){
  NativeFrontend=nativeFrontend;
  Root=root;system=sys;Directory.CreateDirectory(root);rom=File.ReadAllBytes(romPath);bios=File.ReadAllBytes(biosPath);
  var regions=new List<object>();if(rom.Length>=4096){int offset=4096;foreach(var i in Enumerable.Range(0,6)){uint n=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(rom.AsSpan(4+i*4));if(n>rom.Length-offset)break;assets[i]=(offset,(int)n);regions.Add(new{name=new[]{"P","S","M","V1","V2","C"}[i],offset,length=n,sha256=Data.Hash(rom.AsSpan(offset,(int)n).ToArray())});offset+=(int)n;}}
  var firmware=new List<object>();using(var z=new System.IO.Compression.ZipArchive(new MemoryStream(bios))){foreach(var e in z.Entries){if(e.Length>4*1024*1024)throw new Exception("Firmware ZIP component exceeds 4 MiB");using var s=e.Open();using var m=new MemoryStream();s.CopyTo(m);firmware.Add(new{name=e.FullName,length=e.Length,sha256=Data.Hash(m.ToArray())});}}
  string romHash=Data.Hash(rom);
  string systemName=sys==0?"AES":"MVS";
  string firmwareHash=Data.Hash(bios);
  string coreHash=Data.Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"neo-reaper-core.dll")));
  Helpers=new(helperDirectory,romHash,systemName,region);
  Manifest=new{protocol=1,cartridge=new{name=Path.GetFileName(romPath),sha256=romHash,regions},firmware,firmwareZip=firmwareHash,core=coreHash,host=Data.Hash(File.ReadAllBytes(typeof(Machine).Assembly.Location)),revision="2dc88e3fa72ff404213a65d6eb5bb33baa687f73",stateFormat="NR-state-4-MSVC-x64",system=systemName,region,controllers="2 digital pads",palette="resistor network",crop=new{x=8,y=24,width=304,height=224}};
  Identity=Data.Hash(JsonSerializer.SerializeToUtf8Bytes(Manifest,Data.Json));Data.Save(Path.Combine(root,"manifest.json"),Manifest);Data.Save(Path.Combine(root,"identities",Identity+".json"),Manifest);
  if(NativeFrontend){
   byte[] DesktopIdentity(string coreHash)=>System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
    new{romHash,firmwareHash,coreHash,systemName,region,stateFormat="NR-state-4-MSVC-x64"},Data.Json));
   desktopIdentity=DesktopIdentity(coreHash);
   // These earlier cores use the same serialized state and differ only in sprite inspection.
   desktopLegacyIdentities=[
    DesktopIdentity("a1444fa63522b4c50dc648348d973dbd0b947996e2f93689b21bf5702681190d"),
    DesktopIdentity("0facd0afbe90978452e2d7f52bcf9c44453127911acf76d8634cc2319cd0ac3b")
   ];
  }
  thread=new Thread(()=>Loop(region)){IsBackground=true,Name="Geolith emulation owner"};thread.Start();
 }
 public object Status()=>new{protocol=1,product="NEO REAPER - Geolith",session=Session,token=Token,spriteTrackRevision=Helpers.Current.Revision,viewerServer="connected",producer=Failure is not null?"failed":NativeFrontend?(DesktopConnected?"RetroArch connected":"waiting for RetroArch"):Ready?"native in-process":"initializing",machine=Failure is not null?"failed":NativeFrontend?(!DesktopConnected?"waiting":Environment.TickCount64-Interlocked.Read(ref lastDesktopFrame)<250?"running":"paused"):Stepping?"command pending":Running?"running":"paused",nativeFrontend=NativeFrontend,failure=Failure,error=ExecutionError,frame=Frame,generation=Generation,capture=Latest?.Meta(),identity=Identity,manifest=Manifest,movie=new{mode=MovieMode,id=ActiveMovie?.Id,row=MovieRow,total=ActiveMovie?.Rows.Count},speed=Speed,domains=Domains.Concat(AssetDomains).Select((n,i)=>new{id=i,name=n,byteOrder=i is 2 or 3?"big-endian words":i==8?"original NEO word-swapped program storage":"byte-addressed",bus=i>=8?"immutable physical asset region; not logical bank mapping":i==0?"0x100000":i==2?"VRAM word address = byte offset / 2":i==3?"two physical palette banks; CPU 0x400000 banked":"physical storage"}),capabilities=new{spriteTileCatalog=true,capture="native raster",checkpoint="disk; exact build/assets/settings",movies="current-state start; boundary input rows; no timeline edit",lease=new{seconds=120,limit=8,history=HistoryLimit},step=new{min=1,max=3600,stop="completed geo_exec frame"},speed=new[]{0.25,0.5,1,2},trace=new{supported=false,reason="No instruction observer implemented"},sourceWinner=new{supported=true,form="per-pixel classification and exported raw winner map; no separate graphics card"},fetchProvenance=new{supported=false,reason="Raster samples record object/tile/palette; no CPU/DMA fetch chain"},audio=new{supported=NativeFrontend,reason=NativeFrontend?"Audio plays in RetroArch":"Browser audio playback is not implemented"},retailBoards=new{supported=false,reason="Default-board cartridges admitted, retail compatibility unverified; special NGH boards/controllers rejected"},cd=false,service=false}};
 void Loop(int region){try{if(Native.nr_open(rom,(nuint)rom.Length,bios,(nuint)bios.Length,system,region)==0)throw new Exception(Marshal.PtrToStringAnsi(Native.nr_error()));Ready=true;double due=0;var sw=Stopwatch.StartNew();while(true){if(commands.TryTake(out var cmd,Running?0:50)){try{cmd.Item2.SetResult(cmd.Item1());}catch(Exception e){cmd.Item2.SetException(e);}continue;}if(NativeFrontend){if(DesktopConnected&&Frame>0&&(Latest?.Frame!=Frame||Latest?.Generation!=Generation))Capture();continue;}if(!Running)continue;double period=1000/FrameRate/Speed;if(due<sw.Elapsed.TotalMilliseconds-period*2)due=sw.Elapsed.TotalMilliseconds;try{Step(Pad0,Pad1);if(executed%4==0)Capture();}catch(ApiError e){Running=false;ExecutionError=e.Message;continue;}due+=period;int wait=(int)(due-sw.Elapsed.TotalMilliseconds);if(wait>0)Thread.Sleep(Math.Min(wait,100));}}catch(Exception e){Failure=e.ToString();Running=false;}}
 void Step(uint p0,uint p1){var sw=Stopwatch.StartNew();if(MovieMode=="playback"){if(ActiveMovie is null||MovieRow>=ActiveMovie.Rows.Count){Running=false;MovieMode="finished";return;}p0=ActiveMovie.Rows[MovieRow].P0;p1=ActiveMovie.Rows[MovieRow].P1;}
  if(MovieMode=="recording"&&ActiveMovie!.Rows.Count>=36000){Running=false;throw new ApiError(409,"Movie limit reached; save recording before executing more frames");}Frame=Native.nr_step(p0,p1);Interlocked.Increment(ref executed);
  if(!NativeFrontend&&Volatile.Read(ref trackedViewers)==0)foreach(var track in Tracks.Values)track.Publish(Frame);
  if(MovieMode is "recording" or "playback"){string output=Data.Hash(Data.Crop(Native.Copy(Native.nr_video(0),320*264*4))),state=Data.Hash(Native.State());if(MovieMode=="recording"){if(ActiveMovie!.Rows.Count>=36000)throw new ApiError(409,"Movie limit 36000 rows reached; save it");ActiveMovie.Rows.Add(new(ActiveMovie.Rows.Count,p0,p1,output,state,Data.Hash(Native.Audio())));MovieRow=ActiveMovie.Rows.Count;}else{var row=ActiveMovie!.Rows[MovieRow++];if(row.OutputHash!=output||row.StateHash!=state||row.AudioHash!=Data.Hash(Native.Audio())){Running=false;MovieMode="diverged";throw new ApiError(409,$"Movie divergence at row {row.Row}");}if(MovieRow==ActiveMovie.Rows.Count){Running=false;MovieMode="finished";}}}
  if(!NativeFrontend&&Volatile.Read(ref trackedViewers)>0)Capture();
  lock(sync){frameCosts.Add(sw.Elapsed.TotalMilliseconds);if(frameCosts.Count>12000)frameCosts.RemoveRange(0,6000);}
 }
 void Capture(){if(Latest is {} last&&last.Frame==Frame&&last.Generation==Generation)return;var cost=Stopwatch.StartNew();var mem=new byte[8][];for(int d=0;d<8;d++){var p=Native.nr_memory(d,out var n);mem[d]=Native.Copy(p,(int)n);if(d is 2 or 3)for(int j=0;j<mem[d].Length;j+=2)(mem[d][j],mem[d][j+1])=(mem[d][j+1],mem[d][j]);}
  var planes=Enumerable.Range(0,3).Select(p=>Native.Copy(Native.nr_video(p),320*264*4)).ToArray();var rows=Native.Copy(Native.nr_evidence(4),264);if(rows.Skip(24).Take(224).Any(b=>b==0))return;
  var c=new Capture(Session+"-"+(++sequence),Frame,Generation,Identity,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),planes,mem,Native.Copy(Native.nr_evidence(0),320*264),Native.Copy(Native.nr_evidence(1),320*264*4),Native.Copy(Native.nr_evidence(2),320*264*2),Native.Copy(Native.nr_evidence(3),320*264*8),Enumerable.Range(0,29).Select(i=>Native.nr_register((uint)i)).ToArray(),Data.Hash(Native.State()),MovieMode is "recording" or "playback" or "finished" ? ActiveMovie?.Id : null) {
   SpriteSamples=Native.SpriteSamples(),
   MovieInput=MovieMode=="recording"?ActiveMovie?.Rows.LastOrDefault():MovieMode is "playback" or "finished"&&MovieRow>0?ActiveMovie?.Rows[MovieRow-1]:null,
   MovieVersion=MovieMode is "playback" or "finished"?movieVersion:null
  };
  TaskCompletionSource? ready;
  lock(sync){history.Enqueue(c);while(history.Count>HistoryLimit)history.Dequeue();Latest=c;captured++;captureCosts.Add(cost.Elapsed.TotalMilliseconds);if(captureCosts.Count>12000)captureCosts.RemoveRange(0,6000);ready=captureReady;captureReady=null;}
  ready?.TrySetResult();
  foreach(var track in Tracks.Values)track.Publish(Frame,capture:c);
 }
 public (Capture? Capture,Task Next) CaptureState(){
  lock(sync){captureReady??=new(TaskCreationOptions.RunContinuationsAsynchronously);return (Latest,captureReady.Task);}
 }
 public Capture Get(string? id){lock(sync){Prune();if(string.IsNullOrEmpty(id))return Latest??throw new ApiError(409,"No complete fresh capture; step a frame");var c=history.FirstOrDefault(c=>c.Id==id)??leases.Values.FirstOrDefault(v=>v.Item1.Id==id).Item1;if(c!=null)return c;}return Data.Archived(Root,id!);}
 void Prune(){long now=Environment.TickCount64;foreach(var k in leases.Where(p=>p.Value.Item2<now).Select(p=>p.Key).ToArray())leases.Remove(k);}
 public object Lease(string id,string owner,bool release){Data.Id(owner);lock(sync){Prune();if(release){leases.Remove(owner);return new{released=true};}if(id.Length==0)throw new ApiError(400,"Explicit capture required for evidence lease");if(leases.Count>=8&&!leases.ContainsKey(owner))throw new ApiError(429,"Eight evidence leases already in use");var c=Get(id);leases[owner]=(c,Environment.TickCount64+120000);return new{capture=c.Meta(),owner,seconds=120};}}
 public object History(){lock(sync)return history.Select(c=>c.Meta()).ToArray();}
 public Task<object> PrimeTrack(SpriteTrack track)=>Enqueue(JsonSerializer.SerializeToElement(new{action="sprite-preview",id=track.Id}));
 public object Metrics(){lock(sync){var costs=frameCosts.Order().ToArray();return new{executed,captured,history=history.Count,leases=leases.Count,nativeStepP95Ms=costs.Length==0?0:costs[(int)((costs.Length-1)*0.95)],captureCopyP95Ms=captureCosts.Count==0?0:captureCosts.Order().ElementAt((int)((captureCosts.Count-1)*0.95)),captureEveryRunningFrames=NativeFrontend||Volatile.Read(ref trackedViewers)>0?1:4,catalogViewers=Volatile.Read(ref trackedViewers),encoding="detached HTTP/WebSocket; bounded latest-only delivery",sourceHz=FrameRate,workingSet=Environment.WorkingSet};}}
 public Task<object> Enqueue(JsonElement j){
  string action=Str(j,"action");
  if(NativeFrontend&&action!="sprite-preview")throw new ApiError(409,"Use the controls in the RetroArch window.");
  if(action=="pause"){Cancel=true;Running=false;}
  var command=j.Clone();
  return OnOwner(()=>Control(command));
 }
 internal Task<object> OnOwner(Func<object> command){
  if(Failure is not null)throw new ApiError(503,Failure);
  var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
  if(!commands.TryAdd((command,completion)))throw new ApiError(429,"Native command queue full");
  return completion.Task;
 }
 public static int Int(JsonElement j,string n,int def=0)=>j.TryGetProperty(n,out var p)?p.GetInt32():def;
 public static string Str(JsonElement j,string n,string def="")=>j.TryGetProperty(n,out var p)?p.GetString()??def:def;
 void Restore(byte[] b,ulong frame){if(Native.nr_restore(b,(nuint)b.Length,frame)==0)throw new ApiError(409,"Invalid native checkpoint");Frame=frame;Generation++;Latest=null;foreach(var track in Tracks.Values)track.Publish(Frame,reset:true);Running=false;Pad0=Pad1=0;}
 internal int DesktopAttach(){
  if(desktopBooted)DesktopReset();
  desktopBooted=true;
  DesktopConnected=true;
  desktopStateSize=Native.State().Length+40;
  return desktopStateSize;
 }
 internal void DesktopDetach(){
  DesktopConnected=false;
  Latest=null;
  foreach(var track in Tracks.Values)track.Clear(Frame);
 }
 internal (byte[] Video,byte[] Audio) DesktopStep(uint p0,uint p1){
  Step(p0,p1);
  Interlocked.Exchange(ref lastDesktopFrame,Environment.TickCount64);
  Capture();
  var video=Latest is {} latest&&latest.Frame==Frame?latest.Planes[0]:Native.Copy(Native.nr_video(0),320*264*4);
  return (video,Native.Audio());
 }
 internal byte[] DesktopSave(){
  byte[] packet=new byte[desktopStateSize];
  desktopIdentity.CopyTo(packet,0);
  BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(32),Frame);
  Native.State().CopyTo(packet,40);
  return packet;
 }
 internal bool DesktopLoad(byte[] packet){
  if(packet.Length!=desktopStateSize)return false;
  if(!packet.AsSpan(0,32).SequenceEqual(desktopIdentity)&&
     !desktopLegacyIdentities.Any(identity=>packet.AsSpan(0,32).SequenceEqual(identity)))return false;
  ulong frame=BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(32));
  try{Restore(packet[40..],frame);return true;}catch(ApiError){return false;}
 }
 internal void DesktopReset(){
  Native.nr_reset();
  Frame=0;
  Generation++;
  Latest=null;
  foreach(var track in Tracks.Values)track.Clear(Frame);
 }
 object Control(JsonElement j){string a=Str(j,"action");if(a=="pause"){Running=false;return Status();}if(a=="speed"){double n=j.GetProperty("value").GetDouble();if(!new[]{0.25,0.5,1,2}.Contains(n))throw new ApiError(400,"Unsupported speed");Speed=n;return Status();}
  if(a=="run"){if(MovieMode is "finished" or "diverged")throw new ApiError(409,"Stop or restart the movie before running");ExecutionError=null;Cancel=false;Running=true;return Status();}
  if(a=="pad"){if(MovieMode=="playback")throw new ApiError(409,"Movie input is authoritative");Pad0=Mask(j,"p0");Pad1=Mask(j,"p1");return Status();}
  if(a=="step")return Steps(j);
  if(a=="sprite-preview"){if(!Tracks.TryGetValue(Str(j,"id"),out var track))throw new ApiError(404,"Unknown sprite track");track.Publish(Frame);return new{frame=Frame};}
  if(a=="checkpoint-save"){if(MovieMode!="idle")throw new ApiError(409,"Stop movie before checkpoint operations");var c=new Checkpoint(Data.NewId(),Name(j),Identity,Frame,Convert.ToBase64String(Native.State()),Pad0,Pad1);Data.Save(Path.Combine(Root,"checkpoints",c.Id+".json"),c);return new{id=c.Id,name=c.Name,frame=c.Frame};}
  if(a=="checkpoint-load"){Idle();var c=Data.Read<Checkpoint>(Path.Combine(Root,"checkpoints",Data.Id(Str(j,"id"))+".json"));Compatible(c.Identity);Restore(Convert.FromBase64String(c.State),c.Frame);Pad0=c.P0;Pad1=c.P1;return Status();}
  if(a=="movie-record"){Idle();Running=false;ActiveMovie=new(Data.NewId(),Name(j),Identity,Frame,Convert.ToBase64String(Native.State()),[]);MovieMode="recording";MovieRow=0;return Status();}
  if(a=="movie-save"){if(MovieMode!="recording")throw new ApiError(409,"No recording");Running=false;Data.Save(Path.Combine(Root,"movies",ActiveMovie!.Id+".json"),ActiveMovie);MovieMode="idle";return Status();}
  if(a=="movie-stop"){Running=false;if(ActiveMovie is not null)foreach(var track in Tracks.Values)track.Clear(Frame);MovieMode="idle";ActiveMovie=null;MovieRow=0;movieVersion=null;Pad0=Pad1=0;return Status();}
  if(a=="movie-play"||a=="movie-restart"){if(MovieMode=="recording")throw new ApiError(409,"Save recording first");var m=a=="movie-restart"?ActiveMovie??throw new ApiError(409,"No movie"):Data.Read<Movie>(Path.Combine(Root,"movies",Data.Id(Str(j,"id"))+".json"));Compatible(m.Identity);if(m.Rows.Count<1||m.Rows.Count>36000)throw new ApiError(409,"Movie must contain 1..36000 rows");movieVersion=CatalogEvidence.SaveMovieVersion(Root,m);ExecutionError=null;Restore(Convert.FromBase64String(m.Start),m.Frame);ActiveMovie=m;MovieRow=0;MovieMode="playback";return Status();}
  throw new ApiError(400,"Unknown or unsupported action");
 }
 object Steps(JsonElement j){
  int n=Int(j,"frames",1);if(n<1||n>3600)throw new ApiError(400,"Frames must be 1..3600");uint p0=Mask(j,"p0"),p1=Mask(j,"p1");Running=false;Cancel=false;int done=0;Stepping=true;
  try{for(;done<n&&!Cancel;done++){if(MovieMode is "finished" or "diverged")break;Step(p0,p1);}}
  finally{Stepping=false;Pad0=Pad1=0;}
  if(done>0)Capture();return new{requested=n,completed=done,state=Status()};
 }
 uint Mask(JsonElement j,string n){int x=Int(j,n);int max=1023;if(x<0||(x&~max)!=0)throw new ApiError(400,"Unsupported pad mask/topology");return (uint)x;}
 void Idle(){if(MovieMode!="idle")throw new ApiError(409,"Stop the active movie first");}
 void Compatible(string id){if(id!=Identity)throw new ApiError(409,"Incompatible cartridge/firmware/core/build/machine configuration");}
 string Name(JsonElement j){string s=Str(j,"name","Untitled").Trim();if(s.Length>120)throw new ApiError(400,"Name too long");return s;}
}
