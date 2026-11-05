// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

sealed class SpriteHelpers {
 public record Catalog(IReadOnlyDictionary<string,SpriteTrack> Tracks,long Revision);
 public record Document(string File,string Text,string Revision);
 readonly string directory,hash,system;
 readonly int region;
 readonly object sync=new();
 volatile Catalog catalog;
 public Catalog Current=>catalog;

 public SpriteHelpers(string directory,string hash,string system,int region){
  this.directory=directory;
  this.hash=hash;
  this.system=system;
  this.region=region;
  catalog=new(Load(),0);
 }

 IEnumerable<string> Files()=>Directory.Exists(directory)
  ?Directory.EnumerateFiles(directory,"*.json").Where(file=>(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)
   .Order(StringComparer.Ordinal)
  :[];

 public string[] List(){lock(sync)return Files().Select(Path.GetFileName).OfType<string>().ToArray();}

 string FilePath(string name){
  if(name.Length is <6 or >100||!name.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||
     !char.IsAsciiLetterOrDigit(name[0])||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('-' or '_' or '.')))
   throw new ApiError(400,"Use a .json filename with letters, digits, hyphens, underscores or periods.");
  string stem=name.Split('.')[0].ToUpperInvariant();
  if(stem is "CON" or "PRN" or "AUX" or "NUL"||
     (stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&char.IsAsciiDigit(stem[3])))
   throw new ApiError(400,"Reserved filename.");
  string path=Path.Combine(directory,name);
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
   throw new ApiError(400,"The helper file must be a regular file.");
  return path;
 }

 static Document Read(string file){
  byte[] bytes=File.ReadAllBytes(file);
  var contents=bytes.AsSpan();
  if(contents.StartsWith(Encoding.UTF8.Preamble))contents=contents[Encoding.UTF8.Preamble.Length..];
  return new(Path.GetFileName(file),Encoding.UTF8.GetString(contents),Data.Hash(bytes));
 }

 public Document Get(string name){lock(sync)return Read(FilePath(name));}
 public object Reload(){
  lock(sync){
   try{catalog=new(Load(),catalog.Revision+1);}
   catch(InvalidDataException e){throw new ApiError(400,e.Message);}
   return new{catalog.Revision};
  }
 }

 public object Change(string name,string revision,string? contents){
  lock(sync){
   string path=FilePath(name);
   var previous=File.Exists(path)?Read(path):null;
   if((previous?.Revision??"")!=revision||(contents is null&&previous is null))
    throw new ApiError(409,"The file changed on disk. Copy your draft before reloading it.");
   byte[]? bytes=contents is null?null:Encoding.UTF8.GetBytes(contents);
   Dictionary<string,SpriteTrack> tracks;
   try{tracks=Load(path,bytes);}
   catch(InvalidDataException e){throw new ApiError(400,e.Message);}
   if(bytes is null)File.Delete(path);
   else{
    Directory.CreateDirectory(directory);
    string temp=Path.Combine(directory,Data.NewId()+".tmp");
    try{
     File.WriteAllBytes(temp,bytes);
     File.Move(temp,path,previous!=null);
    }finally{File.Delete(temp);}
   }
   catalog=new(tracks,catalog.Revision+1);
   return bytes is null?new{file=name}:new Document(name,contents!,Data.Hash(bytes));
  }
 }

 Dictionary<string,SpriteTrack> Load(string? edit=null,byte[]? replacement=null) {
  var tracks=new Dictionary<string,SpriteTrack>(StringComparer.Ordinal);
  var files=Files().Where(file=>!string.Equals(file,edit,StringComparison.OrdinalIgnoreCase));
  if(replacement!=null)files=files.Append(edit!).Order(StringComparer.Ordinal);
  foreach(string file in files){
   bool edited=file==edit;
   try{
    var bytes=edited?replacement!:File.ReadAllBytes(file);
    ReadOnlyMemory<byte> json=bytes;
    if(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))json=json[Encoding.UTF8.Preamble.Length..];
    using var document=JsonDocument.Parse(json);
    var helper=document.RootElement;
    string? cartridge=helper.GetProperty("cartridgeSha256").GetString();
    string? platform=helper.GetProperty("system").GetString();
    int area=helper.GetProperty("region").GetInt32();
    bool matches=string.Equals(cartridge,hash,StringComparison.OrdinalIgnoreCase)&&platform==system&&area==region;
    if(!matches&&!edited)continue;
    if(edited&&(cartridge is not {Length:64}||!cartridge.All(char.IsAsciiHexDigit)||
       platform is not ("AES" or "MVS")||area is <0 or >3))throw new FormatException("Invalid cartridge SHA-256, system or region.");
    if(helper.TryGetProperty("schemaVersion",out var schema)&&schema.GetInt32()!=1)
     throw new FormatException("Unsupported catalog schema version.");
    var target=matches?tracks:new Dictionary<string,SpriteTrack>(StringComparer.Ordinal);
    string revision=Data.Hash(bytes);
    CatalogEntry Entry(JsonElement item,string kind,string source,string evidence){
     string id=item.GetProperty("id").GetString()??"";
     string name=item.GetProperty("name").GetString()??"";
     if(id.Length is <1 or >48||id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||
        string.IsNullOrWhiteSpace(name)||name.Length>80)throw new FormatException("Invalid catalog ID or name.");
     string? notes=item.TryGetProperty("notes",out var note)?note.GetString():null;
     string[] labels=item.TryGetProperty("labels",out var tags)?tags.EnumerateArray().Select(tag=>tag.GetString()??"").ToArray():[];
     if(notes?.Length>2000||labels.Length>16||labels.Any(label=>label.Length is <1 or >48))
      throw new FormatException("Catalog labels or notes exceed their limits.");
     return new(id,name,kind,source,Path.GetFileName(file),revision,item.GetProperty("selectors").Clone(),evidence,notes,labels);
    }
    void Add(SpriteTrack track){
     if(!target.TryAdd(track.Id,track))throw new FormatException($"Duplicate catalog ID '{track.Id}'.");
    }
    if(helper.TryGetProperty("sprites",out var sprites))foreach(var sprite in sprites.EnumerateArray()){
     var entry=Entry(sprite,"sprite","sprite","Admitted sprite strip writes in hardware order. Group boundaries use hardware chains and inferred seams.");
     var rules=new List<uint>();
     foreach(var selector in entry.Selectors.EnumerateArray()){
      bool hasPalette=selector.TryGetProperty("palette",out _);
      bool hasTiles=hasPalette||selector.TryGetProperty("firstTile",out _)||selector.TryGetProperty("lastTile",out _);
      bool hasStrips=selector.TryGetProperty("firstStrip",out _)||selector.TryGetProperty("lastStrip",out _);
      if(!hasTiles&&!hasStrips)throw new FormatException("A selector needs a tile range, a strip range, or both.");
      uint palette=hasPalette?Hex(selector,"palette",0xff):0x100;
      uint first=hasTiles?Hex(selector,"firstTile",0xfffff):0,last=hasTiles?Hex(selector,"lastTile",0xfffff):0xfffff;
      uint firstStrip=hasStrips?Hex(selector,"firstStrip",SpritePriority.LastStrip):SpritePriority.FirstStrip;
      uint lastStrip=hasStrips?Hex(selector,"lastStrip",SpritePriority.LastStrip):SpritePriority.LastStrip;
      if(first>last)throw new FormatException("firstTile must be at or below lastTile.");
      if(firstStrip<SpritePriority.FirstStrip||firstStrip>lastStrip)
       throw new FormatException("The strip range must be ascending and within 1–381.");
      rules.AddRange([palette,first,last,firstStrip,lastStrip]);
     }
     if(rules.Count is <5 or >80)throw new FormatException("A sprite needs 1–16 selectors.");
     Add(new SpriteTrack(entry,rules.ToArray()));
    }
    if(helper.TryGetProperty("tiles",out var tiles))foreach(var tile in tiles.EnumerateArray()){
     string source=tile.GetProperty("source").GetString()??"";
     if(source is not ("sprite" or "fix"))throw new FormatException("Tile source must be sprite or fix.");
     var entry=Entry(tile,"tile",source,source=="fix"?TileCatalog.FixEvidence:TileCatalog.SpriteEvidence);
     int count=entry.Selectors.GetArrayLength();
     if(count is <1 or >16)throw new FormatException("A tile needs 1–16 selectors.");
     var selectors=entry.Selectors.EnumerateArray().Select(selector=>{
      if(selector.EnumerateObject().Any(field=>field.Name is not ("tile" or "palette" or "bank")))
       throw new FormatException("Tile selectors accept only tile, palette and bank.");
      return new TileSelector(Hex(selector,"tile",source=="fix"?0xffffu:0xfffffu),
       selector.TryGetProperty("palette",out _)?Hex(selector,"palette",source=="fix"?15u:255u):null,
       selector.TryGetProperty("bank",out _)?Hex(selector,"bank",1):null);
     }).ToArray();
     Add(new SpriteTrack(entry,[],new TileCatalog(source,selectors)));
    }
   }catch(Exception e) when(e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException){
    throw new InvalidDataException($"Invalid sprite helper {Path.GetFileName(file)}. {e.Message}",e);
   }
  }
  return tracks;
 }

 static uint Hex(JsonElement selector,string field,uint max){
  var value=selector.GetProperty(field);
  if(value.ValueKind==JsonValueKind.Number&&value.TryGetUInt32(out uint number)&&number<=max)return number;
  if(value.ValueKind==JsonValueKind.String&&value.GetString() is {} hex&&hex.StartsWith("0x",StringComparison.OrdinalIgnoreCase)&&
     uint.TryParse(hex.AsSpan(2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out number)&&number<=max)return number;
  throw new FormatException($"Invalid {field}. Use an unsigned number or a 0x-prefixed hexadecimal string.");
 }
}

sealed class SpriteTrack(CatalogEntry entry,uint[] rules,TileCatalog? tiles=null) {
 public CatalogEntry Entry { get; }=entry;
 public string Id=>Entry.Id;
 public string Name=>Entry.Name;
 public bool IsTile=>tiles is not null;
 public bool Matches(uint strip,uint palette,uint tile){
  for(int i=0;i<rules.Length;i+=5)
   if((rules[i]==0x100||rules[i]==palette)&&tile>=rules[i+1]&&tile<=rules[i+2]&&
      strip>=rules[i+3]&&strip<=rules[i+4])return true;
  return false;
 }
 readonly object sync=new();
 int viewers;
 byte[]? latest;
 ulong latestFrame;
 TaskCompletionSource? updated;
 readonly ConcurrentDictionary<SpriteFollower,byte> followers=new();
 readonly Queue<SpriteMatchFrame> history=new();
 int historySamples;

 public void Open(){lock(sync)viewers++;}
 public void Close(){lock(sync){if(--viewers==0)latest=null;}}
 public SpriteFollower OpenFollower(){
  var follower=new SpriteFollower();
  followers.TryAdd(follower,0);
  return follower;
 }
 public void CloseFollower(SpriteFollower follower)=>followers.TryRemove(follower,out _);
 public void PrimeFollower(SpriteFollower follower,ulong frame,Capture? capture=null){
  Publish(frame,capture:capture);
  follower.Clear(history.LastOrDefault(),frame);
 }
 public void Pick(SpriteFollower follower,ulong frame,int x,int y)=>follower.Pick(history.ToArray(),frame,x,y);
 public void Browse(SpriteFollower follower,ulong frame,int x,int y,int direction)=>follower.Browse(history.ToArray(),frame,x,y,direction);
 public void ConfirmPreview(SpriteFollower follower)=>follower.ConfirmPreview(history.LastOrDefault());
 public void PickFromCapture(SpriteFollower follower,SpriteMatchFrame seed,ulong frame,int x,int y,Capture? capture=null){
  Publish(frame,capture:capture);
  var frames=history.Where(sample=>sample.Number>seed.Number).Prepend(seed).ToArray();
  follower.Pick(frames,seed.Number,x,y);
 }
 public void ClearPick(SpriteFollower follower,ulong frame)=>follower.Clear(history.LastOrDefault(),frame);
 public (byte[]? Frame,Task Next) State(){
  lock(sync){
   updated??=new(TaskCreationOptions.RunContinuationsAsynchronously);
   return (latest,updated.Task);
  }
 }
 public void Clear(ulong frame)=>Publish(frame,reset:true,empty:true);
 public void Publish(ulong frame,bool reset=false,bool empty=false,Capture? capture=null){
  if(reset||followers.IsEmpty){
   history.Clear();
   historySamples=0;
   if(reset)foreach(var follower in followers)follower.Key.Reset(frame);
  }else if(history.LastOrDefault()?.Number!=frame&&(tiles is null||capture is not null)){
   var matched=tiles is null?new SpriteMatchFrame(frame,Native.FollowSamples(rules),capture?.Id):tiles.Match(capture!);
   history.Enqueue(matched);
   historySamples+=matched.SampleCount;
   while(history.Count>30||(history.Count>1&&historySamples>350000))
    historySamples-=history.Dequeue().SampleCount;
   foreach(var follower in followers)follower.Key.Publish(matched);
  }
  if(tiles is not null)return;
  lock(sync){if(viewers==0||(!reset&&latest!=null&&latestFrame==frame))return;}
  byte[] packet=Native.FollowFrame(frame,empty?[]:rules);
  TaskCompletionSource? pending;
  lock(sync){
   if(viewers==0)return;
   latest=packet;
   latestFrame=frame;
   pending=updated;
   updated=null;
  }
  pending?.SetResult();
 }
}
