cmake_minimum_required(VERSION 3.21)
project(MFNOpenXR LANGUAGES CXX)

add_library(MFNOpenXR SHARED MFNOpenXR.cpp)
target_compile_features(MFNOpenXR PRIVATE cxx_std_17)
target_compile_definitions(MFNOpenXR PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_include_directories(MFNOpenXR PRIVATE
    "${CMAKE_CURRENT_LIST_DIR}/../third_party/openxr/include"
    "${CMAKE_CURRENT_LIST_DIR}/unity")
target_link_libraries(MFNOpenXR PRIVATE
    "${CMAKE_CURRENT_LIST_DIR}/../third_party/openxr/x64/lib/openxr_loader.lib"
    d3d11
    d3dcompiler)
set_target_properties(MFNOpenXR PROPERTIES OUTPUT_NAME "MFNOpenXR")
