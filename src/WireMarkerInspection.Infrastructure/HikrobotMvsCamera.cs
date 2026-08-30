using System.Runtime.InteropServices;
using System.Text;
using WireMarkerInspection.Application;
using WireMarkerInspection.Domain;
#if HIKROBOT_MVS
using MvCamCtrl.NET;
#endif

namespace WireMarkerInspection.Infrastructure;

/// <summary>Hikrobot MVS SDK adapter for GigE Vision and USB3 Vision cameras.</summary>
public sealed class HikrobotMvsCamera : ICamera
{
#if HIKROBOT_MVS
    private readonly object gate=new();
    private readonly Dictionary<string,MyCamera.MV_CC_DEVICE_INFO> devices=[];
    private MyCamera? camera;
    private CameraDevice? selected;
    private bool initialized;
    private bool grabbing;
    private uint? lastFrame;

    public IReadOnlyList<CameraDevice> Enumerate()
    {
        lock(gate)
        {
            EnsureInitialized();
            var list=new MyCamera.MV_CC_DEVICE_INFO_LIST();
            Check(MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE|MyCamera.MV_USB_DEVICE,ref list),"enumerate cameras");
            if(list.nDeviceNum>256)throw new InvalidDataException("MVS returned an unexpected camera count.");
            devices.Clear();
            var result=new List<CameraDevice>((int)list.nDeviceNum);
            for(var index=0;index<list.nDeviceNum;index++)
            {
                var info=Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(list.pDeviceInfo[index]);
                var description=Describe(info,index);
                var id=$"mvs:{description.Transport}:{description.Serial}";
                if(devices.ContainsKey(id))id+=$":{index}";
                devices[id]=info;
                result.Add(new(id,description.DisplayName,$"hikrobot-mvs-{description.Transport}",false));
            }
            return result;
        }
    }

    public void Open(CameraDevice device)
    {
        lock(gate)
        {
            Close();
            EnsureInitialized();
            if(!devices.TryGetValue(device.Id,out var info))
            {
                _=Enumerate();
                if(!devices.TryGetValue(device.Id,out info))throw new InvalidOperationException("Camera không còn trong danh sách MVS. Hãy Scan lại.");
            }
            camera=new MyCamera();
            try
            {
                Check(camera.MV_CC_CreateDevice_NET(ref info),"create camera handle");
                Check(camera.MV_CC_OpenDevice_NET(),"open camera");
                if(info.nTLayerType==MyCamera.MV_GIGE_DEVICE)
                {
                    var packetSize=camera.MV_CC_GetOptimalPacketSize_NET();
                    if(packetSize>0)Check(camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize",packetSize),"set GigE packet size");
                }
                Check(camera.MV_CC_SetEnumValue_NET("AcquisitionMode",(uint)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS),"set continuous acquisition");
                Check(camera.MV_CC_SetEnumValue_NET("TriggerMode",(uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF),"disable trigger mode");
                selected=device;lastFrame=null;
            }
            catch
            {
                try{camera.MV_CC_CloseDevice_NET();}catch{/* Preserve the original MVS error. */}
                try{camera.MV_CC_DestroyDevice_NET();}catch{/* Preserve the original MVS error. */}
                camera=null;selected=null;
                throw;
            }
        }
    }

    public void SetParameter(string name,string value)
    {
        lock(gate)
        {
            RequireOpen();
            if(!float.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)||!float.IsFinite(number))
                throw new ArgumentException($"Invalid MVS parameter value: {value}",nameof(value));
            switch(name)
            {
                case "ExposureTime":
                    Check(camera!.MV_CC_SetEnumValue_NET("ExposureAuto",0),"disable auto exposure");
                    Check(camera.MV_CC_SetFloatValue_NET(name,number),"set exposure time");
                    break;
                case "Gain":
                    Check(camera!.MV_CC_SetEnumValue_NET("GainAuto",0),"disable auto gain");
                    Check(camera.MV_CC_SetFloatValue_NET(name,number),"set gain");
                    break;
                default: throw new NotSupportedException($"Unsupported MVS parameter: {name}");
            }
        }
    }

    public void Start()
    {
        lock(gate)
        {
            RequireOpen();
            if(grabbing)return;
            Check(camera!.MV_CC_StartGrabbing_NET(),"start acquisition");
            grabbing=true;lastFrame=null;
        }
    }

    public ImageFrame Grab(int timeoutMs)
    {
        lock(gate)
        {
            RequireOpen();
            if(!grabbing)throw new InvalidOperationException("Acquisition has not started.");
            var frame=new MyCamera.MV_FRAME_OUT();
            var activeCamera=camera!;
            var status=activeCamera.MV_CC_GetImageBuffer_NET(ref frame,Math.Clamp(timeoutMs,1,5000));
            if(status==MyCamera.MV_E_NODATA)throw new TimeoutException("Camera frame timeout.");
            Check(status,"get image buffer");
            try
            {
                var info=frame.stFrameInfo;
                if(frame.pBufAddr==IntPtr.Zero||info.nWidth==0||info.nHeight==0||info.nFrameLen==0)
                    throw new InvalidDataException("MVS returned an incomplete frame.");
                if(lastFrame==info.nFrameNum)throw new InvalidDataException("MVS returned a repeated frame number.");
                var width=checked((int)info.nWidth);var height=checked((int)info.nHeight);
                if(width>30000||height>30000)throw new InvalidDataException("MVS frame dimensions are invalid.");
                var bgr=ConvertToBgr(frame,width,height);
                lastFrame=info.nFrameNum;
                return new(width,height,checked(width*3),bgr,Guid.NewGuid(),DateTimeOffset.UtcNow,selected!.Name);
            }
            finally
            {
                Check(activeCamera.MV_CC_FreeImageBuffer_NET(ref frame),"release image buffer");
            }
        }
    }

    public void Stop()
    {
        lock(gate)
        {
            if(camera==null||!grabbing)return;
            try{Check(camera.MV_CC_StopGrabbing_NET(),"stop acquisition");}
            finally{grabbing=false;lastFrame=null;}
        }
    }

    public void Close()
    {
        lock(gate)
        {
            if(camera==null)return;
            try
            {
                if(grabbing)Check(camera.MV_CC_StopGrabbing_NET(),"stop acquisition");
                Check(camera.MV_CC_CloseDevice_NET(),"close camera");
            }
            finally
            {
                camera.MV_CC_DestroyDevice_NET();camera=null;selected=null;grabbing=false;lastFrame=null;
            }
        }
    }

    public void Dispose()
    {
        lock(gate)
        {
            Close();
            if(initialized){MyCamera.MV_CC_Finalize_NET();initialized=false;}
        }
    }

    private byte[] ConvertToBgr(MyCamera.MV_FRAME_OUT frame,int width,int height)
    {
        var pixelType=frame.stFrameInfo.enPixelType;
        var length=checked(width*height*3);
        if(pixelType==MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
        {
            var mono=new byte[checked(width*height)];
            if(frame.stFrameInfo.nFrameLen<mono.Length)throw new InvalidDataException("MVS Mono8 buffer is shorter than expected.");
            Marshal.Copy(frame.pBufAddr,mono,0,mono.Length);
            return HikrobotFrameConverter.Mono8ToBgr(mono);
        }
        if(pixelType==MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed)
        {
            if(frame.stFrameInfo.nFrameLen<length)throw new InvalidDataException("MVS BGR8 buffer is shorter than expected.");
            var bgr=new byte[length];Marshal.Copy(frame.pBufAddr,bgr,0,length);return bgr;
        }
        if(pixelType==MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed)
        {
            if(frame.stFrameInfo.nFrameLen<length)throw new InvalidDataException("MVS RGB8 buffer is shorter than expected.");
            var rgb=new byte[length];Marshal.Copy(frame.pBufAddr,rgb,0,length);return HikrobotFrameConverter.Rgb8ToBgr(rgb);
        }

        var monoDestination=pixelType.ToString().Contains("Mono",StringComparison.Ordinal);
        var destination=monoDestination?MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8:MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed;
        var destinationLength=checked(width*height*(monoDestination?1:3));
        var converted=Marshal.AllocHGlobal(destinationLength);
        try
        {
            var conversion=new MyCamera.MV_PIXEL_CONVERT_PARAM
            {
                nWidth=checked((ushort)width),nHeight=checked((ushort)height),pSrcData=frame.pBufAddr,nSrcDataLen=frame.stFrameInfo.nFrameLen,
                enSrcPixelType=pixelType,enDstPixelType=destination,pDstBuffer=converted,nDstBufferSize=(uint)destinationLength
            };
            Check(camera!.MV_CC_ConvertPixelType_NET(ref conversion),$"convert pixel format {pixelType}");
            if(conversion.nDstLen==0||conversion.nDstLen>destinationLength)throw new InvalidDataException("MVS returned an invalid converted frame length.");
            var bytes=new byte[conversion.nDstLen];Marshal.Copy(converted,bytes,0,bytes.Length);
            return monoDestination?HikrobotFrameConverter.Mono8ToBgr(bytes):bytes;
        }
        finally{Marshal.FreeHGlobal(converted);}
    }

    private void EnsureInitialized()
    {
        if(initialized)return;
        Check(MyCamera.MV_CC_Initialize_NET(),"initialize MVS SDK");initialized=true;
    }
    private void RequireOpen(){if(camera==null||selected==null)throw new InvalidOperationException("Camera is disconnected.");}
    private static void Check(int status,string operation)
    {
        if(status==MyCamera.MV_OK)return;
        var hint=status switch
        {
            MyCamera.MV_E_ACCESS_DENIED=>" Camera đang được ứng dụng khác sử dụng; hãy Stop/đóng MVS.",
            MyCamera.MV_E_BUSY=>" Camera đang bận hoặc mất kết nối.",
            MyCamera.MV_E_NETER=>" Lỗi mạng camera GigE.",
            _=>string.Empty
        };
        throw new InvalidOperationException($"MVS {operation} failed (0x{unchecked((uint)status):X8}).{hint}");
    }
    private static (string Transport,string Serial,string DisplayName) Describe(MyCamera.MV_CC_DEVICE_INFO info,int index)
    {
        if(info.nTLayerType==MyCamera.MV_GIGE_DEVICE)
        {
            var value=(MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(info.SpecialInfo.stGigEInfo,typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            var serial=Clean(value.chSerialNumber,$"index-{index}");
            var model=Clean(value.chModelName,"Hikrobot GigE");
            var user=Decode(value.chUserDefinedName);
            var ip=$"{value.nCurrentIp>>24&255}.{value.nCurrentIp>>16&255}.{value.nCurrentIp>>8&255}.{value.nCurrentIp&255}";
            return ("gige",serial,$"{(string.IsNullOrWhiteSpace(user)?model:user)} · {serial} · {ip}");
        }
        if(info.nTLayerType==MyCamera.MV_USB_DEVICE)
        {
            var value=(MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(info.SpecialInfo.stUsb3VInfo,typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
            var serial=Clean(value.chSerialNumber,$"index-{index}");
            var model=Clean(value.chModelName,"Hikrobot USB3");
            var user=Decode(value.chUserDefinedName);
            return ("usb3",serial,$"{(string.IsNullOrWhiteSpace(user)?model:user)} · {serial}");
        }
        throw new NotSupportedException($"Unsupported MVS transport type 0x{info.nTLayerType:X}.");
    }
    private static string Clean(string? value,string fallback)=>string.IsNullOrWhiteSpace(value)?fallback:value.TrimEnd('\0').Trim();
    private static string Decode(byte[]? value)
    {
        if(value==null||value.Length==0)return string.Empty;
        var length=Array.IndexOf(value,(byte)0);if(length<0)length=value.Length;
        if(length==0)return string.Empty;
        try{return new UTF8Encoding(false,true).GetString(value,0,length).Trim();}
        catch(DecoderFallbackException){return Encoding.Default.GetString(value,0,length).Trim();}
    }
#else
    private static Exception MissingSdk()=>new DllNotFoundException("Không tìm thấy Hikrobot MVS .NET SDK. Cài MVS hoặc đặt MvCameraControl.Net.dll trong vendor/camera rồi build lại.");
    public IReadOnlyList<CameraDevice> Enumerate()=>throw MissingSdk();
    public void Open(CameraDevice device)=>throw MissingSdk();
    public void SetParameter(string name,string value)=>throw MissingSdk();
    public void Start()=>throw MissingSdk();
    public ImageFrame Grab(int timeoutMs)=>throw MissingSdk();
    public void Stop(){}
    public void Close(){}
    public void Dispose(){}
#endif
}

internal static class HikrobotFrameConverter
{
    internal static byte[] Mono8ToBgr(ReadOnlySpan<byte> mono)
    {
        var bgr=new byte[checked(mono.Length*3)];
        for(var source=0;source<mono.Length;source++)
        {
            var target=source*3;bgr[target]=mono[source];bgr[target+1]=mono[source];bgr[target+2]=mono[source];
        }
        return bgr;
    }
    internal static byte[] Rgb8ToBgr(ReadOnlySpan<byte> rgb)
    {
        if(rgb.Length%3!=0)throw new ArgumentException("RGB8 buffer length must be divisible by three.",nameof(rgb));
        var bgr=rgb.ToArray();
        for(var index=0;index<bgr.Length;index+=3)(bgr[index],bgr[index+2])=(bgr[index+2],bgr[index]);
        return bgr;
    }
}
