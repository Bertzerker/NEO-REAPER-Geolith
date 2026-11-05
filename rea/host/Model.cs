// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
record Capture(string Id,ulong Frame,int Generation,string Identity,long Time,byte[][] Planes,byte[][] Memory,byte[] Winner,byte[] Palette,byte[] Objects,byte[] Tiles,uint[] Registers,string StateHash,string? Movie) {
 public object Meta()=>new {id=Id,frame=Frame,generation=Generation,identity=Identity,time=Time,width=304,height=224,nativeWidth=320,nativeHeight=264,pixelFormat="BGRA8 arrays; composite alpha ignored, PNG opaque",crop=new{x=8,y=24},valid=true,scope="One normal Geolith render; memory copied after geo_exec",stateHash=StateHash,movie=Movie,views=new[]{new{id=0,name="Composite",evidence="Captured output"},new{id=1,name="Sprite plane",evidence="Captured input: last admitted sprite sample before FIX"},new{id=2,name="FIX/text",evidence="Captured input"}}};
}
record Checkpoint(string Id,string Name,string Identity,ulong Frame,string State,uint P0,uint P1);
record InputRow(int Row,uint P0,uint P1,string OutputHash,string StateHash,string AudioHash);
record Movie(string Id,string Name,string Identity,ulong Frame,string Start,List<InputRow> Rows);
record Note(string Id,int Revision,string Title,string Category,string Markdown,string Json,string Mermaid,string Capture,string? Movie,long Updated);
static class Data {
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
 public static string NewId()=>Guid.NewGuid().ToString("N");
 public static string Id(string s){if(s.Length<1||s.Length>96||s.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))throw new ApiError(400,"Invalid identifier");return s;}
 public static void Save<T>(string path,T obj){Directory.CreateDirectory(Path.GetDirectoryName(path)!);string tmp=path+"."+NewId()+".tmp";File.WriteAllBytes(tmp,JsonSerializer.SerializeToUtf8Bytes(obj,Json));File.Move(tmp,path,true);}
 public static T Read<T>(string p)=>JsonSerializer.Deserialize<T>(File.ReadAllBytes(p),Json)??throw new ApiError(404,"Missing saved item");
 public static void Archive(string dir,Capture c){string p=Path.Combine(dir,"evidence",Id(c.Id)+".json.gz");if(File.Exists(p))return;Directory.CreateDirectory(Path.GetDirectoryName(p)!);using var f=File.Create(p);using var gz=new GZipStream(f,CompressionLevel.Fastest);JsonSerializer.Serialize(gz,c,Json);}
 public static Capture Archived(string dir,string id){string p=Path.Combine(dir,"evidence",Id(id)+".json.gz");if(!File.Exists(p))throw new ApiError(410,"Capture expired or unavailable");using var f=File.OpenRead(p);using var gz=new GZipStream(f,CompressionMode.Decompress);return JsonSerializer.Deserialize<Capture>(gz,Json)!;}
 public static byte[] Crop(byte[] b){byte[] o=new byte[304*224*4];for(int y=0;y<224;y++)Buffer.BlockCopy(b,((y+24)*320+8)*4,o,y*304*4,304*4);return o;}
 public static byte[] Png(byte[] bgra,int w,int h,bool opaque=false){
  using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
  void Chunk(string type,byte[] bytes){var t=System.Text.Encoding.ASCII.GetBytes(type);Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(n,bytes.Length);output.Write(n);output.Write(t);output.Write(bytes);uint crc=0xffffffff;foreach(byte x in t.Concat(bytes)){crc^=x;for(int j=0;j<8;j++)crc=(crc>>1)^((crc&1)!=0?0xedb88320:0);}BinaryPrimitives.WriteUInt32BigEndian(n,~crc);output.Write(n);}
  byte[] head=new byte[13];BinaryPrimitives.WriteInt32BigEndian(head,w);BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(4),h);head[8]=8;head[9]=6;Chunk("IHDR",head);
  using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Fastest,true)){for(int y=0;y<h;y++){z.WriteByte(0);for(int x=0;x<w;x++){int p=(y*w+x)*4;z.WriteByte(bgra[p+2]);z.WriteByte(bgra[p+1]);z.WriteByte(bgra[p]);z.WriteByte(opaque?(byte)255:bgra[p+3]);}}}Chunk("IDAT",packed.ToArray());Chunk("IEND",[]);return output.ToArray();
 }
 public static byte[] Atlas(Capture c){byte[] b=new byte[304*224*4*3];for(int i=0;i<3;i++){var p=Crop(c.Planes[i]);if(i==0)for(int j=3;j<p.Length;j+=4)p[j]=255;Buffer.BlockCopy(p,0,b,i*p.Length,p.Length);}return Png(b,304,224*3);}
}
class ApiError(int status,string message):Exception(message){public int Status=status;}
