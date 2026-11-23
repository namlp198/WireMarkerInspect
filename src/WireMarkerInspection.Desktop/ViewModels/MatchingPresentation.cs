using WireMarkerInspection.Controls.Localization;
using WireMarkerInspection.Domain;

namespace WireMarkerInspection.Desktop.ViewModels;

public sealed record InspectionCheck(string Text,bool? Passed);

public static class MatchingPresentation
{
    public static string Describe(TemplateMatchResult result)=>string.Join(Environment.NewLine,Checks(result).Select(c=>c.Text));
    public static IReadOnlyList<InspectionCheck> Checks(TemplateMatchResult r)
    {
        List<InspectionCheck> checks=[new(AppLocalizer.Text("TerminalTemplate")+" · "+(r.Passed?"OK":"NG")+" · "+
            AppLocalizer.Text($"MatchingAlgorithm{r.Algorithm}")+" · "+AppLocalizer.Text($"MatchReason{r.Reason}"),r.Passed)];
        var d=r.Diagnostics;
        // Old result files did not record stage availability or the thresholds. Do not invent them.
        if(d==null){checks.Add(new(AppLocalizer.Text("MatchingLegacyEvidence"),null));return checks;}
        double? Threshold(MatchParameter key)=>d.Thresholds.Length>(int)key?d.Thresholds[(int)key]:null;
        void Minimum(string label,double value,MatchParameter key,bool measured=true,string format="F3")
        {
            var threshold=Threshold(key);
            checks.Add(!measured?new(label+" · "+AppLocalizer.Text("MatchingNotEvaluated"),null):
                new($"{label} {value.ToString(format)}"+(threshold.HasValue?$" [≥ {threshold.Value.ToString(format)}]":""),threshold.HasValue?value>=threshold.Value:null));
        }
        if(r.Algorithm!=MatchingAlgorithm.Normal)
        {
            checks.Add(new(AppLocalizer.Format("MatchingFeatureCounts",d.TemplateKeypoints,d.SourceKeypoints,d.RatioMatches,d.DistanceMatches,r.Matches),null));
            Minimum("Match",r.Matches,MatchParameter.MinMatches,format:"F0");
            Minimum("Inlier",r.Inliers,MatchParameter.MinInliers,d.HomographyEvaluated,"F0");
            Minimum(AppLocalizer.Text("MatchParamInlierRatio"),r.InlierRatio,MatchParameter.InlierRatio,d.HomographyEvaluated,"P1");
            Minimum(AppLocalizer.Text("MatchParamCoverage"),r.Coverage,MatchParameter.Coverage,d.HomographyEvaluated,"P1");
        }
        if(d.VerificationReason is not ("Matched" or "NotEvaluated" or ""))
            checks.Add(new(AppLocalizer.Text("MatchingVerification")+" · "+AppLocalizer.Text($"MatchReason{d.VerificationReason}"),false));
        Minimum("NCC",r.Ncc,MatchParameter.Ncc,d.NccEvaluated);
        Minimum("SSIM",r.Ssim,MatchParameter.Ssim,d.AppearanceEvaluated);
        Minimum("Edge",r.Edge,MatchParameter.Edge,d.AppearanceEvaluated);
        Minimum("Score",r.Score,MatchParameter.Score,d.AppearanceEvaluated);
        if(d.PoseEvaluated)
        {
            var amin=Threshold(MatchParameter.AngleMin);var amax=Threshold(MatchParameter.AngleMax);
            var smin=Threshold(MatchParameter.ScaleMin);var smax=Threshold(MatchParameter.ScaleMax);
            checks.Add(new($"{AppLocalizer.Text("MatchingPose")} {r.Angle:F2}° [{amin}…{amax}]",
                amin.HasValue&&amax.HasValue?r.Angle>=amin-.001&&r.Angle<=amax+.001:null));
            checks.Add(new($"Scale X/Y {d.ScaleX:F3}/{d.ScaleY:F3} [{smin}…{smax}]",
                smin.HasValue&&smax.HasValue?d.ScaleX>=smin-.001&&d.ScaleX<=smax+.001&&d.ScaleY>=smin-.001&&d.ScaleY<=smax+.001:null));
        }
        else checks.Add(new(AppLocalizer.Text("MatchingPose")+" · "+AppLocalizer.Text("MatchingNotEvaluated"),null));
        Minimum(AppLocalizer.Text("MatchParamValidPixels"),r.ValidPixels,MatchParameter.ValidPixels,d.ValidPixelsEvaluated,"P1");
        checks.Add(new($"{r.Milliseconds:F1} ms",null));
        return checks;
    }
}
