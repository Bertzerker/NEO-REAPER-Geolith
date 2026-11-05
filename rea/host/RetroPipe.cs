// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.IO.Pipes;

static class RetroPipe {
 const uint Signature=0x3150524e; // NRP1

 public static async Task Listen(Machine machine,string name){
  while(true){
   using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,65536,512*1024);
   await pipe.WaitForConnectionAsync();
   bool attached=false;
   try{
    var header=new byte[20];
    await pipe.ReadExactlyAsync(header.AsMemory(0,4));
    if(BinaryPrimitives.ReadUInt32LittleEndian(header)!=Signature)continue;
    int stateSize=(int)await machine.OnOwner(()=>machine.DesktopAttach());
    attached=true;
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4),stateSize);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8),machine.SampleRate);
    BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(12),machine.FrameRate);
    await pipe.WriteAsync(header);
    var request=new byte[4];
    while(await pipe.ReadAsync(request.AsMemory(0,1))!=0){
     switch(request[0]){
      case (byte)'F':
       await pipe.ReadExactlyAsync(request);
       uint p0=BinaryPrimitives.ReadUInt16LittleEndian(request),p1=BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(2));
       if(p0>1023||p1>1023)throw new InvalidDataException("Invalid controller input.");
       var (video,audio)=((byte[],byte[]))await machine.OnOwner(()=>machine.DesktopStep(p0,p1));
       BinaryPrimitives.WriteInt32LittleEndian(request,audio.Length);
       await pipe.WriteAsync(request);
       await pipe.WriteAsync(video);
       await pipe.WriteAsync(audio);
       break;
      case (byte)'S':
       await pipe.WriteAsync((byte[])await machine.OnOwner(()=>machine.DesktopSave()));
       break;
      case (byte)'L':
       var state=new byte[stateSize];
       await pipe.ReadExactlyAsync(state);
       request[0]=(bool)await machine.OnOwner(()=>machine.DesktopLoad(state))?(byte)1:(byte)0;
       await pipe.WriteAsync(request.AsMemory(0,1));
       break;
      case (byte)'R':
       await machine.OnOwner(()=>{machine.DesktopReset();return true;});
       await pipe.WriteAsync(new byte[]{1});
       break;
      default:throw new InvalidDataException("Unknown RetroArch command.");
     }
    }
   }catch(EndOfStreamException){}catch(IOException e){Console.Error.WriteLine(e.Message);}
    catch(ApiError e){Console.Error.WriteLine(e.Message);}
   finally{
    if(attached)await machine.OnOwner(()=>{machine.DesktopDetach();return true;});
   }
  }
 }
}
