// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Buffers.Binary;
static class Native {
 const string D="neo-reaper-core";
 [DllImport(D)] public static extern int nr_open(byte[] rom,nuint n,byte[] bios,nuint b,int system,int region);
 [DllImport(D)] public static extern IntPtr nr_error();
 [DllImport(D)] public static extern ulong nr_step(uint p0,uint p1);
 [DllImport(D)] public static extern void nr_reset();
 [DllImport(D)] public static extern IntPtr nr_video(int plane);
 [DllImport(D)] public static extern IntPtr nr_evidence(int kind);
 [DllImport(D)] static extern IntPtr nr_sprite_samples(out nuint length);
 [DllImport(D)] static extern IntPtr nr_follow_frame(uint[] rules,nuint count,out uint x,out uint y,out uint width,out uint height);
 [DllImport(D)] static extern IntPtr nr_follow_samples(uint[] rules,nuint count,out nuint length);
 [DllImport(D)] public static extern IntPtr nr_memory(int domain,out nuint length);
 [DllImport(D)] public static extern IntPtr nr_state(out nuint length);
 [DllImport(D)] public static extern int nr_restore(byte[] bytes,nuint n,ulong frame);
 [DllImport(D)] public static extern uint nr_register(uint r);
 [DllImport(D)] public static extern IntPtr nr_audio(out uint count);
 public static byte[] Audio(){var p=nr_audio(out uint n);return Copy(p,(int)n*2);}
 public static byte[] Copy(IntPtr p,int n){var b=new byte[n];Marshal.Copy(p,b,0,n);return b;}
 public static byte[] State(){var p=nr_state(out var n);return Copy(p,checked((int)n));}
 public static byte[]? SpriteSamples() {
  var samples=nr_sprite_samples(out var length);
  if(samples==IntPtr.Zero)return null;
  return length==0 ? [] : Copy(samples,checked((int)length));
 }
 public static byte[] FollowFrame(ulong frame,uint[] rules) {
  var pixels=nr_follow_frame(rules,(nuint)(rules.Length/5),out uint x,out uint y,out uint width,out uint height);
  int length=checked((int)(width*height*4));
  // LE native frame number and bounds, followed by tightly packed RGBA8.
  byte[] packet=new byte[16+length];
  BinaryPrimitives.WriteUInt64LittleEndian(packet,frame);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8),(ushort)x);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10),(ushort)y);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12),(ushort)width);
  BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14),(ushort)height);
  if(length!=0)Marshal.Copy(pixels,packet,16,length);
  return packet;
 }
 public static unsafe SpriteSample[] FollowSamples(uint[] rules){
  var samples=nr_follow_samples(rules,(nuint)(rules.Length/5),out var length);
  return new ReadOnlySpan<SpriteSample>((void*)samples,checked((int)length)).ToArray();
 }
}
