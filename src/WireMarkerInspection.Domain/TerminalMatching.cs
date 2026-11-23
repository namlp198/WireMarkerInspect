using System.Text.Json.Serialization;

namespace WireMarkerInspection.Domain;

public enum MatchingAlgorithm { Normal, Akaze, Sift, Orb, OrbMaxStable }
// Numeric order is part of matching ABI v1. Append only; never reorder.
public enum MatchParameter
{
    Score, Ncc, Ssim, Edge, AngleMin, AngleMax, AngleStep, ScaleMin, ScaleMax, ScaleStep,
    Ratio, MaxDistance, MinMatches, MinInliers, InlierRatio, Reprojection, Confidence, Iterations,
    Coverage, Keypoints, DetectorThreshold, Octaves, Layers, Contrast, EdgeThreshold, Sigma,
    PyramidScale, Levels, FastThreshold, PatchSize, Blur, ClaheClip, ClaheGrid, Resize,
    Ambiguity, ValidPixels, Distortion, FineAngle, FineScale, Method
}
public sealed record MatchParameterDefinition(MatchParameter Key,double Default,double Min,double Max,bool Integer=false);
public static class MatchingParameters
{
    public static readonly MatchParameterDefinition[] Definitions=
    [
        new(MatchParameter.Score,.8,0,1),new(MatchParameter.Ncc,.8,0,1),new(MatchParameter.Ssim,.75,0,1),new(MatchParameter.Edge,.65,0,1),
        new(MatchParameter.AngleMin,-10,-180,180),new(MatchParameter.AngleMax,10,-180,180),new(MatchParameter.AngleStep,2,.1,45),
        new(MatchParameter.ScaleMin,.95,.25,4),new(MatchParameter.ScaleMax,1.05,.25,4),new(MatchParameter.ScaleStep,.025,.005,1),
        new(MatchParameter.Ratio,.75,.1,.99),new(MatchParameter.MaxDistance,60,1,1000),new(MatchParameter.MinMatches,12,4,10000,true),
        new(MatchParameter.MinInliers,10,4,10000,true),new(MatchParameter.InlierRatio,.65,.1,1),new(MatchParameter.Reprojection,3,.1,20),
        new(MatchParameter.Confidence,.999,.5,.99999),new(MatchParameter.Iterations,5000,100,50000,true),new(MatchParameter.Coverage,.15,.01,1),
        new(MatchParameter.Keypoints,3000,100,20000,true),new(MatchParameter.DetectorThreshold,.001,.00001,.1),
        new(MatchParameter.Octaves,4,1,8,true),new(MatchParameter.Layers,3,1,8,true),new(MatchParameter.Contrast,.04,.001,.2),
        new(MatchParameter.EdgeThreshold,31,2,100,true),new(MatchParameter.Sigma,1.6,.1,5),new(MatchParameter.PyramidScale,1.2,1.01,2),
        new(MatchParameter.Levels,8,1,16,true),new(MatchParameter.FastThreshold,20,1,100,true),new(MatchParameter.PatchSize,31,5,101,true),
        new(MatchParameter.Blur,0,0,9,true),new(MatchParameter.ClaheClip,0,0,10),new(MatchParameter.ClaheGrid,8,2,32,true),
        new(MatchParameter.Resize,1,.25,1),new(MatchParameter.Ambiguity,.05,0,1),new(MatchParameter.ValidPixels,.98,.5,1),
        new(MatchParameter.Distortion,.15,0,.5),new(MatchParameter.FineAngle,.5,.05,10),new(MatchParameter.FineScale,.01,.001,.25),
        new(MatchParameter.Method,0,0,2,true)
    ];
    public static Dictionary<MatchParameter,double> Defaults(MatchingAlgorithm algorithm)
    {
        var p=Definitions.ToDictionary(d=>d.Key,d=>d.Default);
        if(algorithm==MatchingAlgorithm.OrbMaxStable){p[MatchParameter.Blur]=3;p[MatchParameter.ClaheClip]=3;}
        if(algorithm==MatchingAlgorithm.Sift){p[MatchParameter.MaxDistance]=300;p[MatchParameter.EdgeThreshold]=10;}
        return p;
    }
    public static string? Validate(IReadOnlyDictionary<MatchParameter,double> values)
    {
        foreach(var d in Definitions)
            if(!values.TryGetValue(d.Key,out var v)||!double.IsFinite(v)||v<d.Min||v>d.Max||(d.Integer&&v!=Math.Truncate(v)))
                return $"Invalid matching parameter: {d.Key} ({d.Min}..{d.Max}).";
        if(values.Count!=Definitions.Length)return "Unknown matching parameters.";
        if(values[MatchParameter.AngleMin]>values[MatchParameter.AngleMax]||values[MatchParameter.ScaleMin]>values[MatchParameter.ScaleMax])return "Matching search range is reversed.";
        if(values[MatchParameter.Blur]!=0&&values[MatchParameter.Blur]%2==0||values[MatchParameter.PatchSize]%2==0)return "Blur and patch sizes must be odd (blur 0 disables smoothing).";
        if(values[MatchParameter.MinInliers]>values[MatchParameter.MinMatches])return "Minimum inliers cannot exceed minimum matches.";
        var attempts=(1+(values[MatchParameter.AngleMax]-values[MatchParameter.AngleMin])/values[MatchParameter.AngleStep])*
            (1+(values[MatchParameter.ScaleMax]-values[MatchParameter.ScaleMin])/values[MatchParameter.ScaleStep]);
        var fine=(1+2*values[MatchParameter.AngleStep]/values[MatchParameter.FineAngle])*(1+2*values[MatchParameter.ScaleStep]/values[MatchParameter.FineScale]);
        return attempts>10000||fine>10000?"Matching search exceeds 10000 angle/scale candidates. Narrow the range.":null;
    }
}
public sealed record TerminalTemplate(bool Enabled=false,MatchingAlgorithm Algorithm=MatchingAlgorithm.Normal,
    string TemplateImage="",int Width=0,int Height=0,SearchRoi? LearnRoi=null,SearchRoi? SearchRoi=null,
    Dictionary<MatchingAlgorithm,Dictionary<MatchParameter,double>>? Profiles=null)
{
    [JsonIgnore] public byte[] TemplatePng {get;init;}=[];
    public Dictionary<MatchParameter,double> ActiveParameters()=>Profiles?.GetValueOrDefault(Algorithm)??MatchingParameters.Defaults(Algorithm);
    public TerminalTemplate Copy()=>this with {TemplatePng=[..TemplatePng],LearnRoi=LearnRoi?.Copy(),SearchRoi=SearchRoi?.Copy(),
        Profiles=Profiles?.ToDictionary(p=>p.Key,p=>new Dictionary<MatchParameter,double>(p.Value))};
    public string? Validate(int frameWidth,int frameHeight)
    {
        if(!Enum.IsDefined(Algorithm))return "Unsupported matching algorithm.";
        if(Profiles!=null)foreach(var profile in Profiles)
        {
            if(!Enum.IsDefined(profile.Key))return "Unsupported matching profile.";
            if(MatchingParameters.Validate(profile.Value)is{}issue)return $"{profile.Key}: {issue}";
        }
        if(!Enabled)return null;
        if(TemplatePng.Length==0||Width<8||Height<8||LearnRoi==null||SearchRoi==null)return "Terminal template image and both ROIs are required.";
        var error=LearnRoi.Validate(Width,Height)??SearchRoi.Validate(frameWidth,frameHeight)??MatchingParameters.Validate(ActiveParameters());
        if(error!=null)return error;
        if(LearnRoi.Bounds.Width<8||LearnRoi.Bounds.Height<8||SearchRoi.Bounds.Width<8||SearchRoi.Bounds.Height<8)return "Matching ROIs must be at least 8 × 8 pixels.";
        // The saved dimensions must describe the actual immutable PNG, not stale metadata from another image.
        if(TemplatePng.Length<24||!TemplatePng.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(TemplatePng.AsSpan(16,4))!=Width||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(TemplatePng.AsSpan(20,4))!=Height)return "Invalid template PNG or mismatched dimensions.";
        return null;
    }
}
public sealed record TemplateMatchResult(bool Passed,string Reason,double Score,double Ncc,double Ssim,double Edge,
    double Angle,double Scale,int Matches,int Inliers,double InlierRatio,double Coverage,double ValidPixels,
    PixelPoint[] Corners,byte[] AlignedPng,byte[] TemplatePng,double Milliseconds,MatchingAlgorithm Algorithm=MatchingAlgorithm.Normal,
    MatchingDiagnostics? Diagnostics=null);

// Additive result evidence, not recipe state. Flags distinguish unmeasured values from measured zero.
public sealed record MatchingDiagnostics(int TemplateKeypoints,int SourceKeypoints,int RatioMatches,int DistanceMatches,
    bool HomographyEvaluated,bool PoseEvaluated,bool ValidPixelsEvaluated,bool NccEvaluated,bool AppearanceEvaluated,double[] Thresholds,
    double ScaleX=0,double ScaleY=0,string VerificationReason="NotEvaluated");

public static class CombinedInspectionComparer
{
    public static EndResult Combine(EndResult text,TemplateMatchResult? terminal,bool required)
    {
        if(!required)return text;
        if(terminal==null)throw new InvalidOperationException("Template matching result is missing.");
        return text with {Terminal=terminal,Verdict=text.Verdict==Verdict.Error?Verdict.Error:text.Verdict==Verdict.Ok&&terminal.Passed?Verdict.Ok:Verdict.Ng,
            Reason=text.Reason+"; Terminal: "+terminal.Reason};
    }
}
