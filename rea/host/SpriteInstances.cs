// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
readonly struct SpriteSample {
 public readonly uint Key, Color, Selector;
 public SpriteSample(uint key,uint color,uint selector)=>(Key,Color,Selector)=(key,color,selector);
 public int Position=>(int)(Key&0x1ffff);
 public int Strip=>(int)((Key>>17)&0x1ff);
 public int Root=>(int)((Key>>26)|((Color>>24)<<6));
 public bool Matches=>(Selector&0x80000000)==0;
 public uint Tile=>Selector>>8;
}

enum FollowMode : byte { All, Tracking, Lost, Missed, Preview }

sealed class SpriteCandidate {
 public readonly HashSet<int> Roots=[];
 public readonly HashSet<uint> Tiles=[];
 public int Left=SpritePriority.Width,Top=SpritePriority.Height,Right=-1,Bottom=-1;
 public int Width=>Right-Left+1;
 public int Height=>Bottom-Top+1;
 public int? FixedCell { get; set; }
 public bool CanFollow { get; set; }=true;
 public bool HardwareRoots { get; set; }=true;
 public double X=>(Left+Right)/2.0;
 public double Y=>(Top+Bottom)/2.0;

 public void Add(SpritePriority.Chain chain) {
  Roots.Add(chain.Root);
  Left=Math.Min(Left,chain.Left);
  Top=Math.Min(Top,chain.Top);
  Right=Math.Max(Right,chain.Right);
  Bottom=Math.Max(Bottom,chain.Bottom);
 }
}

sealed class SpriteMatchFrame(ulong number,SpriteSample[] samples,string? captureId=null,int[]? occurrences=null,bool fixedCells=false) {
 public ulong Number { get; }=number;
 public string? CaptureId { get; }=captureId;
 public int SampleCount=>samples.Length;
 List<SpriteCandidate>? candidates;
 Dictionary<int,SpriteCandidate>? byRoot;
 byte[]? allPacket;

 // The archived raster identifies the winning sprite at each pixel. Use those
 // hits to find the relevant chains, then keep every write in those chains.
 public static SpriteMatchFrame FromCapture(Capture capture,byte[] raster,SpriteTrack track) {
  const uint chainOnly=0x80000000;
  var observed=new SpriteSample[raster.Length/8];
  Span<bool> roots=stackalloc bool[512];
  roots.Clear();
  for(int i=0;i<observed.Length;++i){
   var row=raster.AsSpan(i*8,8);
   uint key=BinaryPrimitives.ReadUInt32LittleEndian(row);
   uint color=BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
   int position=(int)(key&0x1ffff);
   int index=NativePosition(position%SpritePriority.Width,position/SpritePriority.Width);
   var (strip,palette,tile)=Source(capture,index);
   uint selector=strip==((key>>17)&0x1ff)&&track.Matches(strip,palette,tile)?(tile<<8)|palette:chainOnly;
   var sample=new SpriteSample(key,color,selector);
   observed[i]=sample;
   if(sample.Matches)roots[sample.Root]=true;
  }
  var matched=new List<SpriteSample>();
  foreach(var sample in observed)
   if(roots[sample.Root])matched.Add(sample);
  return new SpriteMatchFrame(capture.Frame,matched.ToArray(),capture.Id);
 }

 public static int NativePosition(int x,int y)=>(y+24)*320+x+8;

 public static (uint Strip,uint Palette,uint Tile) Source(Capture capture,int position) {
  uint strip=BinaryPrimitives.ReadUInt16LittleEndian(capture.Objects.AsSpan(position*2));
  uint palette=(uint)(BinaryPrimitives.ReadUInt16LittleEndian(capture.Palette.AsSpan(position*2))>>4)&0xff;
  uint tile=BinaryPrimitives.ReadUInt32LittleEndian(capture.Tiles.AsSpan(position*4));
  return (strip,palette,tile);
 }

 public IReadOnlyList<SpriteCandidate> Candidates {
  get {
   if(candidates is not null)return candidates;
   var chains=new Dictionary<int,SpritePriority.Chain>();
   for(int i=0;i<samples.Length;++i){
    var sample=samples[i];
    int key=Key(i);
    if(!chains.TryGetValue(key,out var chain))
     chains.Add(key,chain=new SpritePriority.Chain(key));
    chain.Add(sample.Strip,sample.Position);
   }
   candidates=[];
   byRoot=[];
   SpritePriority.Chain? previous=null;
   foreach(var chain in chains.Values.OrderBy(chain=>chain.First)){
    if(occurrences is not null||previous is null||!SpritePriority.Neighbors(previous,chain))
     candidates.Add(new SpriteCandidate());
    var candidate=candidates[^1];
    candidate.Add(chain);
    candidate.HardwareRoots=occurrences is null;
    if(fixedCells)candidate.FixedCell=chain.Root;
    if(occurrences is not null&&!fixedCells)candidate.CanFollow=candidate.Width<=16&&candidate.Height<=16;
    byRoot.Add(chain.Root,candidate);
    previous=chain;
   }
   for(int i=0;i<samples.Length;++i)
    if(samples[i].Matches)byRoot[Key(i)].Tiles.Add(samples[i].Tile);
   return candidates;
  }
 }
 int Key(int index)=>occurrences is null?samples[index].Root:occurrences[index];

 public IEnumerable<SpriteCandidate> LayersAt(int x,int y) {
  int position=y*SpritePriority.Width+x;
  var seen=new HashSet<SpriteCandidate>();
  for(int i=samples.Length-1;i>=0;--i){
   if(!samples[i].Matches||samples[i].Position!=position)continue;
   _=Candidates;
   var candidate=byRoot![Key(i)];
   if(seen.Add(candidate))yield return candidate;
  }
 }

 public byte[] Render(FollowMode mode,SpriteCandidate? candidate=null) {
  if(mode==FollowMode.All)return allPacket??=RenderAll();
  if(mode==FollowMode.Missed){
   var packet=(byte[])Render(FollowMode.All).Clone();
   packet[16]=(byte)mode;
   return packet;
  }
  return candidate is null?Packet(Number,mode):RenderBounds(mode,candidate.Left,candidate.Top,candidate.Width,candidate.Height,candidate.Roots);
 }

 public byte[] Preview(SpriteCandidate? candidate,int layer,int count) {
  var packet=Render(FollowMode.Preview,candidate);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packet.Length-4),(ushort)(layer+1));
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packet.Length-2),(ushort)count);
  return packet;
 }

 byte[] RenderAll() {
  if(samples.Length==0)return Packet(Number,FollowMode.All);
  int left=SpritePriority.Width,right=-1,top=SpritePriority.Height,bottom=-1;
  foreach(var sample in samples){
   if(!sample.Matches)continue;
   int x=sample.Position%SpritePriority.Width,y=sample.Position/SpritePriority.Width;
   left=Math.Min(left,x);right=Math.Max(right,x);
   top=Math.Min(top,y);bottom=Math.Max(bottom,y);
  }
  return RenderBounds(FollowMode.All,left,top,right-left+1,bottom-top+1,null);
 }

 byte[] RenderBounds(FollowMode mode,int left,int top,int width,int height,HashSet<int>? roots) {
  var packet=Packet(Number,mode,left,top,width,height);
  // Samples retain hardware order, including pixels hidden by other sprites.
  for(int i=0;i<samples.Length;++i){
   var sample=samples[i];
   if(roots is null?!sample.Matches:!roots.Contains(Key(i)))continue;
   int x=sample.Position%SpritePriority.Width,y=sample.Position/SpritePriority.Width;
   int offset=17+((y-top)*width+x-left)*4;
   uint bgr=sample.Color;
   packet[offset]=(byte)(bgr>>16);
   packet[offset+1]=(byte)(bgr>>8);
   packet[offset+2]=(byte)bgr;
   packet[offset+3]=255;
  }
  return packet;
 }

 public static byte[] Packet(ulong frame,FollowMode mode,int x=0,int y=0,int width=0,int height=0) {
  byte[] packet=new byte[17+width*height*4+(mode==FollowMode.Preview?4:0)];
  BinaryPrimitives.WriteUInt64LittleEndian(packet,frame);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8),(ushort)x);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10),(ushort)y);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12),(ushort)width);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14),(ushort)height);
  packet[16]=(byte)mode;
  return packet;
 }
}

record TrackPoint(ulong Frame,int X,int Y);

sealed class SpriteFollower {
 readonly object sync=new();
 byte[]? latest;
 TaskCompletionSource? updated;
 FollowMode mode;
 SpriteMotion? motion;
 SpriteMotion[]? layers;
 SpriteMatchFrame? previewFrame;
 int layer;
 string? latestCapture;
 TrackPoint? pickedAt,latestPoint;

 public (byte[]? Frame,Task Next) State() {
  lock(sync){
   updated??=new(TaskCreationOptions.RunContinuationsAsynchronously);
   return (latest,updated.Task);
  }
 }

 public (byte[]? Frame,Task Next,string? Capture,TrackPoint? Point) EvidenceState() {
  lock(sync){
   updated??=new(TaskCreationOptions.RunContinuationsAsynchronously);
   return (latest,updated.Task,latestCapture,latestPoint);
  }
 }

 void Send(byte[] packet,string? capture=null) {
  TaskCompletionSource? pending;
  lock(sync){latest=packet;latestCapture=capture;latestPoint=pickedAt;pending=updated;updated=null;}
  pending?.SetResult();
 }

 public void Pick(SpriteMatchFrame[] history,ulong frame,int x,int y) {
  int index=Array.FindIndex(history,image=>image.Number==frame);
  var picked=index<0?null:history[index].LayersAt(x,y).FirstOrDefault();
  if(picked is null){
   if(mode==FollowMode.Tracking)return;
   mode=FollowMode.Missed;
   Send(history.LastOrDefault()?.Render(mode)??SpriteMatchFrame.Packet(frame,mode));
   return;
  }
  layers=null;
  previewFrame=null;
  pickedAt=new(frame,x,y);
  motion=Follow(picked,history,index);
  SendTracking(history[^1]);
 }

 static SpriteMotion Follow(SpriteCandidate picked,SpriteMatchFrame[] history,int index) {
  var motion=new SpriteMotion(picked);
  for(int i=index+1;i<history.Length;++i)motion.Advance(history[i]);
  return motion;
 }

 public void Browse(SpriteMatchFrame[] history,ulong frame,int x,int y,int direction) {
  if(layers is null){
   if(mode is FollowMode.Tracking or FollowMode.Lost)return;
   int index=Array.FindIndex(history,image=>image.Number==frame);
   if(index<0)return;
   var stack=history[index].LayersAt(x,y).ToArray();
   if(stack.Length==0)return;
   layers=stack.Select(candidate=>Follow(candidate,history,index)).ToArray();
   previewFrame=history[index];
   pickedAt=new(frame,x,y);
   layer=0;
  }
  layer=Math.Clamp(layer+direction,0,layers.Length-1);
  Send(previewFrame!.Preview(layers[layer].Initial,layer,layers.Length));
 }

 public void ConfirmPreview(SpriteMatchFrame? frame) {
  if(layers is null||frame is null)return;
  motion=layers[layer];
  layers=null;
  previewFrame=null;
  SendTracking(frame);
 }

 void SendTracking(SpriteMatchFrame frame) {
  mode=motion!.Lost?FollowMode.Lost:FollowMode.Tracking;
  Send(frame.Render(mode,motion.Current),frame.CaptureId);
 }

 public void Clear(SpriteMatchFrame? frame,ulong number) {
  mode=FollowMode.All;
  pickedAt=null;
  motion=null;
  layers=null;
  previewFrame=null;
  Send(frame?.Render(mode)??SpriteMatchFrame.Packet(number,mode),frame?.CaptureId);
 }

 public void Reset(ulong frame) {
  if(mode==FollowMode.Tracking)mode=FollowMode.Lost;
  pickedAt=null;
  motion=null;
  layers=null;
  previewFrame=null;
  Send(SpriteMatchFrame.Packet(frame,mode));
 }

 public void Publish(SpriteMatchFrame frame) {
  if(layers is not null){
   foreach(var choice in layers)choice.Advance(frame);
  }else if(motion is not null){
   motion.Advance(frame);
   SendTracking(frame);
  }else Send(frame.Render(mode),frame.CaptureId);
 }
}

sealed class SpriteMotion(SpriteCandidate initial) {
 const int MaxMisses=6;
 SpriteCandidate last=initial;
 double dx,dy;
 int misses;
 public SpriteCandidate Initial { get; }=initial;
 public SpriteCandidate? Current { get; private set; }=initial.CanFollow?initial:null;
 public bool Lost { get; private set; }=!initial.CanFollow;

 public void Advance(SpriteMatchFrame frame) {
  if(Lost)return;
  int elapsed=misses+1;
  double x=last.X+dx*elapsed,y=last.Y+dy*elapsed;
  double radius=Math.Max(12,Math.Max(last.Width,last.Height)/3.0)+2*misses;
  SpriteCandidate? best=null;
  double bestCost=double.PositiveInfinity,secondCost=double.PositiveInfinity;
  foreach(var candidate in frame.Candidates){
   if(!candidate.CanFollow||last.FixedCell!=candidate.FixedCell)continue;
   double distance=Math.Sqrt(Math.Pow(candidate.X-x,2)+Math.Pow(candidate.Y-y,2));
   if(distance>radius)continue;
   double shape=Math.Abs(candidate.Width-last.Width)+Math.Abs(candidate.Height-last.Height);
   double cost=distance+shape*0.15;
   if(candidate.Tiles.Overlaps(last.Tiles))cost-=3;
   // Stable roots can distinguish close overlaps, while motion can match new roots.
   if(candidate.HardwareRoots&&distance<4&&candidate.Roots.SetEquals(last.Roots))cost-=5;
   if(cost<bestCost){secondCost=bestCost;bestCost=cost;best=candidate;}
   else secondCost=Math.Min(secondCost,cost);
  }
  if(best is null){
   if(++misses>MaxMisses)Lost=true;
   Current=null;
   return;
  }
  // A merge or two equally plausible matches can hide a change of identity.
  if(secondCost-bestCost<4||best.Width>last.Width*1.7+8||best.Height>last.Height*1.7+8){
   Lost=true;
   Current=null;
   return;
  }
  dx=Math.Clamp((best.X-last.X)/elapsed,-12,12);
  dy=Math.Clamp((best.Y-last.Y)/elapsed,-12,12);
  misses=0;
  Current=last=best;
 }
}
