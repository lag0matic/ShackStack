cmake_minimum_required(VERSION 3.20)

project(shackstack_ggmorse_bridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT DEFINED GGMORSE_SOURCE_DIR)
    set(GGMORSE_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../vendor/ggmorse")
endif()

set(GGMORSE_INCLUDE_DIR "${GGMORSE_SOURCE_DIR}/include")
set(GGMORSE_SRC_DIR "${GGMORSE_SOURCE_DIR}/src")

add_library(shackstack_ggmorse_bridge SHARED
    src/shackstack_ggmorse_bridge.cpp
    ${GGMORSE_SRC_DIR}/ggmorse.cpp
    ${GGMORSE_SRC_DIR}/resampler.cpp
)

target_include_directories(shackstack_ggmorse_bridge
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${GGMORSE_INCLUDE_DIR}
        ${GGMORSE_SRC_DIR}
)

target_compile_definitions(shackstack_ggmorse_bridge PRIVATE
    GGMORSE_SHARED
    GGMORSE_BUILD
)

if(MSVC)
    target_compile_definitions(shackstack_ggmorse_bridge PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()
