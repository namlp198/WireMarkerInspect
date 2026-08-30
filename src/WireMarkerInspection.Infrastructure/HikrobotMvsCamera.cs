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
    private CameraTriggerSource triggerSource=CameraTriggerSource.FreeRun;

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
                selected=device;lastFrame=null;triggerSource=CameraTriggerSource.FreeRun;
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

    public CameraInfo ReadInfo()
    {
        lock(gate)
        {
            RequireOpen();
            var pixel=TryEnum("PixelFormat",out var pixelValue)
                ?((MyCamera.MvGvspPixelType)pixelValue).ToString().Replace("PixelType_Gvsp_",string.Empty):"unknown";
            return new(
                TryString("DeviceModelName")??selected!.Name,
                TryString("DeviceSerialNumber")??string.Empty,
                pixel,
                TryInt("WidthMax",out var maxWidth)?checked((int)maxWidth.nCurValue):0,
                TryInt("HeightMax",out var maxHeight)?checked((int)maxHeight.nCurValue):0,
                TryFloat("ResultingFrameRate",out var fps)?fps.fCurValue:null,
                TryFloat("DeviceTemperature",out var temperature)?temperature.fCurValue:null);
        }
    }

    /// <summary>Real GenICam limits, so the UI shows what this camera actually accepts.</summary>
    public IReadOnlyList<CameraParameterInfo> DescribeParameters()
    {
        lock(gate)
        {
            RequireOpen();
            var list=new List<CameraParameterInfo>();
            void Float(string name,string unit)
            {
                if(TryFloat(name,out var value))list.Add(new(name,unit,value.fMin,value.fMax,0,value.fCurValue,true));
            }
            void Integer(string name,string unit)
            {
                if(TryInt(name,out var value))list.Add(new(name,unit,value.nMin,value.nMax,value.nInc,value.nCurValue,true));
            }
            Float("ExposureTime","us");Float("Gain","dB");Float("Gamma",string.Empty);Float("BlackLevel",string.Empty);
            Integer("OffsetX","px");Integer("OffsetY","px");Integer("Width","px");Integer("Height","px");
            Integer("StrobeLineDuration","us");Integer("StrobeLineDelay","us");
            return list;
        }
    }

    public CameraSettings ReadSettings()
    {
        lock(gate)
        {
            RequireOpen();
            var exposure=TryFloat("ExposureTime",out var exposureValue)?exposureValue.fCurValue:0;
            var gain=TryFloat("Gain",out var gainValue)?gainValue.fCurValue:0;
            double? gamma=TryBool("GammaEnable",out var gammaEnabled)&&gammaEnabled&&TryFloat("Gamma",out var gammaValue)
                ?gammaValue.fCurValue:null;
            double? blackLevel=TryFloat("BlackLevel",out var black)?black.fCurValue:null;
            SensorRoi? roi=TryInt("Width",out var width)&&TryInt("Height",out var height)
                &&TryInt("OffsetX",out var offsetX)&&TryInt("OffsetY",out var offsetY)
                ?new(checked((int)offsetX.nCurValue),checked((int)offsetY.nCurValue),
                     checked((int)width.nCurValue),checked((int)height.nCurValue)):null;
            return new(exposure,gain,gamma,blackLevel,roi,null);
        }
    }

    /// <summary>
    /// Applies a taught acquisition setup. A requested value this camera does not expose is an error
    /// rather than a silent skip: the operator asked for it and the recipe recorded it.
    /// </summary>
    public void ApplySettings(CameraSettings settings)
    {
        if(settings.Validate() is{}invalid)throw new ArgumentException(invalid,nameof(settings));
        lock(gate)
        {
            RequireOpen();
            if(settings.Roi!=null&&grabbing)
                throw new InvalidOperationException("Dung acquisition truoc khi doi vung doc sensor (ROI).");
            if(settings.Roi is{}roi)
            {
                // Offsets must collapse before the window grows, otherwise MVS rejects an out-of-range window.
                Require("OffsetX",camera!.MV_CC_SetIntValueEx_NET("OffsetX",0));
                Require("OffsetY",camera.MV_CC_SetIntValueEx_NET("OffsetY",0));
                Require("Width",camera.MV_CC_SetIntValueEx_NET("Width",roi.Width));
                Require("Height",camera.MV_CC_SetIntValueEx_NET("Height",roi.Height));
                Require("OffsetX",camera.MV_CC_SetIntValueEx_NET("OffsetX",roi.OffsetX));
                Require("OffsetY",camera.MV_CC_SetIntValueEx_NET("OffsetY",roi.OffsetY));
            }
            Check(camera!.MV_CC_SetEnumValue_NET("ExposureAuto",0),"disable auto exposure");
            Require("ExposureTime",camera.MV_CC_SetFloatValue_NET("ExposureTime",(float)settings.ExposureTimeUs));
            Check(camera.MV_CC_SetEnumValue_NET("GainAuto",0),"disable auto gain");
            Require("Gain",camera.MV_CC_SetFloatValue_NET("Gain",(float)settings.Gain));
            if(settings.Gamma is{}gamma)
            {
                Require("GammaEnable",camera.MV_CC_SetBoolValue_NET("GammaEnable",true));
                Require("Gamma",camera.MV_CC_SetFloatValue_NET("Gamma",(float)gamma));
            }
            else if(TryBool("GammaEnable",out _))camera.MV_CC_SetBoolValue_NET("GammaEnable",false);
            if(settings.BlackLevel is{}blackLevel)
            {
                if(TryBool("BlackLevelEnable",out _))camera.MV_CC_SetBoolValue_NET("BlackLevelEnable",true);
                Require("BlackLevel",camera.MV_CC_SetFloatValue_NET("BlackLevel",(float)blackLevel));
            }
            if(settings.Strobe is{}strobe)
            {
                Require("LineSelector",camera.MV_CC_SetEnumValue_NET("LineSelector",(uint)strobe.Line));
                Require("StrobeEnable",camera.MV_CC_SetBoolValue_NET("StrobeEnable",strobe.Enabled));
                if(strobe.Enabled)
                {
                    Require("StrobeLineDuration",camera.MV_CC_SetIntValueEx_NET("StrobeLineDuration",(long)strobe.DurationUs));
                    Require("StrobeLineDelay",camera.MV_CC_SetIntValueEx_NET("StrobeLineDelay",(long)strobe.DelayUs));
                }
            }
        }
    }

    /// <summary>
    /// Free-run and triggered acquisition are different acquisition lifecycles, not one setting: MVS
    /// rejects a trigger-source change while grabbing, so the caller must stop acquisition first.
    /// </summary>
    public void ConfigureTrigger(CameraTrigger trigger)
    {
        if(trigger.Validate() is{}invalid)throw new ArgumentException(invalid,nameof(trigger));
        lock(gate)
        {
            RequireOpen();
            if(grabbing)throw new InvalidOperationException("Dừng acquisition trước khi đổi chế độ trigger.");
            if(trigger.Source==CameraTriggerSource.FreeRun)
            {
                Require("TriggerMode",camera!.MV_CC_SetEnumValue_NET("TriggerMode",(uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF));
                triggerSource=CameraTriggerSource.FreeRun;
                return;
            }
            Require("TriggerMode",camera!.MV_CC_SetEnumValue_NET("TriggerMode",(uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON));
            if(trigger.Source==CameraTriggerSource.Software)
            {
                Require("TriggerSource",camera.MV_CC_SetEnumValue_NET("TriggerSource",(uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE));
            }
            else
            {
                Require("TriggerSource",camera.MV_CC_SetEnumValue_NET("TriggerSource",checked((uint)trigger.Line)));
                Require("TriggerActivation",camera.MV_CC_SetEnumValue_NET("TriggerActivation",trigger.RisingEdge?0u:1u));
                if(trigger.DebouncerUs>0)
                {
                    Require("LineSelector",camera.MV_CC_SetEnumValue_NET("LineSelector",checked((uint)trigger.Line)));
                    Require("LineDebouncerTime",camera.MV_CC_SetIntValueEx_NET("LineDebouncerTime",(long)trigger.DebouncerUs));
                }
            }
            if(trigger.DelayUs>0)Require("TriggerDelay",camera.MV_CC_SetFloatValue_NET("TriggerDelay",(float)trigger.DelayUs));
            triggerSource=trigger.Source;
        }
    }

    public void ExecuteSoftwareTrigger()
    {
        lock(gate)
        {
            RequireOpen();
            if(triggerSource!=CameraTriggerSource.Software)
                throw new InvalidOperationException("Camera chưa ở chế độ software trigger.");
            Require("TriggerSoftware",camera!.MV_CC_SetCommandValue_NET("TriggerSoftware"));
        }
    }

    private bool TryFloat(string name,out MyCamera.MVCC_FLOATVALUE value)
    {
        value=new();
        return camera!.MV_CC_GetFloatValue_NET(name,ref value)==MyCamera.MV_OK;
    }
    private bool TryInt(string name,out MyCamera.MVCC_INTVALUE_EX value)
    {
        value=new();
        return camera!.MV_CC_GetIntValueEx_NET(name,ref value)==MyCamera.MV_OK;
    }
    private bool TryBool(string name,out bool value)
    {
        value=false;
        return camera!.MV_CC_GetBoolValue_NET(name,ref value)==MyCamera.MV_OK;
    }
    private bool TryEnum(string name,out uint value)
    {
        var result=new MyCamera.MVCC_ENUMVALUE();
        var status=camera!.MV_CC_GetEnumValue_NET(name,ref result);
        value=result.nCurValue;
        return status==MyCamera.MV_OK;
    }
    private string? TryString(string name)
    {
        var value=new MyCamera.MVCC_STRINGVALUE();
        if(camera!.MV_CC_GetStringValue_NET(name,ref value)!=MyCamera.MV_OK)return null;
        var text=value.chCurValue?.TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(text)?null:text;
    }
    private static void Require(string parameter,int status)
    {
        if(status==MyCamera.MV_OK)return;
        throw new InvalidOperationException(
            $"Camera khong nhan thong so {parameter} (0x{unchecked((uint)status):X8}). Kiem tra gia tri nam trong dai cho phep.");
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
            // The selector shows name and serial only; the address belongs in diagnostics, not an operator list.
            return ("gige",serial,$"{(string.IsNullOrWhiteSpace(user)?model:user)} · {serial}");
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
    public CameraInfo ReadInfo()=>throw MissingSdk();
    public IReadOnlyList<CameraParameterInfo> DescribeParameters()=>throw MissingSdk();
    public CameraSettings ReadSettings()=>throw MissingSdk();
    public void ApplySettings(CameraSettings settings)=>throw MissingSdk();
    public void ConfigureTrigger(CameraTrigger trigger)=>throw MissingSdk();
    public void ExecuteSoftwareTrigger()=>throw MissingSdk();
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
