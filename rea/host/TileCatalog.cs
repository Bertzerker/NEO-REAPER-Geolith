// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text.Json;

record CatalogEntry(string Id,string Name,string Kind,string Source,string File,string Revision,JsonElement Selectors,string Evidence,string? Notes,string[] Labels);
record TileSelector(uint Tile,uint? Palette,uint? Bank);
record TileSample(int X,int Y,string Source,uint Tile,uint Palette,uint Bank,int? Strip,int? CellX,int? CellY,int? VramWord);

sealed class TileCatalog(string source,TileSelector[] selectors) {
 public const string SpriteEvidence="Winning sprite sample before FIX. Covered strip writes have no archived tile attribution.";
 public const string FixEvidence="Captured FIX tile pixels. The raster records the resolved index and palette bank, with no graphics ROM source bit.";
 public string Evidence=>source=="fix"?FixEvidence:SpriteEvidence;

 public static TileSample? Sample(Capture capture,int view,int x,int y) {
  int position=SpriteMatchFrame.NativePosition(x,y);
  int layer=view==0?capture.Winner[position]:view;
  if(layer is not (1 or 2)||capture.Planes[layer][position*4+3]==0)return null;
  var (tile,palette,bank)=SourceAt(capture,layer,position);
  if(tile==uint.MaxValue)return null;
  int cellX=(x+8)/8,cellY=FixRow(y);
  return layer==2
   ?new(x,y,"fix",tile,palette,bank,null,cellX,cellY,0x7000+cellX*32+cellY)
   :new(x,y,"sprite",tile,palette,bank,BinaryPrimitives.ReadUInt16LittleEndian(capture.Objects.AsSpan(position*2)),null,null,null);
 }
 // The rendered FIX map starts at native scanline 8, above the crop at 24.
 static int FixRow(int y)=>(y+24-8)/8;

 static (uint Tile,uint Palette,uint Bank) SourceAt(Capture capture,int layer,int position) {
  int address=(layer-1)*320*264+position;
  uint tile=BinaryPrimitives.ReadUInt32LittleEndian(capture.Tiles.AsSpan(address*4));
  uint palette=BinaryPrimitives.ReadUInt16LittleEndian(capture.Palette.AsSpan(address*2));
  return (tile,(palette>>4)&0xff,palette>>12);
 }

 public SpriteMatchFrame Match(Capture capture) {
  int layer=source=="fix"?2:1;
  var pixels=new List<SpriteSample>();
  var occurrences=new List<int>();
  var runs=new Dictionary<(int Strip,uint Tile),(int Y,int Occurrence)>();
  int nextOccurrence=0;
  for(int y=0;y<SpritePriority.Height;++y)
   for(int x=0;x<SpritePriority.Width;++x){
    int position=SpriteMatchFrame.NativePosition(x,y);
    if(capture.Planes[layer][position*4+3]==0)continue;
    var (tile,palette,bank)=SourceAt(capture,layer,position);
    bool matches=false;
    foreach(var selector in selectors){
     if(selector.Tile!=tile||selector.Palette.HasValue&&selector.Palette!=palette||
        selector.Bank.HasValue&&selector.Bank!=bank)continue;
     matches=true;
     break;
    }
    if(!matches)continue;
    int occurrence;
    if(layer==2)occurrence=FixRow(y)*40+(x+8)/8;
    else{
     int strip=BinaryPrimitives.ReadUInt16LittleEndian(capture.Objects.AsSpan(position*2));
     if(strip is <1 or >SpritePriority.LastStrip)continue;
     var key=(strip,tile);
     // The raster has no sprite tile row ID. Split only clearly separated runs.
     if(!runs.TryGetValue(key,out var run)||y-run.Y>=16)run=(y,nextOccurrence++);
     runs[key]=(y,run.Occurrence);
     occurrence=run.Occurrence;
    }
    uint color=BinaryPrimitives.ReadUInt32LittleEndian(capture.Planes[layer].AsSpan(position*4))&0x00ffffff;
    pixels.Add(new((uint)(y*SpritePriority.Width+x),color,(tile<<8)|palette));
    occurrences.Add(occurrence);
   }
  return new SpriteMatchFrame(capture.Frame,pixels.ToArray(),capture.Id,occurrences.ToArray(),layer==2);
 }
}
