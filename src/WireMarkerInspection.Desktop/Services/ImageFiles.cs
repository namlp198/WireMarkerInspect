using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WireMarkerInspection.Domain;
namespace WireMarkerInspection.Desktop.Services;
public static class ImageFiles
{
    public static BitmapSource Decode(byte[] bytes, int thumbnailWidth = 0)
    {
        using var stream=new MemoryStream(bytes);
        var bitmap=new BitmapImage();
        bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        if(thumbnailWidth>0)bitmap.DecodePixelWidth=thumbnailWidth;
        bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    public static ImageFrame Load(string path)
    {
        var image=Decode(File.ReadAllBytes(path));
        return Frame(image,"OFFLINE · "+Path.GetFileName(path));
    }
    public static ImageFrame Frame(BitmapSource image,string source)
    {
        var converted=new FormatConvertedBitmap(image,PixelFormats.Bgr24,null,0);
        var stride=checked(converted.PixelWidth*3);
        var pixels=new byte[checked(stride*converted.PixelHeight)];
        converted.CopyPixels(pixels,stride,0);
        return new(converted.PixelWidth,converted.PixelHeight,stride,pixels,Guid.NewGuid(),DateTimeOffset.UtcNow,source);
    }
    public static BitmapSource Bitmap(ImageFrame frame)
    {
        frame.Validate();
        var result=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgr24,null,frame.Bgr,frame.Stride);
        result.Freeze();return result;
    }
    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
}
