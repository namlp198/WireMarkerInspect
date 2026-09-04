#define WMI_IMPORT
#include "vision.h"
#include <opencv2/opencv.hpp>
#include <iostream>
#include <string>
int main() {
    if(wmi_abi_version()!=1) return 1;
    if(wmi_matching_abi_version()!=1)return 6;
    auto matching=wmi_match(nullptr,0,0,0,nullptr,0,0,nullptr,0,0,nullptr,0,0,nullptr,0);
    std::string matchingError(matching);wmi_free(matching);
    if(matchingError.find("error")==std::string::npos)return 7;
    cv::Mat frame(100,120,CV_8UC3,cv::Scalar(10,20,30));
    double rect[]={10,10,110,90};
    char* value=wmi_crop(frame.data,120,100,static_cast<int>(frame.step),0,rect,2);
    std::string good(value); wmi_free(value);
    if(good.find("cropPng")==std::string::npos) return 2;
    double bad[]={-10,0,100,90};
    value=wmi_crop(frame.data,120,100,static_cast<int>(frame.step),0,bad,2);
    std::string failure(value); wmi_free(value);
    if(failure.find("error")==std::string::npos) return 3;
    char* message=nullptr;
    auto* engine=wmi_create(L"missing-det.onnx",L"missing-rec.onnx",L"missing.txt",&message);
    if(engine || !message) return 4;
    wmi_free(message);
    value=wmi_inspect(nullptr,frame.data,120,100,static_cast<int>(frame.step),0,rect,2,0);
    failure=value; wmi_free(value);
    if(failure.find("error")==std::string::npos) return 5;
    std::cout << "Native ABI, crop validation, missing-model and null-engine tests passed.\n";
    return 0;
}
