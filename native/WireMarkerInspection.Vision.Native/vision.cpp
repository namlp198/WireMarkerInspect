#include "vision.h"
#include <opencv2/opencv.hpp>
#include <onnxruntime_cxx_api.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
Ort::Env& environment() { static Ort::Env env(ORT_LOGGING_LEVEL_WARNING, "WireMarkerVision"); return env; }
Ort::SessionOptions options() {
    Ort::SessionOptions o; o.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);
    o.SetIntraOpNumThreads(2); return o;
}
struct Engine {
    Ort::Session detector;
    Ort::Session recognizer;
    std::vector<std::string> dictionary;
    std::mutex mutex;
    Engine(const wchar_t* det, const wchar_t* rec, const wchar_t* dict)
      : detector(environment(), det, options()), recognizer(environment(), rec, options()) {
        std::ifstream input{std::filesystem::path(dict)};
        if (!input) throw std::runtime_error("Cannot open OCR dictionary.");
        std::string line;
        while(std::getline(input, line)) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (dictionary.empty() && line.rfind("\xEF\xBB\xBF",0)==0) line.erase(0,3);
            dictionary.push_back(line);
        }
        if(dictionary.empty()) throw std::runtime_error("OCR dictionary is empty.");
    }
};
char* allocate(const std::string& s) {
    auto* p = new char[s.size()+1]; std::memcpy(p,s.c_str(),s.size()+1); return p;
}
std::string quoted(const std::string& s) {
    std::ostringstream o; o << '"';
    for(unsigned char c:s) {
        if(c=='"' || c=='\\') o << '\\' << c;
        else if(c < 32) o << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(c) << std::dec;
        else o << c;
    }
    o << '"'; return o.str();
}
char* error(const std::string& message) { return allocate("{\"error\":"+quoted(message)+"}"); }
std::string base64(const std::vector<uchar>& bytes) {
    static const char* table="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string result; result.reserve((bytes.size()+2)/3*4);
    for(size_t i=0;i<bytes.size();i+=3) {
        const unsigned n=(static_cast<unsigned>(bytes[i])<<16) |
            (i+1<bytes.size()? static_cast<unsigned>(bytes[i+1])<<8:0) | (i+2<bytes.size()?bytes[i+2]:0);
        result += table[(n>>18)&63]; result += table[(n>>12)&63];
        result += i+1<bytes.size()?table[(n>>6)&63]:'='; result += i+2<bytes.size()?table[n&63]:'=';
    }
    return result;
}
std::string png(const cv::Mat& m) { std::vector<uchar> data; cv::imencode(".png",m,data); return base64(data); }
struct Crop { cv::Mat image; cv::Rect bounds; };
Crop crop(const uint8_t* bgr,int width,int height,int stride,int shape,const double* xy,int count) {
    if(!bgr || !xy || width<=0 || height<=0 || width>30000 || height>30000 || stride<static_cast<int64_t>(width)*3 ||
        count<2 || count>4096 || shape<0 || shape>2 || (shape!=2 && count!=2) || (shape==2 && count<3))
        throw std::invalid_argument("Invalid image or ROI arguments.");
    std::vector<cv::Point2d> pts;
    for(int i=0;i<count;i++) {
        if(!std::isfinite(xy[2*i]) || !std::isfinite(xy[2*i+1])) throw std::invalid_argument("Invalid ROI coordinate.");
        pts.emplace_back(xy[2*i],xy[2*i+1]);
    }
    double left=width,top=height,right=0,bottom=0,radius=0;
    for(const auto& p:pts) { left=std::min(left,p.x); top=std::min(top,p.y); right=std::max(right,p.x); bottom=std::max(bottom,p.y); }
    if(shape==1) {
        radius=cv::norm(pts[1]-pts[0]); left=pts[0].x-radius; right=pts[0].x+radius; top=pts[0].y-radius; bottom=pts[0].y+radius;
    }
    if(left<0 || top<0 || right>width+0.001 || bottom>height+0.001 || right-left<2 || bottom-top<2)
        throw std::invalid_argument("ROI is empty or outside the image.");
    cv::Rect bounds(static_cast<int>(std::floor(left)),static_cast<int>(std::floor(top)),
        static_cast<int>(std::ceil(right))-static_cast<int>(std::floor(left)),
        static_cast<int>(std::ceil(bottom))-static_cast<int>(std::floor(top)));
    bounds &= cv::Rect(0,0,width,height);
    cv::Mat image(height,width,CV_8UC3,const_cast<uint8_t*>(bgr),static_cast<size_t>(stride));
    cv::Mat result=image(bounds).clone();
    if(shape!=0) {
        cv::Mat mask(bounds.size(),CV_8U,cv::Scalar(0));
        if(shape==1) cv::circle(mask,cv::Point(cvRound(pts[0].x-bounds.x),cvRound(pts[0].y-bounds.y)),
            cvRound(radius),cv::Scalar(255),cv::FILLED);
        else {
            std::vector<cv::Point> poly;
            for(auto p:pts) poly.emplace_back(cvRound(p.x-bounds.x),cvRound(p.y-bounds.y));
            if(std::abs(cv::contourArea(poly))<2) throw std::invalid_argument("Polygon area is too small.");
            cv::fillPoly(mask,std::vector<std::vector<cv::Point>>{poly},cv::Scalar(255));
        }
        result.setTo(cv::Scalar(255,255,255),mask==0);
    }
    return {result,bounds};
}
std::vector<float> chw(const cv::Mat& image) {
    std::vector<cv::Mat> channels; cv::split(image,channels);
    std::vector<float> result; result.reserve(image.total()*3);
    for(auto& c:channels) result.insert(result.end(),c.ptr<float>(),c.ptr<float>()+c.total());
    return result;
}
Ort::Value run(Ort::Session& session,std::vector<float>& input,const std::array<int64_t,4>& shape) {
    Ort::AllocatorWithDefaultOptions allocator;
    auto in=session.GetInputNameAllocated(0,allocator); auto out=session.GetOutputNameAllocated(0,allocator);
    const char* inputNames[]={in.get()}; const char* outputNames[]={out.get()};
    auto memory=Ort::MemoryInfo::CreateCpu(OrtArenaAllocator,OrtMemTypeDefault);
    auto tensor=Ort::Value::CreateTensor<float>(memory,input.data(),input.size(),shape.data(),shape.size());
    auto output=session.Run(Ort::RunOptions{nullptr},inputNames,&tensor,1,outputNames,1);
    return std::move(output.front());
}
std::vector<cv::RotatedRect> detect(Engine& engine,const cv::Mat& image) {
    const double scale=std::min(1.0,960.0/std::max(image.cols,image.rows));
    const int w=std::max(1,cvRound(image.cols*scale)),h=std::max(1,cvRound(image.rows*scale));
    const int pw=(w+31)/32*32,ph=(h+31)/32*32;
    cv::Mat rgb; cv::cvtColor(image,rgb,cv::COLOR_BGR2RGB); cv::resize(rgb,rgb,{w,h});
    cv::copyMakeBorder(rgb,rgb,0,ph-h,0,pw-w,cv::BORDER_CONSTANT,cv::Scalar(0,0,0));
    rgb.convertTo(rgb,CV_32FC3,1.0/255);
    std::vector<cv::Mat> channels; cv::split(rgb,channels);
    channels[0]=(channels[0]-0.485)/0.229; channels[1]=(channels[1]-0.456)/0.224; channels[2]=(channels[2]-0.406)/0.225;
    cv::merge(channels,rgb);
    auto input=chw(rgb); auto output=run(engine.detector,input,{1,3,ph,pw});
    auto dims=output.GetTensorTypeAndShapeInfo().GetShape();
    if(dims.size()!=4 || dims[0]!=1 || dims[1]!=1 || dims[2]<=0 || dims[3]<=0)
        throw std::runtime_error("Detector must return [1,1,H,W] probabilities.");
    cv::Mat probabilities(static_cast<int>(dims[2]),static_cast<int>(dims[3]),CV_32F,output.GetTensorMutableData<float>());
    cv::Mat binary; cv::threshold(probabilities,binary,0.3,255,cv::THRESH_BINARY); binary.convertTo(binary,CV_8U);
    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(binary,contours,cv::RETR_EXTERNAL,cv::CHAIN_APPROX_SIMPLE);
    std::vector<cv::RotatedRect> boxes;
    for(auto& contour:contours) {
        if(cv::contourArea(contour)<9) continue;
        cv::Mat mask(probabilities.size(),CV_8U,cv::Scalar(0));
        cv::fillPoly(mask,std::vector<std::vector<cv::Point>>{contour},cv::Scalar(255));
        if(cv::mean(probabilities,mask)[0]<0.5) continue;
        std::vector<cv::Point2f> mapped;
        for(auto p:contour) mapped.emplace_back(static_cast<float>(p.x*pw/(probabilities.cols*scale)),
            static_cast<float>(p.y*ph/(probabilities.rows*scale)));
        auto box=cv::minAreaRect(mapped);
        if(box.center.x>=image.cols || box.center.y>=image.rows || std::min(box.size.width,box.size.height)<3) continue;
        if(box.size.width>=box.size.height) { box.size.width*=1.1f; box.size.height*=1.6f; }
        else { box.size.width*=1.6f; box.size.height*=1.1f; }
        boxes.push_back(box);
    }
    // Row ordering is independent of recognition score or expected text.
    std::sort(boxes.begin(),boxes.end(),[](const auto& a,const auto& b){
        return a.center.y==b.center.y ? a.center.x<b.center.x : a.center.y<b.center.y;
    });
    for(size_t start=0;start<boxes.size();) {
        size_t end=start+1;
        const auto first=boxes[start].boundingRect2f();
        while(end<boxes.size() && std::abs(boxes[end].center.y-boxes[start].center.y)<first.height*0.5f) ++end;
        std::sort(boxes.begin()+start,boxes.begin()+end,[](const auto& a,const auto& b){return a.center.x<b.center.x;});
        start=end;
    }
    return boxes;
}
std::array<cv::Point2f,4> corners(cv::RotatedRect rect) {
    cv::Point2f pts[4]; rect.points(pts);
    std::sort(pts,pts+4,[](auto a,auto b){return a.y==b.y?a.x<b.x:a.y<b.y;});
    if(pts[0].x>pts[1].x) std::swap(pts[0],pts[1]);
    if(pts[2].x>pts[3].x) std::swap(pts[2],pts[3]);
    return {pts[0],pts[1],pts[3],pts[2]};
}
cv::Mat rectify(const cv::Mat& image,const std::array<cv::Point2f,4>& points) {
    int w=std::max(2,cvRound(std::max(cv::norm(points[0]-points[1]),cv::norm(points[3]-points[2]))));
    int h=std::max(2,cvRound(std::max(cv::norm(points[0]-points[3]),cv::norm(points[1]-points[2]))));
    std::array<cv::Point2f,4> target={cv::Point2f(0,0),cv::Point2f(static_cast<float>(w-1),0),
        cv::Point2f(static_cast<float>(w-1),static_cast<float>(h-1)),cv::Point2f(0,static_cast<float>(h-1))};
    cv::Mat result; cv::warpPerspective(image,result,cv::getPerspectiveTransform(points.data(),target.data()),
        {w,h},cv::INTER_CUBIC,cv::BORDER_CONSTANT,cv::Scalar(255,255,255)); return result;
}
std::pair<std::string,double> recognize(Engine& engine,const cv::Mat& image) {
    constexpr int h=48,w=320;
    cv::Mat rgb; cv::cvtColor(image,rgb,cv::COLOR_BGR2RGB);
    int actual=std::clamp(cvRound(static_cast<double>(h)*rgb.cols/rgb.rows),1,w);
    cv::resize(rgb,rgb,{actual,h},0,0,cv::INTER_CUBIC);
    cv::Mat padded(h,w,CV_8UC3,cv::Scalar(255,255,255)); rgb.copyTo(padded(cv::Rect(0,0,actual,h)));
    padded.convertTo(padded,CV_32FC3,1.0/127.5,-1);
    auto input=chw(padded); auto output=run(engine.recognizer,input,{1,3,h,w});
    const auto dims=output.GetTensorTypeAndShapeInfo().GetShape();
    if(dims.size()!=3 || dims[0]!=1 || dims[1]<1 || dims[2]<2) throw std::runtime_error("Recognizer must return [1,T,C].");
    const int sequence=static_cast<int>(dims[1]),classes=static_cast<int>(dims[2]);
    if(classes!=static_cast<int>(engine.dictionary.size())+1 &&
       classes!=static_cast<int>(engine.dictionary.size())+2) throw std::runtime_error("Dictionary and recognizer class counts differ.");
    const float* values=output.GetTensorData<float>();
    int previous=-1,count=0; double confidence=0; std::string text;
    for(int t=0;t<sequence;t++) {
        const float* row=values+static_cast<size_t>(t)*classes;
        const int best=static_cast<int>(std::max_element(row,row+classes)-row);
        if(best>0 && best!=previous) {
            if(!std::isfinite(row[best]) || row[best]<0 || row[best]>1.0001) throw std::runtime_error("Expected CTC probabilities, not logits.");
            text += best-1<static_cast<int>(engine.dictionary.size())?engine.dictionary[best-1]:" ";
            confidence += row[best]; ++count;
        }
        previous=best;
    }
    return {text,count?confidence/count:0};
}
struct Region { std::string text; double confidence; std::array<cv::Point2f,4> box; cv::Mat crop; };
std::vector<Region> read(Engine& e,const cv::Mat& image) {
    std::vector<Region> regions;
    for(auto box:detect(e,image)) {
        auto pts=corners(box); auto patch=rectify(image,pts); auto text=recognize(e,patch);
        regions.push_back({text.first,text.second,pts,patch});
    }
    return regions;
}
double quality(const std::vector<Region>& regions) {
    if(regions.empty()) return -1;
    double sum=0; for(auto& r:regions) sum+=r.text.empty()?0:r.confidence; return sum/regions.size();
}
}
int __cdecl wmi_abi_version() { return 1; }
void* __cdecl wmi_create(const wchar_t* det,const wchar_t* rec,const wchar_t* dict,char** message) {
    if(message) *message=nullptr;
    try {
        if(!det || !rec || !dict) throw std::invalid_argument("Missing model paths.");
        return new Engine(det,rec,dict);
    } catch(const std::exception& ex) { if(message) *message=allocate(ex.what()); return nullptr; }
    catch(...) { if(message) *message=allocate("Unknown native initialization failure."); return nullptr; }
}
void __cdecl wmi_destroy(void* engine) { delete static_cast<Engine*>(engine); }
void __cdecl wmi_free(char* p) { delete[] p; }
char* __cdecl wmi_crop(const uint8_t* bgr,int w,int h,int stride,int shape,const double* xy,int count) {
    try { auto c=crop(bgr,w,h,stride,shape,xy,count); return allocate("{\"cropPng\":"+quoted(png(c.image))+"}"); }
    catch(const std::exception& ex) { return error(ex.what()); } catch(...) {return error("Unknown native crop failure.");}
}
char* __cdecl wmi_inspect(void* handle,const uint8_t* bgr,int w,int h,int stride,int shape,const double* xy,int count,int orientation) {
    try {
        if(!handle) throw std::invalid_argument("OCR engine is not initialized.");
        if(orientation<0 || orientation>2) throw std::invalid_argument("Invalid orientation.");
        auto& e=*static_cast<Engine*>(handle); std::lock_guard<std::mutex> lock(e.mutex);
        auto c=crop(bgr,w,h,stride,shape,xy,count); int rotation=0; std::vector<Region> regions;
        if(orientation!=1) regions=read(e,c.image);
        if(orientation!=0) {
            cv::Mat turned; cv::rotate(c.image,turned,cv::ROTATE_180);
            auto alternate=read(e,turned);
            if(orientation==1 || quality(alternate)>quality(regions)+0.01) {regions=std::move(alternate);rotation=180;}
        }
        std::ostringstream output; output.imbue(std::locale::classic());
        output << "{\"rotation\":" << rotation << ",\"regions\":[";
        for(size_t i=0;i<regions.size();i++) {
            if(i) output << ',';
            auto& r=regions[i]; output << "{\"text\":" << quoted(r.text) << ",\"confidence\":" << r.confidence << ",\"box\":[";
            for(int k=0;k<4;k++) {
                if(k) output << ',';
                auto p=r.box[k];
                if(rotation==180) {p.x=c.image.cols-1-p.x;p.y=c.image.rows-1-p.y;}
                output << "{\"x\":" << p.x+c.bounds.x << ",\"y\":" << p.y+c.bounds.y << '}';
            }
            output << "],\"cropPng\":" << quoted(png(r.crop)) << '}';
        }
        output << "]}"; return allocate(output.str());
    } catch(const std::exception& ex) {return error(ex.what());} catch(...) {return error("Unknown native OCR failure.");}
}
