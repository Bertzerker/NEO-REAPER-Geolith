// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text.Json;

record TrackedBounds(int X,int Y,int Width,int Height);
record CatalogAttachment(string Id,string Capture,ulong Frame,int Generation,string Identity,CatalogEntry Entry,
 string Selection,TrackedBounds Bounds,TrackPoint? PickedAt,string PacketSha256,string? Movie,
 string? MovieVersion,InputRow? MovieInput,string? InputRowSha256,TileSample? Sample);
record CatalogEvidence(CatalogAttachment Attachment,byte[] Packet) {
 public static string MoviePath(string root,string version)=>Path.Combine(root,"movie-versions",Data.Id(version)+".json");
 public static string SaveMovieVersion(string root,Movie movie){
  string version=Data.Hash(JsonSerializer.SerializeToUtf8Bytes(movie,Data.Json));
  string path=MoviePath(root,version);
  if(!File.Exists(path))Data.Save(path,movie);
  return version;
 }

 public static CatalogEvidence Pin(Machine machine,CatalogEntry entry,TrackedPacket tracked){
  var c=machine.Get(tracked.Capture);
  var packet=tracked.Packet;
  var mode=(FollowMode)packet[16];
  var bounds=new TrackedBounds(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)),
   BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10)),BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12)),
   BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(14)));
  if(mode is not (FollowMode.All or FollowMode.Tracking)||bounds.Width==0)
   throw new ApiError(409,"Attach a visible match or a followed instance.");
  if(BinaryPrimitives.ReadUInt64LittleEndian(packet)!=c.Frame)
   throw new ApiError(409,"Tracked packet and capture disagree.");
  string? version=c.MovieVersion;
  if(c.Movie is {} movie&&version is null){
   string path=Path.Combine(machine.Root,"movies",Data.Id(movie)+".json");
   if(!File.Exists(path))throw new ApiError(409,"Save the movie before attaching this frame.");
   var recording=Data.Read<Movie>(path);
   if(c.MovieInput is not {} input||input.Row>=recording.Rows.Count||recording.Rows[input.Row]!=input)
    throw new ApiError(409,"The saved movie has no matching input row for this frame.");
   version=SaveMovieVersion(machine.Root,recording);
  }
  string? inputHash=c.MovieInput is {} row?Data.Hash(JsonSerializer.SerializeToUtf8Bytes(row,Data.Json)):null;
  TileSample? sample=null;
  if(entry.Kind=="tile"){
   for(int offset=17;offset<packet.Length;offset+=4){
    if(packet[offset+3]==0)continue;
    int pixel=(offset-17)/4;
    sample=TileCatalog.Sample(c,entry.Source=="fix"?2:1,bounds.X+pixel%bounds.Width,bounds.Y+pixel/bounds.Width);
    break;
   }
  }
  var attachment=new CatalogAttachment(Data.NewId(),c.Id,c.Frame,c.Generation,c.Identity,entry,
   mode==FollowMode.Tracking?"instance":"all",bounds,tracked.Point,Data.Hash(packet),c.Movie,version,c.MovieInput,inputHash,sample);
  Data.Archive(machine.Root,c);
  var evidence=new CatalogEvidence(attachment,packet);
  Data.Save(Path.Combine(machine.Root,"tracked",attachment.Id+".json"),evidence);
  return evidence;
 }

 public static CatalogEvidence Read(string root,string id){
  var evidence=Data.Read<CatalogEvidence>(Path.Combine(root,"tracked",Data.Id(id)+".json"));
  if(evidence.Attachment.Id!=id||Data.Hash(evidence.Packet)!=evidence.Attachment.PacketSha256)
   throw new ApiError(409,"The tracked frame attachment failed its integrity check.");
  return evidence;
 }
}

record TrackedPacket(string Capture,byte[] Packet,TrackPoint? Point);

sealed class TrackedStream(CatalogEntry entry) {
 const int Limit=8;
 public CatalogEntry Entry { get; }=entry;
 readonly object sync=new();
 readonly Queue<(uint Sequence,TrackedPacket Packet)> recent=new();
 uint sequence;

 public byte[] Wrap(byte[] packet,string? capture,TrackPoint? point) {
  uint number=++sequence;
  if(capture is not null){
   lock(sync){
    recent.Enqueue((number,new(capture,packet,point)));
    if(recent.Count>Limit)recent.Dequeue();
   }
  }
  var wire=new byte[4+packet.Length];
  BinaryPrimitives.WriteUInt32LittleEndian(wire,number);
  packet.CopyTo(wire,4);
  return wire;
 }

 public TrackedPacket Get(uint number){
  lock(sync){
   foreach(var frame in recent)
    if(frame.Sequence==number)return frame.Packet;
  }
  throw new ApiError(410,"That tracked frame has expired. Attach a current frame.");
 }
}
