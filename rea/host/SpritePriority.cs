// SPDX-License-Identifier: MIT
using System.Buffers.Binary;

static class SpritePriority {
 public const int FirstStrip=1, LastStrip=381, Width=304, Height=224;
 const int SampleSize=8, PositionBits=17, PositionMask=(1<<PositionBits)-1, StripMask=0x1ff;

 // Older captures store literal opaque alpha. Chain samples use three bits of that byte.
 public static bool HasChains(byte[]? samples)=>samples is {Length:>0}&&samples[7]<=7;
 static int Strip(uint key)=>(int)((key>>PositionBits)&StripMask);
 static int Root(uint key,byte highBits)=>(int)(key>>26)|(highBits<<6);

 static (int Strip,int Root)? PickChain(byte[] samples,int x,int y) {
  int position=y*Width+x;
  for(int offset=samples.Length-SampleSize;offset>=0;offset-=SampleSize) {
   uint key=BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(offset));
   if((key&PositionMask)==position)return (Strip(key),Root(key,samples[offset+7]));
  }
  return null;
 }

 public static IEnumerable<int> Strips(byte[] samples,HashSet<int>? roots=null,int? position=null) {
  var present=new bool[LastStrip+1];
  for(int offset=0;offset<samples.Length;offset+=SampleSize) {
   uint key=BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(offset));
   if(position.HasValue&&(key&PositionMask)!=position.Value)continue;
   if(roots is null||roots.Contains(Root(key,samples[offset+7])))present[Strip(key)]=true;
  }
  for(int strip=FirstStrip;strip<=LastStrip;++strip)
   if(present[strip])yield return strip;
 }

 public static byte[] Render(byte[] samples,int first,int last,HashSet<int>? roots=null) {
  var pixels=new byte[Width*Height*4];
  // Replay only selected writes. The last surviving write at each pixel wins.
  for(int offset=0;offset<samples.Length;offset+=SampleSize) {
   uint key=BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(offset));
   int strip=Strip(key);
   if(strip<first||strip>last||(roots is not null&&!roots.Contains(Root(key,samples[offset+7]))))continue;
   int pixel=(int)(key&PositionMask)*4;
   samples.AsSpan(offset+4,3).CopyTo(pixels.AsSpan(pixel,3));
   pixels[pixel+3]=255;
  }
  return pixels;
 }

 public record Selection(int Strip,int[] Strips,HashSet<int> Roots);

 public static Selection? PickGroup(byte[] samples,int x,int y) {
  if(PickChain(samples,x,y) is not { } picked)return null;
  var groups=new SortedDictionary<int,Chain>();
  for(int offset=0;offset<samples.Length;offset+=SampleSize) {
   uint key=BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(offset));
   int root=Root(key,samples[offset+7]);
   if(!groups.TryGetValue(root,out var chain))groups.Add(root,chain=new Chain(root));
   chain.Add(Strip(key),(int)(key&PositionMask));
  }

  var ordered=groups.Values.ToArray();
  int first=Array.IndexOf(ordered,groups[picked.Root]),last=first;
  while(first>0&&Neighbors(ordered[first-1],ordered[first]))--first;
  while(last+1<ordered.Length&&Neighbors(ordered[last],ordered[last+1]))++last;
  var roots=ordered[first..(last+1)].Select(chain=>chain.Root).ToHashSet();
  return new Selection(picked.Strip,Strips(samples,roots).ToArray(),roots);
 }

 internal sealed class Chain(int root) {
  public int Root { get; }=root;
  public int First=LastStrip,Last=FirstStrip,Left=Width,Right=-1,Top=Height,Bottom=-1;
  public readonly HashSet<int> Pixels=[];

  public void Add(int strip,int position) {
   First=Math.Min(First,strip);
   Last=Math.Max(Last,strip);
   if(!Pixels.Add(position))return;
   int x=position%Width,y=position/Width;
   Left=Math.Min(Left,x);
   Right=Math.Max(Right,x);
   Top=Math.Min(Top,y);
   Bottom=Math.Max(Bottom,y);
  }
 }

 internal static bool Neighbors(Chain a,Chain b) {
  if(a.Last+1!=b.First)return false;
  // Prefer seams between parts. Deep overlap in both axes suggests separate objects.
  int overlapX=Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left)+1;
  int overlapY=Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top)+1;
  if(overlapX<-1||overlapY<-1)return false;
  if(overlapX*2>=Math.Min(a.Right-a.Left+1,b.Right-b.Left+1)&&
     overlapY*2>=Math.Min(a.Bottom-a.Top+1,b.Bottom-b.Top+1))return false;
  if(a.Pixels.Count>b.Pixels.Count)(a,b)=(b,a);
  foreach(int position in a.Pixels) {
   int x=position%Width;
   for(int dy=-1;dy<=1;++dy)
    for(int dx=Math.Max(-1,-x);dx<=Math.Min(1,Width-1-x);++dx)
     if(b.Pixels.Contains(position+dy*Width+dx))return true;
  }
  return false;
 }
}
