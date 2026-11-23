using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class HikrobotCameraTests
{
    [Fact]
    public void Mono8FrameIsExpandedToBgr24()
    {
        Assert.Equal(new byte[]{0,0,0,127,127,127,255,255,255},
            HikrobotFrameConverter.Mono8ToBgr(new byte[]{0,127,255}));
    }

    [Fact]
    public void Rgb8FrameIsConvertedToBgr24()
    {
        Assert.Equal(new byte[]{3,2,1,30,20,10},
            HikrobotFrameConverter.Rgb8ToBgr(new byte[]{1,2,3,10,20,30}));
    }

    [Fact]
    public void Rgb8RejectsPartialPixel()
    {
        Assert.Throws<ArgumentException>(()=>HikrobotFrameConverter.Rgb8ToBgr(new byte[]{1,2}));
    }
}
