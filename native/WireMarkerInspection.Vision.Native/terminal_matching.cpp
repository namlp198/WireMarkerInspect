#include "vision.h"
#include <opencv2/opencv.hpp>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <vector>

// Adapted from the matching pipeline inspected in PinInsertMachine/NVisionInspectCore.cpp.
// No reference-project dependency. Strict geometry, masked ZNCC/local SSIM/edge verification
// replace the permissive legacy confidence checks. All coordinates reported in source pixels.
namespace {
enum P {Score,Ncc,Ssim,Edge,AngleMin,AngleMax,AngleStep,ScaleMin,ScaleMax,ScaleStep,Ratio,MaxDistance,
    MinMatches,MinInliers,InlierRatio,Reprojection,Confidence,Iterations,Coverage,Keypoints,DetectorThreshold,
    Octaves,Layers,Contrast,EdgeThreshold,Sigma,PyramidScale,Levels,FastThreshold,PatchSize,Blur,ClaheClip,
    ClaheGrid,Resize,Ambiguity,ValidPixels,Distortion,FineAngle,FineScale,Method,ParameterCount};
char* payload(const std::string& text){auto p=new char[text.size()+1];std::memcpy(p,text.c_str(),text.size()+1);return p;}
std::string escape(const std::string& text){std::string s;for(auto c:text){if(c=='"'||c=='\\')s+='\\';if(c>=' ')s+=c;}return s;}
std::string png64(const cv::Mat& image){
    if(image.empty())return "";
    std::vector<uchar> bytes;cv::imencode(".png",image,bytes);
    const char* table="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";std::string s;
    for(size_t i=0;i<bytes.size();i+=3){unsigned n=(unsigned(bytes[i])<<16)|(i+1<bytes.size()?unsigned(bytes[i+1])<<8:0)|(i+2<bytes.size()?bytes[i+2]:0);
        s+=table[(n>>18)&63];s+=table[(n>>12)&63];s+=i+1<bytes.size()?table[(n>>6)&63]:'=';s+=i+2<bytes.size()?table[n&63]:'=';}return s;
}
struct Region {cv::Rect bounds;cv::Mat mask;};
Region region(cv::Size size,int shape,const double* xy,int count){
    if(!xy||count<2||count>4096||shape<0||shape>2||(shape!=2&&count!=2)||(shape==2&&count<3))throw std::invalid_argument("Invalid matching ROI.");
    std::vector<cv::Point> pts;double left=size.width,top=size.height,right=0,bottom=0;
    for(int i=0;i<count;i++){double x=xy[i*2],y=xy[i*2+1];if(!std::isfinite(x)||!std::isfinite(y)||x<0||y<0||x>size.width||y>size.height)throw std::invalid_argument("Matching ROI outside image.");
        pts.emplace_back(cvRound(x),cvRound(y));left=std::min(left,x);top=std::min(top,y);right=std::max(right,x);bottom=std::max(bottom,y);}
    double radius=0;
    if(shape==1){radius=cv::norm(pts[0]-pts[1]);left=pts[0].x-radius;right=pts[0].x+radius;top=pts[0].y-radius;bottom=pts[0].y+radius;}
    cv::Rect b(int(std::floor(left)),int(std::floor(top)),int(std::ceil(right)-std::floor(left)),int(std::ceil(bottom)-std::floor(top)));
    if(b.width<8||b.height<8||(b&cv::Rect(0,0,size.width,size.height))!=b)throw std::invalid_argument("Matching ROI must be inside the image and at least 8x8 pixels.");
    cv::Mat mask=cv::Mat::zeros(b.size(),CV_8U);
    if(shape==0)mask.setTo(255);
    else if(shape==1)cv::circle(mask,pts[0]-b.tl(),cvRound(radius),255,cv::FILLED);
    else{for(auto& p:pts)p-=b.tl();cv::fillPoly(mask,std::vector<std::vector<cv::Point>>{pts},255);}
    if(cv::countNonZero(mask)<64)throw std::invalid_argument("Template mask is too small.");
    return {b,mask};
}
cv::Mat preprocess(const cv::Mat& gray,const double* p){cv::Mat out=gray.clone();int blur=int(p[Blur]);if(blur>0)cv::GaussianBlur(out,out,{blur,blur},0);
    if(p[ClaheClip]>0)cv::createCLAHE(p[ClaheClip],{int(p[ClaheGrid]),int(p[ClaheGrid])})->apply(out,out);return out;}
struct Candidate{cv::Mat H;double raw=0;int matches=0,inliers=0;double ratio=0,coverage=0;
    int templateKeypoints=0,sourceKeypoints=0,ratioMatches=0,distanceMatches=0;
    bool homography=false;std::string failure;
};
struct Evidence{
    bool passed=false;std::string reason="NoCandidate";double score=0,ncc=0,ssim=0,edge=0,angle=0,scale=0,valid=0;
    Candidate candidate;std::vector<cv::Point2f> corners;cv::Mat aligned;
    bool poseEvaluated=false,validEvaluated=false,nccEvaluated=false,appearanceEvaluated=false;
    double scaleX=0,scaleY=0;std::string verificationReason;
};
double clamp(double x){return std::max(0.,std::min(1.,x));}
Evidence verify(const Candidate& c,const cv::Mat& src,const cv::Mat& tpl,const cv::Mat& sourceMask,const cv::Mat& templateMask,const double* p){
    Evidence e;e.candidate=c;
    if(c.H.empty()||!cv::checkRange(c.H)||std::abs(cv::determinant(c.H))<1e-10){e.reason="InvalidGeometry";return e;}
    std::vector<cv::Point2f> original={{0,0},{float(tpl.cols),0},{float(tpl.cols),float(tpl.rows)},{0,float(tpl.rows)}};
    cv::perspectiveTransform(original,e.corners,c.H);
    for(auto q:e.corners)if(!std::isfinite(q.x)||!std::isfinite(q.y)||std::abs(q.x)>1e6||std::abs(q.y)>1e6){e.corners.clear();e.reason="InvalidGeometry";return e;}
    if(!cv::isContourConvex(e.corners)||cv::contourArea(e.corners,true)<=0){e.reason="InvalidGeometry";return e;}
    auto x=e.corners[1]-e.corners[0],y=e.corners[3]-e.corners[0];
    double sx=cv::norm(x)/tpl.cols,sy=cv::norm(y)/tpl.rows;
    e.scaleX=sx;e.scaleY=sy;
    e.angle=std::atan2(x.y,x.x)*180/CV_PI;e.scale=std::sqrt(sx*sy);
    e.poseEvaluated=true;
    double skew=std::abs(x.dot(y))/std::max(1e-9,cv::norm(x)*cv::norm(y));
    double oppositeX=cv::norm(e.corners[2]-e.corners[3])/std::max(1e-9,cv::norm(x));
    double oppositeY=cv::norm(e.corners[2]-e.corners[1])/std::max(1e-9,cv::norm(y));
    if(e.angle<p[AngleMin]-.001||e.angle>p[AngleMax]+.001){e.reason="AngleOutOfRange";return e;}
    if(sx<p[ScaleMin]-.001||sx>p[ScaleMax]+.001||sy<p[ScaleMin]-.001||sy>p[ScaleMax]+.001){e.reason="ScaleOutOfRange";return e;}
    if(skew>p[Distortion]||std::abs(sx/sy-1)>p[Distortion]||std::abs(oppositeX-1)>p[Distortion]||std::abs(oppositeY-1)>p[Distortion]){e.reason="ExcessiveDistortion";return e;}
    cv::Mat inverse=c.H.inv(),valid;
    cv::warpPerspective(src,e.aligned,inverse,tpl.size(),cv::INTER_LINEAR,cv::BORDER_CONSTANT,0);
    cv::warpPerspective(sourceMask,valid,inverse,tpl.size(),cv::INTER_NEAREST,cv::BORDER_CONSTANT,0);
    cv::bitwise_and(valid,templateMask,valid);e.valid=double(cv::countNonZero(valid))/cv::countNonZero(templateMask);
    e.validEvaluated=true;
    if(e.valid<p[ValidPixels]){e.reason="OutsideSearchArea";return e;}
    cv::Mat a,b;tpl.convertTo(a,CV_32F);e.aligned.convertTo(b,CV_32F);
    cv::Scalar ma,sa,mb,sb;cv::meanStdDev(a,ma,sa,valid);cv::meanStdDev(b,mb,sb,valid);
    if(sa[0]<2||sb[0]<2){e.reason="InsufficientTexture";return e;}
    e.ncc=clamp(cv::mean((a-ma[0]).mul(b-mb[0]),valid)[0]/(sa[0]*sb[0]));
    e.nccEvaluated=true;
    cv::Mat mua,mub,aa,bb,ab;
    cv::GaussianBlur(a,mua,{11,11},1.5);cv::GaussianBlur(b,mub,{11,11},1.5);
    cv::GaussianBlur(a.mul(a),aa,{11,11},1.5);aa-=mua.mul(mua);
    cv::GaussianBlur(b.mul(b),bb,{11,11},1.5);bb-=mub.mul(mub);
    cv::GaussianBlur(a.mul(b),ab,{11,11},1.5);ab-=mua.mul(mub);
    cv::Mat numerator=(2*mua.mul(mub)+6.5025).mul(2*ab+58.5225);
    cv::Mat denominator=(mua.mul(mua)+mub.mul(mub)+6.5025).mul(aa+bb+58.5225),ssimMap,inner;
    cv::divide(numerator,denominator,ssimMap);cv::erode(valid,inner,cv::Mat::ones(11,11,CV_8U));
    if(cv::countNonZero(inner)<16){e.reason="InsufficientValidArea";return e;}
    e.ssim=clamp(cv::mean(ssimMap,inner)[0]);
    cv::Mat ea,eb,da,db;cv::Canny(tpl,ea,50,150);cv::Canny(e.aligned,eb,50,150);
    cv::bitwise_and(ea,inner,ea);cv::bitwise_and(eb,inner,eb);
    cv::dilate(ea,da,cv::Mat::ones(3,3,CV_8U));cv::dilate(eb,db,cv::Mat::ones(3,3,CV_8U));
    double n1=cv::countNonZero(ea),n2=cv::countNonZero(eb);cv::bitwise_and(ea,db,da);
    cv::Mat dilatedA;cv::dilate(ea,dilatedA,cv::Mat::ones(3,3,CV_8U));cv::bitwise_and(eb,dilatedA,db);
    e.edge=n1>0&&n2>0?.5*(cv::countNonZero(da)/n1+cv::countNonZero(db)/n2):0;
    e.score=std::min({e.ncc,e.ssim,e.edge});
    e.appearanceEvaluated=true;
    e.reason=e.ncc<p[Ncc]?"NccBelowThreshold":e.ssim<p[Ssim]?"SsimBelowThreshold":e.edge<p[Edge]?"EdgeBelowThreshold":e.score<p[Score]?"ScoreBelowThreshold":"Matched";
    e.passed=e.reason=="Matched";return e;
}
std::vector<Candidate> featureCandidates(const cv::Mat& tpl,const cv::Mat& src,const cv::Mat& tm,const cv::Mat& sm,int algo,const double* p){
    double scale=p[Resize];cv::Mat t,s,mt,ms;
    cv::resize(tpl,t,{},scale,scale);cv::resize(src,s,{},scale,scale);cv::resize(tm,mt,t.size(),0,0,cv::INTER_NEAREST);cv::resize(sm,ms,s.size(),0,0,cv::INTER_NEAREST);
    cv::Ptr<cv::Feature2D> detector;
    if(algo==1)detector=cv::AKAZE::create(cv::AKAZE::DESCRIPTOR_MLDB,0,3,float(p[DetectorThreshold]),int(p[Octaves]),int(p[Layers]));
    else if(algo==2)detector=cv::SIFT::create(int(p[Keypoints]),int(p[Layers]),p[Contrast],p[EdgeThreshold],p[Sigma]);
    else detector=cv::ORB::create(int(p[Keypoints]),float(p[PyramidScale]),int(p[Levels]),int(p[EdgeThreshold]),0,2,cv::ORB::HARRIS_SCORE,int(p[PatchSize]),int(p[FastThreshold]));
    std::vector<cv::KeyPoint> kt;cv::Mat dt;detector->detectAndCompute(t,mt,kt,dt);
    std::vector<Candidate> candidates;
    Candidate diagnostic;diagnostic.templateKeypoints=int(kt.size());
    if(dt.empty()){diagnostic.failure="NoTemplateFeatures";return {diagnostic};}
    for(int attempt=0;attempt<2;attempt++){
        Candidate c;c.templateKeypoints=int(kt.size());
        std::vector<cv::KeyPoint> ks;cv::Mat ds;detector->detectAndCompute(s,ms,ks,ds);c.sourceKeypoints=int(ks.size());
        if(ds.rows<2){c.failure="NoSourceFeatures";if(candidates.empty())candidates.push_back(c);break;}
        cv::BFMatcher matcher(algo==2?cv::NORM_L2:cv::NORM_HAMMING);std::vector<std::vector<cv::DMatch>> pairs;std::vector<cv::DMatch> reverse;
        matcher.knnMatch(dt,ds,pairs,2);matcher.match(ds,dt,reverse);std::vector<cv::Point2f> a,b;
        for(auto& k:pairs){
            if(k.size()!=2||k[0].distance>=p[Ratio]*k[1].distance)continue;++c.ratioMatches;
            if(k[0].distance>p[MaxDistance])continue;++c.distanceMatches;
            if(reverse[k[0].trainIdx].trainIdx!=k[0].queryIdx)continue;
            a.push_back(kt[k[0].queryIdx].pt);b.push_back(ks[k[0].trainIdx].pt);}
        c.matches=int(a.size());
        if(a.size()<p[MinMatches])c.failure="InsufficientMatches";
        // Four correspondences are the mathematical minimum. Below the recipe gates, a
        // transform is diagnostic only: it must NEVER turn a rejected candidate into OK.
        if(a.size()<4){if(candidates.empty())candidates.push_back(c);break;}
        cv::Mat inlier;auto h=cv::findHomography(a,b,cv::RANSAC,p[Reprojection]*scale,inlier,int(p[Iterations]),p[Confidence]);
        if(h.empty()){if(c.failure.empty())c.failure="HomographyNotFound";if(candidates.empty())candidates.push_back(c);break;}
        c.homography=true;c.inliers=cv::countNonZero(inlier);c.ratio=double(c.inliers)/a.size();
        std::vector<cv::Point2f> points,hull;for(int i=0;i<int(a.size());i++)if(inlier.at<uchar>(i))points.push_back(a[i]);
        cv::convexHull(points,hull);c.coverage=std::abs(cv::contourArea(hull))/std::max(1,cv::countNonZero(mt));
        if(c.failure.empty())c.failure=c.inliers<p[MinInliers]?"InsufficientInliers":c.ratio<p[InlierRatio]?"LowInlierRatio":c.coverage<p[Coverage]?"LowFeatureCoverage":"";
        cv::Mat st=cv::Mat::eye(3,3,CV_64F);st.at<double>(0,0)=double(t.cols)/tpl.cols;st.at<double>(1,1)=double(t.rows)/tpl.rows;
        cv::Mat ss=cv::Mat::eye(3,3,CV_64F);ss.at<double>(0,0)=double(s.cols)/src.cols;ss.at<double>(1,1)=double(s.rows)/src.rows;
        c.H=ss.inv()*h*st;c.raw=c.ratio;
        // Keep failed first-attempt evidence, but a failed second attempt is not a
        // competing instance and must not displace a valid first candidate.
        if(!c.failure.empty()){if(candidates.empty())candidates.push_back(c);break;}
        candidates.push_back(c);
        std::vector<cv::Point2f> corners={{0,0},{float(t.cols),0},{float(t.cols),float(t.rows)},{0,float(t.rows)}};cv::perspectiveTransform(corners,corners,h);
        std::vector<cv::Point> polygon;for(auto q:corners){if(!std::isfinite(q.x)||!std::isfinite(q.y)||std::abs(q.x)>1e6||std::abs(q.y)>1e6)return candidates;polygon.emplace_back(cvRound(q.x),cvRound(q.y));}
        cv::fillConvexPoly(ms,polygon,0);
    }return candidates;
}
std::vector<Candidate> normalCandidates(const cv::Mat& tpl,const cv::Mat& src,const cv::Mat& tm,const cv::Mat& sm,const double* p){
    std::vector<Candidate> candidates;
    auto sweep=[&](double amin,double amax,double astep,double smin,double smax,double sstep){
        for(double angle=amin;angle<=amax+1e-8;angle+=astep)for(double scale=smin;scale<=smax+1e-8;scale+=sstep){
            // OpenCV's positive rotation is opposite to the source-coordinate atan2 convention.
            cv::Mat affine=cv::getRotationMatrix2D({0,0},-angle,scale),h=cv::Mat::eye(3,3,CV_64F);affine.copyTo(h(cv::Rect(0,0,3,2)));
            std::vector<cv::Point2f> points={{0,0},{float(tpl.cols),0},{float(tpl.cols),float(tpl.rows)},{0,float(tpl.rows)}};cv::perspectiveTransform(points,points,h);
            double left=points[0].x,top=points[0].y,right=left,bottom=top;
            for(auto point:points){left=std::min(left,double(point.x));top=std::min(top,double(point.y));right=std::max(right,double(point.x));bottom=std::max(bottom,double(point.y));}
            cv::Rect bounds(int(std::floor(left)),int(std::floor(top)),int(std::ceil(right)-std::floor(left)),int(std::ceil(bottom)-std::floor(top)));
            h.at<double>(0,2)-=bounds.x;h.at<double>(1,2)-=bounds.y;
            if(bounds.width>src.cols||bounds.height>src.rows||bounds.width<8||bounds.height<8)continue;
            cv::Mat rotated,mask,response;cv::warpPerspective(tpl,rotated,h,bounds.size(),cv::INTER_LINEAR,cv::BORDER_CONSTANT,0);
            cv::warpPerspective(tm,mask,h,bounds.size(),cv::INTER_NEAREST,cv::BORDER_CONSTANT,0);
            int method=p[Method]==1?cv::TM_CCORR_NORMED:p[Method]==2?cv::TM_SQDIFF_NORMED:cv::TM_CCOEFF_NORMED;
            cv::matchTemplate(src,rotated,response,method,mask);
            // Excluded source pixels must not nominate peaks ahead of valid matches inside a polygon/circle.
            if(cv::countNonZero(sm)!=sm.rows*sm.cols){
                cv::Mat overlap;cv::matchTemplate(sm,mask,overlap,cv::TM_CCORR);
                double minimum=cv::countNonZero(mask)*65025.*p[ValidPixels];
                response.setTo(method==cv::TM_SQDIFF_NORMED?1:-1,overlap<minimum);
            }
            for(int y=0;y<response.rows;y++)for(int x=0;x<response.cols;x++)if(!std::isfinite(response.at<float>(y,x)))response.at<float>(y,x)=method==cv::TM_SQDIFF_NORMED?1.f:-1.f;
            for(int k=0;k<2;k++){
                double mn,mx;cv::Point lo,hi;cv::minMaxLoc(response,&mn,&mx,&lo,&hi);auto location=method==cv::TM_SQDIFF_NORMED?lo:hi;double score=method==cv::TM_SQDIFF_NORMED?1-mn:mx;
                auto placed=h.clone();placed.at<double>(0,2)+=location.x;placed.at<double>(1,2)+=location.y;candidates.push_back({placed,score});
                cv::Rect suppression(location.x-bounds.width/2,location.y-bounds.height/2,bounds.width,bounds.height);
                suppression&=cv::Rect(0,0,response.cols,response.rows);response(suppression).setTo(method==cv::TM_SQDIFF_NORMED?1:-1);
            }
        }
    };
    sweep(p[AngleMin],p[AngleMax],p[AngleStep],p[ScaleMin],p[ScaleMax],p[ScaleStep]);
    if(candidates.empty())return candidates;
    auto best=*std::max_element(candidates.begin(),candidates.end(),[](auto& a,auto& b){return a.raw<b.raw;});
    double a=std::atan2(best.H.at<double>(1,0),best.H.at<double>(0,0))*180/CV_PI;
    double s=std::hypot(best.H.at<double>(0,0),best.H.at<double>(1,0));
    sweep(std::max(p[AngleMin],a-p[AngleStep]),std::min(p[AngleMax],a+p[AngleStep]),p[FineAngle],
        std::max(p[ScaleMin],s-p[ScaleStep]),std::min(p[ScaleMax],s+p[ScaleStep]),p[FineScale]);
    std::sort(candidates.begin(),candidates.end(),[](auto& a,auto& b){return a.raw>b.raw;});
    std::vector<Candidate> unique;
    for(auto& c:candidates){bool duplicate=false;for(auto& q:unique)if(std::hypot(c.H.at<double>(0,2)-q.H.at<double>(0,2),c.H.at<double>(1,2)-q.H.at<double>(1,2))<std::min(tpl.cols,tpl.rows)*.35){duplicate=true;break;}
        if(!duplicate)unique.push_back(c);if(unique.size()>=8)break;}
    return unique;
}
}
int __cdecl wmi_matching_abi_version(){return 1;}
char* __cdecl wmi_match(const uint8_t* bgr,int width,int height,int stride,const uint8_t* bytes,int length,
    int learnShape,const double* learnXY,int learnCount,int searchShape,const double* searchXY,int searchCount,int algorithm,const double* p,int parameterCount){
    try{
        auto start=std::chrono::steady_clock::now();
        if(!bgr||width<1||height<1||width>30000||height>30000||stride<int64_t(width)*3||!bytes||length<1||!p||parameterCount!=ParameterCount||algorithm<0||algorithm>4)throw std::invalid_argument("Invalid matching arguments.");
        for(int i=0;i<ParameterCount;i++)if(!std::isfinite(p[i]))throw std::invalid_argument("Nonfinite matching parameter.");
        if(p[AngleStep]<=0||p[ScaleStep]<=0||p[FineAngle]<=0||p[FineScale]<=0||p[ScaleMin]<=0||p[Resize]<=0||p[Resize]>1||p[Iterations]<1||p[Confidence]<=0||p[Confidence]>=1||p[AngleMin]>p[AngleMax]||p[ScaleMin]>p[ScaleMax])throw std::invalid_argument("Invalid matching range.");
        double count=(1+(p[AngleMax]-p[AngleMin])/p[AngleStep])*(1+(p[ScaleMax]-p[ScaleMin])/p[ScaleStep]);
        if(count>10000||(1+2*p[AngleStep]/p[FineAngle])*(1+2*p[ScaleStep]/p[FineScale])>10000)throw std::invalid_argument("Matching search is too large.");
        cv::Mat fullTemplate=cv::imdecode(cv::Mat(1,length,CV_8U,const_cast<uint8_t*>(bytes)),cv::IMREAD_GRAYSCALE);
        if(fullTemplate.empty())throw std::invalid_argument("Invalid template PNG.");
        auto tr=region(fullTemplate.size(),learnShape,learnXY,learnCount),sr=region({width,height},searchShape,searchXY,searchCount);
        cv::Mat color(height,width,CV_8UC3,const_cast<uint8_t*>(bgr),stride),fullGray;cv::cvtColor(color,fullGray,cv::COLOR_BGR2GRAY);
        cv::Mat tpl=fullTemplate(tr.bounds).clone(),src=fullGray(sr.bounds).clone();cv::Scalar mean,stddev;cv::meanStdDev(tpl,mean,stddev,tr.mask);
        if(stddev[0]<2)throw std::invalid_argument("Template has insufficient texture.");
        auto t=preprocess(tpl,p),s=preprocess(src,p);
        auto candidates=algorithm==0?normalCandidates(t,s,tr.mask,sr.mask,p):featureCandidates(t,s,tr.mask,sr.mask,algorithm,p);
        std::vector<Evidence> evidence;for(auto& c:candidates){
            auto e=verify(c,src,tpl,sr.mask,tr.mask,p);
            e.verificationReason=c.H.empty()?"NotEvaluated":e.reason;
            if(!c.failure.empty()){e.passed=false;e.reason=c.failure;}
            evidence.push_back(e);
        }
        std::stable_sort(evidence.begin(),evidence.end(),[](auto& a,auto& b){return a.score>b.score;});
        Evidence best;if(!evidence.empty())best=evidence.front();
        if(best.passed&&evidence.size()>1&&evidence[1].score>0&&best.score-evidence[1].score<p[Ambiguity]){best.passed=false;best.reason="AmbiguousMatch";}
        double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
        std::ostringstream out;out.imbue(std::locale::classic());out<<std::setprecision(10);
        out<<"{\"passed\":"<<(best.passed?"true":"false")<<",\"reason\":\""<<best.reason<<"\",\"score\":"<<best.score<<",\"ncc\":"<<best.ncc<<",\"ssim\":"<<best.ssim<<",\"edge\":"<<best.edge
            <<",\"angle\":"<<best.angle<<",\"scale\":"<<best.scale<<",\"matches\":"<<best.candidate.matches<<",\"inliers\":"<<best.candidate.inliers<<",\"inlierRatio\":"<<best.candidate.ratio<<",\"coverage\":"<<best.candidate.coverage<<",\"validPixels\":"<<best.valid<<",\"corners\":[";
        for(size_t i=0;i<best.corners.size();i++){if(i)out<<',';out<<"{\"x\":"<<best.corners[i].x+sr.bounds.x<<",\"y\":"<<best.corners[i].y+sr.bounds.y<<'}';}
        out<<"],\"alignedPng\":\""<<png64(best.aligned)<<"\",\"templatePng\":\""<<png64(tpl)<<"\",\"milliseconds\":"<<ms<<",\"algorithm\":"<<algorithm;
        auto& c=best.candidate;
        out<<",\"diagnostics\":{\"templateKeypoints\":"<<c.templateKeypoints<<",\"sourceKeypoints\":"<<c.sourceKeypoints
            <<",\"ratioMatches\":"<<c.ratioMatches<<",\"distanceMatches\":"<<c.distanceMatches
            <<",\"homographyEvaluated\":"<<(c.homography?"true":"false")
            <<",\"poseEvaluated\":"<<(best.poseEvaluated?"true":"false")<<",\"validPixelsEvaluated\":"<<(best.validEvaluated?"true":"false")
            <<",\"nccEvaluated\":"<<(best.nccEvaluated?"true":"false")<<",\"appearanceEvaluated\":"<<(best.appearanceEvaluated?"true":"false")
            <<",\"scaleX\":"<<best.scaleX<<",\"scaleY\":"<<best.scaleY<<",\"verificationReason\":\""<<best.verificationReason<<"\""
            <<",\"thresholds\":[";
        for(int i=0;i<ParameterCount;i++){if(i)out<<',';out<<p[i];}
        out<<"]}}";return payload(out.str());
    }catch(const std::exception& ex){return payload("{\"error\":\""+escape(ex.what())+"\"}");}catch(...){return payload("{\"error\":\"Unknown matching error\"}");}
}
