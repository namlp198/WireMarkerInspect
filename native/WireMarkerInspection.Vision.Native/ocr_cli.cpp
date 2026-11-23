#define WMI_IMPORT
#include "vision.h"
#include <opencv2/opencv.hpp>
#include <algorithm>
#include <cctype>
#include <filesystem>
#include <iostream>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
std::string withoutCrops(std::string json) {
    constexpr const char* marker="\"cropPng\":\"";
    size_t start=0;
    while((start=json.find(marker,start))!=std::string::npos) {
        start+=std::char_traits<char>::length(marker);
        const size_t end=json.find('"',start);
        if(end==std::string::npos)break;
        json.erase(start,end-start);
    }
    return json;
}
std::string escape(const std::string& value) {
    std::string result;
    for(char c:value) {
        if(c=='"'||c=='\\')result.push_back('\\');
        result.push_back(c);
    }
    return result;
}
}

int main(int argc,char** argv) {
    if(argc!=6&&argc!=10) {
        std::cerr << "Usage: VisionOcrCli detector.onnx recognizer.onnx dictionary.txt image orientation [left top right bottom]\n"
            << "ROI coordinates are normalized to 0..1. Omit them to inspect the full image.\n";
        return 2;
    }
    try {
        const auto detector=std::filesystem::path(argv[1]).wstring();
        const auto recognizer=std::filesystem::path(argv[2]).wstring();
        const auto dictionary=std::filesystem::path(argv[3]).wstring();
        const int orientation=std::stoi(argv[5]);
        std::optional<std::array<double,4>> normalizedRoi;
        if(argc==10) {
            normalizedRoi=std::array<double,4>{std::stod(argv[6]),std::stod(argv[7]),std::stod(argv[8]),std::stod(argv[9])};
            const auto [left,top,right,bottom]=*normalizedRoi;
            if(left<0||top<0||right>1||bottom>1||left>=right||top>=bottom)
                throw std::invalid_argument("Normalized ROI must satisfy 0 <= left < right <= 1 and 0 <= top < bottom <= 1.");
        }
        char* creationError=nullptr;
        void* engine=wmi_create(detector.c_str(),recognizer.c_str(),dictionary.c_str(),&creationError);
        if(!engine) {
            std::cerr << (creationError?creationError:"OCR engine initialization failed.") << '\n';
            if(creationError)wmi_free(creationError);
            return 4;
        }
        const std::filesystem::path input=argv[4];
        auto inspect=[&](const std::filesystem::path& path,bool compact) {
            const cv::Mat image=cv::imread(path.string(),cv::IMREAD_COLOR);
            if(image.empty())throw std::runtime_error("Cannot load image: "+path.string());
            double roi[]={0,0,static_cast<double>(image.cols),static_cast<double>(image.rows)};
            if(normalizedRoi) {
                roi[0]=(*normalizedRoi)[0]*image.cols;roi[1]=(*normalizedRoi)[1]*image.rows;
                roi[2]=(*normalizedRoi)[2]*image.cols;roi[3]=(*normalizedRoi)[3]*image.rows;
            }
            char* raw=wmi_inspect(engine,image.data,image.cols,image.rows,static_cast<int>(image.step),0,roi,2,orientation);
            std::string payload=raw;wmi_free(raw);
            if(compact)std::cout << "{\"file\":\"" << escape(path.filename().string()) << "\",\"result\":"
                << withoutCrops(std::move(payload)) << "}\n";
            else std::cout << payload << '\n';
        };
        if(std::filesystem::is_directory(input)) {
            std::vector<std::filesystem::path> files;
            for(const auto& entry:std::filesystem::directory_iterator(input)) {
                auto extension=entry.path().extension().string();
                std::transform(extension.begin(),extension.end(),extension.begin(),[](unsigned char c){return static_cast<char>(std::tolower(c));});
                if(entry.is_regular_file()&&(extension==".bmp"||extension==".png"||extension==".jpg"||extension==".jpeg"||extension==".tif"||extension==".tiff"))files.push_back(entry.path());
            }
            std::sort(files.begin(),files.end());
            for(const auto& path:files)inspect(path,true);
        } else inspect(input,false);
        wmi_destroy(engine);
        return 0;
    } catch(const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 5;
    }
}
