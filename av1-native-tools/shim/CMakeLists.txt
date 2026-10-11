# shim/CMakeLists.txt - builds the ONE native library `codebrix_av1` for CodeBrix.VideoEncoding.Av1.
#
# libaom (v3.15.2) is built from the UNMODIFIED tree in ../aom through libaom's OWN CMake
# (add_subdirectory, EXCLUDE_FROM_ALL), as a STATIC library, and linked into one shared library
# together with codebrix_av1.c. Only the functions declared in codebrix_av1.h are exported.
#
# libaom's CMake is driven with (forced here, whatever the cache says):
#   CONFIG_AV1_ENCODER=1  CONFIG_AV1_DECODER=0   encoder only - no decoder is compiled at all
#   CONFIG_AV1_HIGHBITDEPTH=1                    10- and 12-bit input
#   CONFIG_MULTITHREAD=1                         libaom's worker threads (g_threads, row-mt, tiles)
#   CONFIG_PIC=1                                 position-independent code, -DPIC for the x86 assembly
#   CONFIG_RUNTIME_CPU_DETECT=1                  SIMD chosen at run time (x86 cpuid / Arm hwcaps /
#                                                RISC-V hwcap), so one binary runs on every CPU of its arch
#   CONFIG_WEBM_IO=0  CONFIG_LIBYUV=0            no libwebm, no libyuv (only aomenc / the tests use them;
#                                                the shim never scales or converts)
#   CONFIG_TUNE_VMAF=0  CONFIG_TUNE_BUTTERAUGLI=0  no external libraries (libaom's defaults, restated)
#   ENABLE_APPS=0  ENABLE_TOOLS=0  ENABLE_EXAMPLES=0  ENABLE_TESTS=0  ENABLE_TESTDATA=0  ENABLE_DOCS=0
#   BUILD_SHARED_LIBS=0                          static libaom, folded into the shared codebrix_av1
#   ENABLE_NASM=1                                x86 / x86_64: nasm, never yasm (libaom prefers nasm). The
#                                                nasm on PATH is REQUIRED and pinned as the assembler (the
#                                                Linux/Android scripts put the vendored ../nasm build there)
#   CMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER       build tools (nasm, perl) are host programs
#   GIT_EXECUTABLE=<none>                        libaom's version string comes from aom/CHANGELOG, never
#                                                from whatever git repository happens to contain the tree
# Everything else is libaom's default (all SIMD flavours the toolchain supports, runtime-dispatched).
#
# Options:
#   CBA_BUILD_SMOKE_TEST=ON   also build smoke-test.c as `codebrix_av1_smoke`. The smoke test loads
#                             codebrix_av1 BY PATH at run time (dlopen / LoadLibrary) and links no libaom.
cmake_minimum_required(VERSION 3.16)

project(codebrix_av1 LANGUAGES C)

option(CBA_BUILD_SMOKE_TEST "Build the smoke-test executable" OFF)

set(CBA_TOOLS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/..")
if(NOT DEFINED CBA_AOM_DIR)
    set(CBA_AOM_DIR "${CBA_TOOLS_DIR}/aom")
endif()

if(NOT EXISTS "${CBA_AOM_DIR}/av1/av1_cx_iface.c" OR NOT EXISTS "${CBA_AOM_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "Upstream libaom tree not found at ${CBA_AOM_DIR}.")
endif()

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

# Every object - libaom's included - is compiled with hidden visibility; codebrix_av1.h marks its
# own functions CBA_EXPORT (visibility("default") / __declspec(dllexport)). libaom's own x86inc.asm
# marks every assembly function hidden as well (nasm, ELF / Mach-O).
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# ---- libaom via its own CMake -------------------------------------------------------------------
foreach(opt CONFIG_AV1_ENCODER CONFIG_AV1_HIGHBITDEPTH CONFIG_MULTITHREAD CONFIG_PIC CONFIG_RUNTIME_CPU_DETECT)
    set(${opt} 1 CACHE STRING "codebrix_av1: forced 1" FORCE)
endforeach()
foreach(opt CONFIG_AV1_DECODER CONFIG_WEBM_IO CONFIG_LIBYUV CONFIG_TUNE_VMAF CONFIG_TUNE_BUTTERAUGLI)
    set(${opt} 0 CACHE STRING "codebrix_av1: forced 0" FORCE)
endforeach()
foreach(opt ENABLE_APPS ENABLE_TOOLS ENABLE_EXAMPLES ENABLE_TESTS ENABLE_TESTDATA ENABLE_DOCS BUILD_SHARED_LIBS)
    set(${opt} OFF CACHE BOOL "codebrix_av1: forced OFF" FORCE)
endforeach()
set(ENABLE_NASM ON CACHE BOOL "codebrix_av1: nasm, never yasm" FORCE)
# Build tools are HOST programs: never look for them inside a cross-compiling sysroot. (The Android
# NDK toolchain file would otherwise hide the nasm on PATH from libaom's find_program, and CMake
# would then silently fall back to the yasm the NDK ships.) Inherited by libaom's directory.
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
# x86 / x86-64 targets: nasm is REQUIRED (libaom's assembly); say so here, by name, rather than let
# libaom fall back to yasm or fail obscurely.
# The target CPU is libaom's AOM_TARGET_CPU when the build script passes it (macOS and Windows do:
# there CMAKE_SYSTEM_PROCESSOR is the HOST's when cross-building with CMAKE_OSX_ARCHITECTURES or
# -A ARM64), else CMAKE_SYSTEM_PROCESSOR (Linux containers, the Android NDK toolchain).
if(DEFINED AOM_TARGET_CPU)
    string(TOLOWER "${AOM_TARGET_CPU}" cba_cpu)
else()
    string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" cba_cpu)
endif()
if(cba_cpu MATCHES "^(x86_64|amd64|i[3-6]86|x86)$")
    find_program(CBA_NASM_EXECUTABLE nasm)
    if(NOT CBA_NASM_EXECUTABLE)
        message(FATAL_ERROR "nasm is required for libaom's x86 assembly (yasm is never used). Build the "
                            "vendored one with av1-native-tools/build-nasm.sh <dir> and put <dir> first on PATH.")
    endif()
    # A NORMAL variable shadows the cache entry libaom's find_program() would fill, so libaom takes
    # exactly this assembler. Needed on Android: the NDK toolchain file sets CMAKE_ASM_NASM_COMPILER
    # to the NDK's own yasm, which would otherwise win silently.
    set(CMAKE_ASM_NASM_COMPILER "${CBA_NASM_EXECUTABLE}")
    message(STATUS "codebrix_av1: nasm for libaom: ${CBA_NASM_EXECUTABLE}")
endif()
set(GIT_EXECUTABLE "${CMAKE_CURRENT_BINARY_DIR}/no-git-version-comes-from-CHANGELOG" CACHE FILEPATH
    "codebrix_av1: deliberately not a git executable" FORCE)

add_subdirectory("${CBA_AOM_DIR}" "${CMAKE_CURRENT_BINARY_DIR}/aom" EXCLUDE_FROM_ALL)

if(NOT TARGET aom)
    message(FATAL_ERROR "libaom's CMake did not define the aom target.")
endif()

# ---- the shared library -------------------------------------------------------------------------
add_library(codebrix_av1 SHARED codebrix_av1.c)
target_include_directories(codebrix_av1 PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}" "${CBA_AOM_DIR}")
target_link_libraries(codebrix_av1 PRIVATE aom)
if(MSVC)
    target_compile_definitions(codebrix_av1 PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_options(codebrix_av1 PRIVATE /W4)
else()
    target_compile_options(codebrix_av1 PRIVATE -Wall -Wextra)
endif()
# No SONAME version suffix: the managed loader probes the plain file name on every RID.
if(WIN32)
    set_target_properties(codebrix_av1 PROPERTIES PREFIX "")
endif()
if(CMAKE_SYSTEM_NAME STREQUAL "Linux" OR CMAKE_SYSTEM_NAME STREQUAL "Android")
    find_package(Threads REQUIRED)
    target_link_libraries(codebrix_av1 PRIVATE Threads::Threads m)
    target_link_options(codebrix_av1 PRIVATE
        "LINKER:--no-undefined"
        "LINKER:--exclude-libs,ALL"
        "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/codebrix_av1.map"
        "LINKER:-z,noexecstack"
        "LINKER:--build-id=sha1")
    set_property(TARGET codebrix_av1 APPEND PROPERTY LINK_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/codebrix_av1.map")
endif()
if(APPLE)
    set_target_properties(codebrix_av1 PROPERTIES INSTALL_NAME_DIR "@rpath")
    target_link_options(codebrix_av1 PRIVATE
        "LINKER:-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/codebrix_av1.exports")
endif()

# ---- smoke test ---------------------------------------------------------------------------------
if(CBA_BUILD_SMOKE_TEST)
    add_executable(codebrix_av1_smoke "${CMAKE_CURRENT_SOURCE_DIR}/smoke-test.c")
    # No libaom here: the library under test is loaded by path; the checks are bitstream-level.
    target_link_libraries(codebrix_av1_smoke PRIVATE ${CMAKE_DL_LIBS})
    if(MSVC)
        target_compile_definitions(codebrix_av1_smoke PRIVATE _CRT_SECURE_NO_WARNINGS)
    else()
        target_compile_options(codebrix_av1_smoke PRIVATE -Wall -Wextra)
    endif()
endif()
