// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
static class Native {
 const string D="neo-reaper-core";
 [DllImport(D)] public static extern int nr_open(byte[] rom,nuint n,byte[] bios,nuint b,int system,int region);
 [DllImport(D)] public static extern IntPtr nr_error();
 [DllImport(D)] public static extern ulong nr_step(uint p0,uint p1);
 [DllImport(D)] public static extern IntPtr nr_video(int plane);
 [DllImport(D)] public static extern IntPtr nr_evidence(int kind);
 [DllImport(D)] public static extern IntPtr nr_memory(int domain,out nuint length);
 [DllImport(D)] public static extern IntPtr nr_state(out nuint length);
 [DllImport(D)] public static extern int nr_restore(byte[] bytes,nuint n,ulong frame);
 [DllImport(D)] public static extern uint nr_register(uint r);
 [DllImport(D)] public static extern IntPtr nr_audio(out uint count);
 public static byte[] Audio(){var p=nr_audio(out uint n);return Copy(p,(int)n*2);}
 public static byte[] Copy(IntPtr p,int n){var b=new byte[n];Marshal.Copy(p,b,0,n);return b;}
 public static byte[] State(){var p=nr_state(out var n);return Copy(p,checked((int)n));}
}
