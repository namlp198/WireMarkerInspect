using System.IO;
using System.Text.Json;
using WireMarkerInspection.Application;
using WireMarkerInspection.Controls.Localization;
using WireMarkerInspection.Desktop.Services;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using WireMarkerInspection.Vision;
using Xunit;

namespace WireMarkerInspection.Tests;
[Collection(DispatcherTestHost.Collection)]
public sealed class TerminalMatchingTests
{
    internal static ImageFrame Texture(int seed=12)
    {
        const int w=480,h=360;var random=new Random(seed);var data=new byte[w*h*3];
        // Multiscale corners; generated image is a software fixture, not an accuracy dataset.
        for(int y=0;y<h;y+=6)for(int x=0;x<w;x+=6)
        {var v=(byte)random.Next(20,235);for(int yy=y;yy<Math.Min(h,y+6);yy++)for(int xx=x;xx<Math.Min(w,x+6);xx++)for(int c=0;c<3;c++)data[(yy*w+xx)*3+c]=v;}
        return new(w,h,w*3,data,Guid.NewGuid(),DateTimeOffset.UtcNow,"SYNTHETIC MATCHING TEST");
    }
    internal static TerminalTemplate Template(ImageFrame frame,MatchingAlgorithm algorithm)
    {
        var p=MatchingParameters.Defaults(algorithm);p[MatchParameter.AngleMin]=0;p[MatchParameter.AngleMax]=0;p[MatchParameter.ScaleMin]=1;p[MatchParameter.ScaleMax]=1;
        // Feature keypoints estimate a transform; allow subpixel fitting noise, unlike a fixed Normal sweep.
        if(algorithm!=MatchingAlgorithm.Normal){p[MatchParameter.AngleMin]=-1;p[MatchParameter.AngleMax]=1;p[MatchParameter.ScaleMin]=.99;p[MatchParameter.ScaleMax]=1.01;}
        return new(true,algorithm,Width:frame.Width,Height:frame.Height,LearnRoi:new(RoiShape.Rectangle,[new(90,60),new(390,300)]),
            SearchRoi:SearchRoi.FullImage(frame.Width,frame.Height),Profiles:new(){[algorithm]=p}){TemplatePng=ImageFiles.Png(ImageFiles.Bitmap(frame))};
    }
    [Theory]
    [InlineData(MatchingAlgorithm.Normal)][InlineData(MatchingAlgorithm.Akaze)][InlineData(MatchingAlgorithm.Sift)]
    [InlineData(MatchingAlgorithm.Orb)][InlineData(MatchingAlgorithm.OrbMaxStable)]
    public async Task EveryAlgorithmAcceptsExactTemplateAndRejectsDifferentTerminal(MatchingAlgorithm algorithm)
    {
        var frame=Texture();var template=Template(frame,algorithm);var matcher=new NativeTemplateMatcher();
        var good=await matcher.MatchAsync(frame,template,default);
        Assert.True(good.Passed,$"{algorithm}: {good.Reason} NCC={good.Ncc} SSIM={good.Ssim} Edge={good.Edge}");
        Assert.Equal(4,good.Corners.Length);Assert.NotEmpty(good.AlignedPng);Assert.NotEmpty(good.TemplatePng);
        Assert.InRange(good.Corners[0].X,88,92);Assert.InRange(good.Corners[0].Y,58,62);
        var bad=await matcher.MatchAsync(Texture(71),template,default);Assert.False(bad.Passed);
    }
    [Fact]public async Task NormalFullImageTemplateAndMaskedRoiRemainValid()
    {
        var frame=Texture();var matcher=new NativeTemplateMatcher();var t=Template(frame,MatchingAlgorithm.Normal) with {LearnRoi=SearchRoi.FullImage(frame.Width,frame.Height)};
        Assert.True((await matcher.MatchAsync(frame,t,default)).Passed);
        t=t with {LearnRoi=new(RoiShape.Circle,[new(240,180),new(330,180)])};
        Assert.True((await matcher.MatchAsync(frame,t,default)).Passed);
        t=t with {SearchRoi=new(RoiShape.Rectangle,[new(0,0),new(100,100)])};
        Assert.False((await matcher.MatchAsync(frame,t,default)).Passed);
    }
    [Theory][InlineData(MatchingAlgorithm.Akaze)][InlineData(MatchingAlgorithm.Sift)]
    public async Task FeatureGateFailureRetainsMeasuredEvidenceButCannotPass(MatchingAlgorithm algorithm)
    {
        var f=Texture();var t=Template(f,algorithm);var p=t.ActiveParameters();
        p[MatchParameter.MinMatches]=10000;p[MatchParameter.MinInliers]=10000;
        var r=await new NativeTemplateMatcher().MatchAsync(f,t,default);
        Assert.False(r.Passed);Assert.Equal("InsufficientMatches",r.Reason);
        Assert.True(r.Matches>4);Assert.True(r.Inliers>4);Assert.True(r.Diagnostics!.TemplateKeypoints>0);
        Assert.True(r.Diagnostics.AppearanceEvaluated);Assert.InRange(r.Ncc,.95,1);
        Assert.Equal(10000,r.Diagnostics.Thresholds[(int)MatchParameter.MinMatches]);
        Assert.Contains(MatchingPresentation.Checks(r),c=>c.Text.StartsWith("NCC")&&c.Passed==true);
        Assert.Contains(MatchingPresentation.Checks(r),c=>c.Text.StartsWith("Match ")&&c.Passed==false);
        p[MatchParameter.MinMatches]=12;Assert.Equal(10000,r.Diagnostics.Thresholds[(int)MatchParameter.MinMatches]);
    }
    [Theory][InlineData(MatchingAlgorithm.Akaze)][InlineData(MatchingAlgorithm.Sift)]
    public async Task MissingSourceFeaturesAreNotDisplayedAsMeasuredZero(MatchingAlgorithm algorithm)
    {
        var f=Texture();var r=await new NativeTemplateMatcher().MatchAsync(f with {Bgr=new byte[f.Bgr.Length]},Template(f,algorithm),default);
        Assert.False(r.Passed);Assert.Equal("NoSourceFeatures",r.Reason);
        Assert.True(r.Diagnostics!.TemplateKeypoints>0);Assert.Equal(0,r.Diagnostics.SourceKeypoints);
        Assert.False(r.Diagnostics.AppearanceEvaluated);Assert.False(r.Diagnostics.PoseEvaluated);Assert.Empty(r.AlignedPng);
        var ncc=Assert.Single(MatchingPresentation.Checks(r),c=>c.Text.StartsWith("NCC"));
        Assert.Contains("N/A",ncc.Text);Assert.Null(ncc.Passed);
    }
    [Fact]public async Task CoverageFailureRetainsPoseAndScoresWithoutBypassingGate()
    {
        var f=Texture();var t=Template(f,MatchingAlgorithm.Sift);t.ActiveParameters()[MatchParameter.Coverage]=1;
        var r=await new NativeTemplateMatcher().MatchAsync(f,t,default);
        Assert.False(r.Passed);Assert.Equal("LowFeatureCoverage",r.Reason);Assert.True(r.Diagnostics!.AppearanceEvaluated);
        Assert.True(r.Coverage>0&&r.Coverage<1);Assert.NotEmpty(r.AlignedPng);
    }
    [Fact]public void ResultColorsAreIndependentOfOverallVerdict()=>DispatcherTestHost.Sta(()=>
    {
        var f=Texture();var e=new EndRecipe("ref",f.Width,f.Height,SearchRoi.FullImage(f.Width,f.Height),["ABC"]);
        var vm=new EndResultViewModel(1);vm.Reset(e);
        var preview=ImageFiles.Png(ImageFiles.Bitmap(f));
        EndResult Text(string text,int rotation)=>ExactTextComparer.Compare(f,e,new([new(text,1,[],preview)],rotation));
        vm.Show(f,CombinedInspectionComparer.Combine(Text("ABC",0),Result(false),true));
        Assert.Equal("NG",vm.Status);Assert.True(vm.TextCheck.Passed);Assert.True(vm.OrientationCheck.Passed);Assert.False(vm.TerminalChecks[0].Passed);
        vm.Show(f,Text("ABC",180));Assert.True(vm.TextPassed);Assert.False(vm.OrientationCheck.Passed);
        vm.Show(f,Text("BAD",0));Assert.False(vm.TextPassed);Assert.True(vm.OrientationCheck.Passed);
        var copy=new EndResultViewModel(2);copy.CopyFrom(vm);Assert.Equal(vm.TextCheck,copy.TextCheck);Assert.Equal(vm.OrientationCheck,copy.OrientationCheck);
        vm.Show(f,null);Assert.Null(vm.TextPassed);Assert.Null(vm.OrientationCheck.Passed);Assert.Empty(vm.TerminalChecks);
    });
    [Fact]public void MeasuredZeroRemainsNgAndLegacyUnknownEvidenceStaysNeutral()
    {
        var p=MatchingParameters.Defaults(MatchingAlgorithm.Normal);
        var d=new MatchingDiagnostics(0,0,0,0,false,true,true,true,true,MatchingParameters.Definitions.Select(x=>p[x.Key]).ToArray(),1,1,"NccBelowThreshold");
        var r=Result(false) with {Ncc=0,Diagnostics=d};
        var ncc=Assert.Single(MatchingPresentation.Checks(r),c=>c.Text.StartsWith("NCC"));
        Assert.DoesNotContain("N/A",ncc.Text);Assert.False(ncc.Passed);
        Assert.Null(MatchingPresentation.Checks(r with {Diagnostics=null})[1].Passed);
    }
    [Fact]public async Task InvalidAssetsAndEmptyTextureNeverPass()
    {
        var f=Texture();var t=Template(f,MatchingAlgorithm.Normal);var matcher=new NativeTemplateMatcher();
        await Assert.ThrowsAsync<ArgumentException>(()=>matcher.MatchAsync(f,t with {TemplatePng=[1,2,3]},default));
        Assert.NotNull((t with {Width=t.Width+1}).Validate(f.Width,f.Height));
        var blank=f with {Bgr=new byte[f.Bgr.Length]};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>matcher.MatchAsync(f,Template(blank,MatchingAlgorithm.Normal),default));
        var p=t.ActiveParameters();p[MatchParameter.Score]=double.NaN;
        Assert.NotNull(t.Validate(f.Width,f.Height));
    }
    [Theory][InlineData(true,0,true,Verdict.Ok)][InlineData(true,0,false,Verdict.Ng)]
    [InlineData(false,0,true,Verdict.Ng)][InlineData(true,180,true,Verdict.Ng)]
    public async Task SessionRequiresTextDirectionAndTemplateOnSameFrame(bool text,int direction,bool templatePass,Verdict expected)
    {
        var frame=Texture();var t=Template(frame,MatchingAlgorithm.Normal);
        var end=new EndRecipe("ref.png",frame.Width,frame.Height,SearchRoi.FullImage(frame.Width,frame.Height),["ABC"],Terminal:t);
        var recipe=new Recipe(Guid.NewGuid(),"TEST","Test",1,[end,end.Copy()],DateTimeOffset.UtcNow,3);
        var matcher=new FakeMatcher(templatePass);var sink=new Sink();var session=new InspectionSession(new FakeOcr(text?"ABC":"BAD",direction),sink,matcher);
        session.Begin(recipe);t.ActiveParameters()[MatchParameter.Ncc]=.123;t.TemplatePng[0]=0;
        var result=await session.AcceptAsync(frame);Assert.Equal(expected,result!.Verdict);Assert.Equal(frame.Id,matcher.FrameId);
        Assert.NotEqual(.123,matcher.Template!.ActiveParameters()[MatchParameter.Ncc]);Assert.NotEqual(0,matcher.Template.TemplatePng[0]);
        await session.AcceptAsync(frame with {Id=Guid.NewGuid()});Assert.Equal(expected,session.Result!.Verdict);Assert.Equal(1,sink.Count);
    }
    [Fact]public async Task StoppingDuringMatchingDiscardsLateResult()
    {
        var frame=Texture();var end=new EndRecipe("ref",frame.Width,frame.Height,SearchRoi.FullImage(frame.Width,frame.Height),["ABC"],Terminal:Template(frame,MatchingAlgorithm.Normal));
        var matcher=new DeferredMatcher();var sink=new Sink();var session=new InspectionSession(new FakeOcr("ABC",0),sink,matcher);
        session.Begin(new(Guid.NewGuid(),"C","C",1,[end,end],DateTimeOffset.UtcNow,3));
        var pending=session.AcceptAsync(frame);session.Stop();matcher.Signal.SetResult(Result(true));
        Assert.Null(await pending);Assert.Empty(session.EndResults);Assert.Equal(0,sink.Count);
    }
    [Fact]public void TemplatePersistenceIsImmutableAndMissingAssetIsRejected()
    {
        var root=Path.Combine(Path.GetTempPath(),"wmi-template-"+Guid.NewGuid().ToString("N"));
        try
        {
            var f=Texture();var t=Template(f,MatchingAlgorithm.Normal);var e=new EndRecipe("ref",f.Width,f.Height,SearchRoi.FullImage(f.Width,f.Height),["ABC"],Terminal:t);
            var store=new FileRecipeStore(root);var r=store.Save(new(Guid.NewGuid(),"T","T",0,[e,e.Copy()],DateTimeOffset.UtcNow,3),[t.TemplatePng,t.TemplatePng]);
            var loaded=Assert.Single(store.LoadAll());Assert.Equal(t.TemplatePng,loaded.Ends[0].Terminal!.TemplatePng);
            var updated=store.Save(loaded,[t.TemplatePng,t.TemplatePng]);Assert.NotEqual(r.Ends[0].Terminal!.TemplateImage,updated.Ends[0].Terminal!.TemplateImage);
            File.Delete(Path.Combine(root,"recipes",r.Id.ToString("N"),updated.Ends[0].Terminal!.TemplateImage));
            Assert.Empty(store.LoadAll());Assert.Single(store.LoadErrors);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]public void LanguageAndAlgorithmSwitchPreserveProfilesAndCancelIsIsolated()=>DispatcherTestHost.Sta(()=>
    {
        var frame=Texture();var end=new EndEditorViewModel(1);end.SetFrame(frame);end.SetTerminal(Template(frame,MatchingAlgorithm.Normal));
        var before=JsonSerializer.Serialize(end.Terminal);
        using var vm=new TemplateEditorViewModel(end,new FakeMatcher(true));
        vm.Parameters.Single(p=>p.Key==MatchParameter.Score).Text="0.91";
        vm.Algorithm=MatchingAlgorithm.Sift;vm.Parameters.Single(p=>p.Key==MatchParameter.Score).Text="0.89";
        var draft=JsonSerializer.Serialize(vm.Build());
        foreach(var language in Enum.GetValues<AppLanguage>())
        {AppLocalizer.ChangeLanguage(language,false);Assert.Equal(draft,JsonSerializer.Serialize(vm.Build()));}
        vm.Algorithm=MatchingAlgorithm.Normal;Assert.Equal("0.91",vm.Parameters.Single(p=>p.Key==MatchParameter.Score).Text);
        Assert.Equal(before,JsonSerializer.Serialize(end.Terminal));AppLocalizer.ChangeLanguage(AppLanguage.Vietnamese,false);
    });
    private static TemplateMatchResult Result(bool pass)=>new(pass,pass?"Matched":"NoCandidate",pass?1:0,1,1,1,0,1,20,20,1,1,1,[],[],[],1);
    [Theory]
    [InlineData(MatchingAlgorithm.Normal)][InlineData(MatchingAlgorithm.Akaze)][InlineData(MatchingAlgorithm.Sift)]
    [InlineData(MatchingAlgorithm.Orb)][InlineData(MatchingAlgorithm.OrbMaxStable)]
    public async Task ReflectionWrongRotationAndRepeatedCandidatesCannotPass(MatchingAlgorithm algorithm)
    {
        var f=Texture();var t=Template(f,algorithm);var matcher=new NativeTemplateMatcher();
        var reflected=new byte[f.Bgr.Length];var rotated=new byte[f.Bgr.Length];
        for(int y=0;y<f.Height;y++)for(int x=0;x<f.Width;x++)for(int c=0;c<3;c++)
        {
            reflected[(y*f.Width+x)*3+c]=f.Bgr[(y*f.Width+f.Width-1-x)*3+c];
            rotated[(y*f.Width+x)*3+c]=f.Bgr[((f.Height-1-y)*f.Width+f.Width-1-x)*3+c];
        }
        Assert.False((await matcher.MatchAsync(f with {Bgr=reflected},t,default)).Passed);
        Assert.False((await matcher.MatchAsync(f with {Bgr=rotated},t,default)).Passed);
        const int width=1000;var duplicate=new byte[width*f.Height*3];
        for(int y=0;y<f.Height;y++)foreach(int offset in new[]{0,520})Array.Copy(f.Bgr,y*f.Stride,duplicate,(y*width+offset)*3,f.Stride);
        var wide=f with {Width=width,Stride=width*3,Bgr=duplicate};
        var ambiguous=await matcher.MatchAsync(wide,t with {SearchRoi=SearchRoi.FullImage(width,f.Height)},default);
        Assert.False(ambiguous.Passed,$"{algorithm} accepted ambiguous duplicate terminals");
    }
    [Fact]public async Task NormalCanSearchApproved180DegreePose()
    {
        var f=Texture();var t=Template(f,MatchingAlgorithm.Normal);var rotated=new byte[f.Bgr.Length];
        for(int i=0;i<f.Width*f.Height;i++)Array.Copy(f.Bgr,i*3,rotated,(f.Width*f.Height-1-i)*3,3);
        t.ActiveParameters()[MatchParameter.AngleMin]=180;t.ActiveParameters()[MatchParameter.AngleMax]=180;
        var r=await new NativeTemplateMatcher().MatchAsync(f with {Bgr=rotated},t,default);
        Assert.True(r.Passed,r.Reason);Assert.InRange(r.Angle,179.99,180.01);
    }
    [Fact]public void AllDynamicMatchingLabelsHaveThreeTranslations()
    {
        var original=AppLocalizer.CurrentLanguage;
        try{foreach(var language in Enum.GetValues<AppLanguage>())
        {
            AppLocalizer.ChangeLanguage(language,false);
            foreach(var key in Enum.GetValues<MatchingAlgorithm>().Select(a=>$"MatchingAlgorithm{a}").Concat(Enum.GetValues<MatchParameter>().Select(p=>$"MatchParam{p}")))
                Assert.NotEqual(key,AppLocalizer.Text(key));
        }}finally{AppLocalizer.ChangeLanguage(original,false);}
    }
    [Fact]public void NewModelRequiresTemplatesButLegacyRecipeRemainsUnchanged()=>DispatcherTestHost.Sta(()=>
    {
        var root=Path.Combine(Path.GetTempPath(),"wmi-template-role-"+Guid.NewGuid().ToString("N"));
        var main=new MainViewModel(root,autoDiscoverCameraOnLoad:false).AsAdmin();
        try
        {
            main.NewModelCommand.Execute(new ModelIdentity("T","T"));Assert.True(main.End1.Terminal.Enabled);Assert.True(main.End2.Terminal.Enabled);
            main.End1.SetFrame(Texture());main.End1.Roi=SearchRoi.FullImage(480,360);main.End1.ExpectedText="ABC";
            Assert.Throws<InvalidOperationException>(()=>main.End1.Apply());
            var e=InspectionTests.End("ABC");main.End1.Load(e,ImageFiles.Png(ImageFiles.Bitmap(InspectionTests.Frame())));
            Assert.False(main.End1.Terminal.Enabled);Assert.True(main.End1.Applied);
        }
        finally{main.ShutdownAsync().GetAwaiter().GetResult();if(Directory.Exists(root))Directory.Delete(root,true);}
    });
    private sealed class FakeOcr(string text,int direction):IOcrEngine
    {public Task<OcrReading> ReadAsync(ImageFrame f,EndRecipe r,CancellationToken c)=>Task.FromResult(new OcrReading([new(text,1,[],[])],direction));}
    private sealed class FakeMatcher(bool pass):ITemplateMatcher
    {
        public Guid FrameId;public TerminalTemplate? Template;
        public Task<TemplateMatchResult> MatchAsync(ImageFrame f,TerminalTemplate t,CancellationToken c){FrameId=f.Id;Template=t;return Task.FromResult(Result(pass));}
    }
    private sealed class DeferredMatcher:ITemplateMatcher
    {
        public TaskCompletionSource<TemplateMatchResult> Signal=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TemplateMatchResult> MatchAsync(ImageFrame f,TerminalTemplate t,CancellationToken c)=>Signal.Task;
    }
    private sealed class Sink:IResultStore
    {public int Count;public Task SaveAsync(ProductResult r,ImageFrame[] f,CancellationToken c){Count++;return Task.CompletedTask;}}
}
