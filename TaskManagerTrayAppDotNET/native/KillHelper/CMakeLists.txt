cmake_minimum_required(VERSION 3.25)

if(CMAKE_GENERATOR MATCHES "^Visual Studio")
    set(CMAKE_VS_GLOBALS "TrackFileAccess=false")
    list(APPEND CMAKE_TRY_COMPILE_PLATFORM_VARIABLES CMAKE_VS_GLOBALS)
endif()

project(TaskManagerTrayAppDotNETKillHelper LANGUAGES CXX)

set(KILL_HELPER_COMMON_SOURCES
    KillHelper.cpp
    KillHelperProtocol.h)

add_executable(TaskManagerTrayAppDotNETKillHelper WIN32
    ${KILL_HELPER_COMMON_SOURCES}
    KillHelper.rc)

add_library(TaskManagerTrayAppDotNETKillHelperNativeAOT STATIC
    ${KILL_HELPER_COMMON_SOURCES})

foreach(KILL_HELPER_TARGET IN ITEMS
        TaskManagerTrayAppDotNETKillHelper
        TaskManagerTrayAppDotNETKillHelperNativeAOT)
    set_target_properties(${KILL_HELPER_TARGET} PROPERTIES
        CXX_STANDARD 20
        CXX_STANDARD_REQUIRED YES
        CXX_EXTENSIONS NO
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

    target_compile_definitions(${KILL_HELPER_TARGET} PRIVATE
        NOMINMAX
        STRICT
        UNICODE
        WIN32_LEAN_AND_MEAN
        _UNICODE)

    target_compile_options(${KILL_HELPER_TARGET} PRIVATE
        /guard:cf
        /guard:ehcont
        /permissive-
        /sdl
        /W4
        /WX)
endforeach()

set_target_properties(TaskManagerTrayAppDotNETKillHelper PROPERTIES
    OUTPUT_NAME "TaskManagerTrayAppDotNET.KillHelper"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/out"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/out")

set_target_properties(TaskManagerTrayAppDotNETKillHelperNativeAOT PROPERTIES
    OUTPUT_NAME "TaskManagerTrayAppDotNET.KillHelper.NativeAOT"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
    ARCHIVE_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/out"
    ARCHIVE_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/out")

target_compile_definitions(TaskManagerTrayAppDotNETKillHelperNativeAOT PRIVATE
    KILL_HELPER_NATIVE_AOT_ENTRY)

target_link_options(TaskManagerTrayAppDotNETKillHelper PRIVATE
    /CETCOMPAT
    /DYNAMICBASE
    /GUARD:CF
    /GUARD:EHCONT
    /HIGHENTROPYVA
    /MANIFEST:NO
    /NXCOMPAT
    /STACK:131072,131072)
