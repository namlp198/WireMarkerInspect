using System.Runtime.InteropServices;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Infrastructure;

/// <summary>C ABI adapter matched against NVision/NAcquire/wrappers/c_api/NAcquireC.h.</summary>
public sealed class NAcquireCamera : ICamera
{
    private readonly object gate=new();
    private IntPtr camera;
    private bool initialized;
    private bool grabbing;
    private CameraDevice? device;
    private uint? lastFrame;
    public IReadOnlyList<CameraDevice> Enumerate()
    {
        lock(gate)
        {
            if(!initialized)
            {
                Native.NAcquire_GetVersion(out var major,out var minor,out _);
                if(major!=0 || minor!=1)throw new NotSupportedException($"NAcquire ABI {major}.{minor} requires validation.");
                Check(Native.NAcquire_Initialize()); initialized=true;
            }
            Check(Native.NAcquire_EnumerateDevices(null,0,out var count));
            if(count<0 || count>256)throw new InvalidDataException("Unexpected camera count.");
            if(count==0)return [];
            var items=new Native.Device[count];
            Check(Native.NAcquire_EnumerateDevices(items,count,out var actual));
            if(actual>count)throw new InvalidDataException("Camera list changed; enumerate again.");
            return items.Take(actual).Select(d=>new CameraDevice(d.Id,$"{d.Model} · {d.Serial}",d.Backend,
                d.Backend.Contains("opencv",StringComparison.OrdinalIgnoreCase) || d.Backend.Contains("synthetic",StringComparison.OrdinalIgnoreCase))).ToArray();
        }
    }
    public void Open(CameraDevice selected)
    {
        lock(gate)
        {
            Close();
            if(!initialized)_=Enumerate();
            camera=Native.NAcquire_OpenCamera(selected.Id);
            if(camera==IntPtr.Zero)throw new InvalidOperationException("Cannot open selected camera.");
            device=selected;lastFrame=null;
        }
    }
    public void SetParameter(string name,string value) {lock(gate){RequireOpen();Check(Native.NAcquire_SetParameter(camera,name,value));}}
    public void Start() {lock(gate){RequireOpen();Check(Native.NAcquire_StartGrabbing(camera));grabbing=true;}}
    public ImageFrame Grab(int timeoutMs)
    {
        lock(gate)
        {
            RequireOpen();
            if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
            Check(Native.NAcquire_GetFrame(camera,out var frame,Math.Clamp(timeoutMs,1,2000)));
            try
            {
                Check(Native.NAcquire_Frame_GetInfo(frame,out var info));
                Check(Native.NAcquire_Frame_GetData(frame,out var data,out var size));
                if(info.Status!=0 || data==IntPtr.Zero || info.Width==0 || info.Height==0)throw new InvalidDataException("Incomplete camera frame.");
                int w=checked((int)info.Width),h=checked((int)info.Height),stride=checked((int)info.Stride),length=checked((int)size);
                int channels=info.Format==1?1:info.Format is 5 or 6?3:throw new NotSupportedException("Use Mono8, RGB8 or BGR8. Bayer/12-bit conversion needs the validated vendor adapter.");
                if(w>30000 || h>30000 || stride<checked(w*channels) || length<(long)stride*h || info.Size<size)
                    throw new InvalidDataException("Invalid camera frame layout.");
                if(lastFrame==info.FrameId)throw new InvalidDataException("Repeated camera frame ID.");
                var raw=new byte[length];Marshal.Copy(data,raw,0,length);
                var bgr=new byte[checked(w*h*3)];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    int src=y*stride+x*channels,dst=(y*w+x)*3;
                    bgr[dst]=raw[src+(channels==1?0:info.Format==5?2:0)];
                    bgr[dst+1]=raw[src+(channels==1?0:1)];
                    bgr[dst+2]=raw[src+(channels==1?0:info.Format==5?0:2)];
                }
                lastFrame=info.FrameId;
                return new(w,h,w*3,bgr,Guid.NewGuid(),DateTimeOffset.UtcNow,device!.IsSimulation?"SIMULATION":device.Name);
            }
            finally {Native.NAcquire_ReleaseFrame(camera,frame);}
        }
    }
    public void Stop() {lock(gate){if(camera!=IntPtr.Zero&&grabbing){Check(Native.NAcquire_StopGrabbing(camera));grabbing=false;}}}
    public void Close()
    {
        lock(gate)
        {
            if(camera==IntPtr.Zero)return;
            try {if(grabbing)Native.NAcquire_StopGrabbing(camera);}
            finally {Native.NAcquire_CloseCamera(camera);camera=IntPtr.Zero;grabbing=false;lastFrame=null;}
        }
    }
    public void Dispose() {lock(gate){Close();if(initialized){Native.NAcquire_Shutdown();initialized=false;}}}
    private void RequireOpen(){if(camera==IntPtr.Zero)throw new InvalidOperationException("Camera is disconnected.");}
    private static void Check(int code){if(code==5)throw new TimeoutException("Camera frame timeout.");if(code!=0)throw new InvalidOperationException($"NAcquire status {code}.");}
    private static class Native
    {
        private const string Dll="NAcquireCAPI";
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)]
        internal struct Device
        {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Id;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Vendor;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Model;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Serial;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Backend;
            public int Transport;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=40)]public string Ip;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=20)]public string Mac;
            public int Major,Minor;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Info {public int Format;public nuint Width,Height,Stride,Size;public ulong Timestamp;public uint FrameId;public int Status;}
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void NAcquire_GetVersion(out int major,out int minor,out int patch);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_Initialize();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern void NAcquire_Shutdown();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_EnumerateDevices([Out]Device[]? devices,int count,out int actual);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern IntPtr NAcquire_OpenCamera([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_CloseCamera(IntPtr handle);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_StartGrabbing(IntPtr handle);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_StopGrabbing(IntPtr handle);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_SetParameter(IntPtr handle,string name,string value);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_GetFrame(IntPtr handle,out IntPtr frame,int timeout);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_Frame_GetInfo(IntPtr frame,out Info info);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_Frame_GetData(IntPtr frame,out IntPtr data,out nuint size);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int NAcquire_ReleaseFrame(IntPtr handle,IntPtr frame);
    }
}
