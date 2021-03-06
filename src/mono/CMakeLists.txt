cmake_minimum_required(VERSION 3.14.5)

if (CMAKE_VERSION VERSION_GREATER 3.15 OR CMAKE_VERSION VERSION_EQUAL 3.15)
  cmake_policy(SET CMP0091 NEW)
endif()

project(mono)

include(GNUInstallDirs)
include(CheckIncludeFile)
include(CheckFunctionExists)
include(TestBigEndian)
include(CheckCCompilerFlag)

include("cmake/QuietOSXRanlib.cmake")

set(CMAKE_MODULE_PATH
  ${CMAKE_MODULE_PATH}
  "${CMAKE_CURRENT_SOURCE_DIR}/cmake"
  )
set(CMAKE_INSTALL_MESSAGE LAZY)

find_program(CCACHE_PROGRAM ccache)
if(CCACHE_PROGRAM)
  set_property(GLOBAL PROPERTY RULE_LAUNCH_COMPILE "${CCACHE_PROGRAM}")
endif()

function(append value)
  foreach(variable ${ARGN})
    set(${variable} "${${variable}} ${value}" PARENT_SCOPE)
  endforeach(variable)
endfunction()

# User options
include(options)

function(process_enable_minimal)
  string(REPLACE "," ";" tmp1 "${ENABLE_MINIMAL}")
  foreach(arg ${tmp1})
    string(TOUPPER "${arg}" var1)
    set(DISABLE_${var1} 1 PARENT_SCOPE)
  endforeach(arg)
endfunction()

if(ENABLE_MINIMAL)
  process_enable_minimal()
endif()

function(extract_mono_corlib_version)
  file(STRINGS ${CMAKE_CURRENT_SOURCE_DIR}/configure.ac corlib_version_line REGEX ^MONO_CORLIB_VERSION=)

  if(corlib_version_line STREQUAL "")
    message(FATAL_ERROR "Couldn't find MONO_CORLIB_VERSION from configure.ac")
  endif()

  string(REGEX REPLACE "MONO_CORLIB_VERSION=([0-9a-fA-F\-]+)" "\\1" corlib_version ${corlib_version_line})

  if(corlib_version STREQUAL "")
    message(FATAL_ERROR "Couldn't parse corlib version")
  endif()

  set(MONO_CORLIB_VERSION "\"${corlib_version}\"" PARENT_SCOPE)
endfunction()

extract_mono_corlib_version()

if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/mono.proj")
  set(ENABLE_NETCORE 1)
endif()

if(ENABLE_NETCORE)
  set(DISABLE_REMOTING 1)
  set(DISABLE_REFLECTION_EMIT_SAVE 1)
  set(DISABLE_APPDOMAINS 1)
  set(DISABLE_SHADOW_COPY 1)
  set(DISABLE_CLEANUP 1)
  set(DISABLE_ASSEMBLY_REMAPPING 1)
  set(DISABLE_SECURITY 1)
  set(DISABLE_MDB 1)
  set(DISABLE_COM 1)
  set(DISABLE_GAC 1)
  set(DISABLE_PERFCOUNTERS 1)
  set(DISABLE_ATTACH 1)
  set(DISABLE_CONFIG 1)
  set(DISABLE_CFGDIR_CONFIG 1)
  set(DISABLE_VERIFIER 1)
else()
  message(FATAL_ERROR "Building without -DENABLE_NETCORE=1 is not supported.")
endif()

# Dependencies between options
if(DISABLE_ASSEMBLY_REMAPPING)
  set(DISABLE_DESKTOP_LOADER 1)
endif()
if(ENABLE_INTERP_LIB)
  set(DISABLE_INTERPRETER 1)
endif()
if(NOT DISABLE_INTERPRETER OR NOT DISABLE_JIT)
  set(ENABLE_ILGEN 1)
endif()
if(NOT DISABLE_EVENTPIPE)
  set(ENABLE_PERFTRACING 1)
endif()
if(ENABLE_LAZY_GC_THREAD_CREATION)
  set(LAZY_GC_THREAD_CREATION 1)
endif()

set(DISABLED_FEATURES "\"\"")

if((CMAKE_CXX_COMPILER_ID STREQUAL "GNU") OR(CMAKE_CXX_COMPILER_ID STREQUAL "Clang") OR(CMAKE_CXX_COMPILER_ID STREQUAL "AppleClang"))
  set(GCC 1)
endif()

add_definitions(-DHAVE_CONFIG_H)
add_definitions(-DMONO_DLL_EXPORT)

if(GCC)
  add_definitions(-g)  # TODO: should this really be on by default?
  add_definitions(-fPIC)
  add_definitions(-fvisibility=hidden)
  set(USE_GCC_ATOMIC_OPS 1)
endif()

set(HAVE_CLASSIC_WINAPI_SUPPORT 1)
set(HAVE_MOVING_COLLECTOR 1)
set(HAVE_CONC_GC_AS_DEFAULT 1)
set(MONO_INSIDE_RUNTIME 1)

######################################
# AOT CROSS COMPILER SUPPORT
######################################

if(NOT AOT_TARGET_TRIPLE STREQUAL "")
  set(MONO_CROSS_COMPILE 1)
  add_definitions(-DNO_GLOBALIZATION_SHIM)
  if(NOT AOT_OFFSETS_FILE STREQUAL "")
    set(MONO_OFFSETS_FILE "${AOT_OFFSETS_FILE}")
  endif()
  if(AOT_TARGET_TRIPLE STREQUAL "x86_64-apple-darwin10")
    set(TARGET_SYSTEM_NAME "iOS")
    set(TARGET_ARCH "x86_64")
  elseif(AOT_TARGET_TRIPLE STREQUAL "i386-apple-darwin10")
    set(TARGET_SYSTEM_NAME "iOS")
    set(TARGET_ARCH "x86")
  elseif(AOT_TARGET_TRIPLE STREQUAL "aarch64-apple-darwin10")
    set(TARGET_SYSTEM_NAME "iOS")
    set(TARGET_ARCH "arm64")
  elseif(AOT_TARGET_TRIPLE STREQUAL "arm-apple-darwin10")
    set(TARGET_SYSTEM_NAME "iOS")
    set(TARGET_ARCH "arm")
  elseif(AOT_TARGET_TRIPLE STREQUAL "wasm32-unknown-none")
    set(TARGET_SYSTEM_NAME "Emscripten")
    set(TARGET_ARCH "wasm")
  elseif(AOT_TARGET_TRIPLE STREQUAL "x86_64-none-linux-android")
    set(TARGET_SYSTEM_NAME "Android")
    set(TARGET_ARCH "x86_64")
  elseif(AOT_TARGET_TRIPLE STREQUAL "i686-none-linux-android")
    set(TARGET_SYSTEM_NAME "Android")
    set(TARGET_ARCH "x86")
  elseif(AOT_TARGET_TRIPLE STREQUAL "aarch64-v8a-linux-android")
    set(TARGET_SYSTEM_NAME "Android")
    set(TARGET_ARCH "arm64")
  elseif(AOT_TARGET_TRIPLE STREQUAL "armv7-none-linux-androideabi")
    set(TARGET_SYSTEM_NAME "Android")
    set(TARGET_ARCH "arm")
  elseif(AOT_TARGET_TRIPLE STREQUAL "aarch64-linux-gnu")
    set(TARGET_SYSTEM_NAME "Linux")
    set(TARGET_ARCH "arm64")
  else()
    message(FATAL_ERROR "AOT target '${AOT_TARGET_TRIPLE}' not supported.")
  endif()
endif()

######################################
# HOST OS CHECKS
######################################

message ("CMAKE_SYSTEM_NAME=${CMAKE_SYSTEM_NAME}")

if(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
  add_definitions(-D_THREAD_SAFE)
  set(HOST_DARWIN 1)
  set(HOST_OSX 1)
  set(PTHREAD_POINTER_ID 1)
  set(USE_MACH_SEMA 1)
elseif(CMAKE_SYSTEM_NAME STREQUAL "iOS" OR CMAKE_SYSTEM_NAME STREQUAL "tvOS")
  # See man cmake-toolchains(7) on which variables
  # control cross-compiling to ios
  add_definitions(-DNO_GLOBALIZATION_SHIM)
  add_definitions(-D_THREAD_SAFE)
  set(HOST_DARWIN 1)
  set(HOST_IOS 1)
  if(CMAKE_SYSTEM_NAME STREQUAL "tvOS")
    set(HOST_TVOS 1)
  endif()
  set(PTHREAD_POINTER_ID 1)
  set(USE_MACH_SEMA 1)
  set(DISABLE_EXECUTABLES 1)
  set(DISABLE_CRASH_REPORTING 1)
  set(ENABLE_MONOTOUCH 1)
  add_definitions(-DMONOTOUCH=1)
  add_definitions("-DSMALL_CONFIG")
  add_definitions("-D_XOPEN_SOURCE")
  add_definitions("-DHAVE_LARGE_FILE_SUPPORT=1")
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  set(HOST_LINUX 1)
  add_definitions(-D_GNU_SOURCE -D_REENTRANT)
  add_definitions(-D_THREAD_SAFE)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Android")
  set(HOST_LINUX 1)
  add_definitions(-D_GNU_SOURCE -D_REENTRANT)
  add_definitions(-D_THREAD_SAFE)
  # The normal check fails because it uses --isystem <ndk root>/sysroot/usr/include
  set(HAVE_USR_INCLUDE_MALLOC_H 1)
  set(HOST_ANDROID 1)
  set(ANDROID_UNIFIED_HEADERS 1)
  set(ENABLE_MONODROID 1)
  set(DISABLE_EXECUTABLES 1)
  set(GC_SUSPEND "hybrid")
  # Force some defines
  set(HAVE_SCHED_GETAFFINITY 0)
  set(HAVE_SCHED_SETAFFINITY 0)
  # FIXME: Rest of the flags from configure.ac
elseif(CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
  set(HOST_BROWSER 1)
  add_definitions(-D_THREAD_SAFE)
  set(DISABLE_EXECUTABLES 1)
  # FIXME: Is there a cmake option for this ?
  set(DISABLE_SHARED_LIBS 1)
  # sys/random.h exists, but its not found
  set(HAVE_SYS_RANDOM_H 1)
elseif(CMAKE_SYSTEM_NAME STREQUAL "Windows")
  set(HOST_WIN32 1)
  set(EXE_SUFFIX ".exe")
  set(DISABLE_CRASH_REPORTING 1)
  set(DISABLE_PORTABILITY 1)
  set(HOST_NO_SYMLINKS 1)
  set(MONO_KEYWORD_THREAD "__declspec (thread)")
  set(MONO_ZERO_LEN_ARRAY 1)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>") # statically link VC runtime library
  add_compile_options(/W3)   # set warning level 3
  add_compile_options(/EHsc) # set exception handling behavior
  add_compile_options(/FC)   # use full pathnames in diagnostics
  if(CMAKE_BUILD_TYPE STREQUAL "Release")
    add_compile_options(/Oi) # enable intrinsics
    add_compile_options(/GF) # enable string pooling
    add_compile_options(/Zi) # enable debugging information
    add_compile_options(/GL) # whole program optimization
    add_link_options(/LTCG)  # link-time code generation
  endif()
else()
  message(FATAL_ERROR "Host '${CMAKE_SYSTEM_NAME}' not supported.")
endif()

######################################
# TARGET OS CHECKS
######################################

if(NOT TARGET_SYSTEM_NAME)
  set(TARGET_SYSTEM_NAME "${CMAKE_SYSTEM_NAME}")
endif()

if(TARGET_SYSTEM_NAME STREQUAL "Darwin")
  set(TARGET_MACH 1)
  set(TARGET_OSX 1)
  set(TARGET_DARWIN 1)
  if (GC_SUSPEND STREQUAL "default")
    set(GC_SUSPEND "hybrid")
  endif()
elseif(TARGET_SYSTEM_NAME STREQUAL "iOS" OR TARGET_SYSTEM_NAME STREQUAL "tvOS")
  set(TARGET_MACH 1)
  set(TARGET_IOS 1)
  set(TARGET_DARWIN 1)
  if(TARGET_SYSTEM_NAME STREQUAL "tvOS")
    set(TARGET_TVOS 1)
  endif()
  set(ENABLE_MONOTOUCH 1)
  add_definitions(-DMONOTOUCH=1)
elseif(TARGET_SYSTEM_NAME STREQUAL "Linux")
  set(TARGET_LINUX 1)
elseif(TARGET_SYSTEM_NAME STREQUAL "Android")
  set(TARGET_ANDROID 1)
elseif(TARGET_SYSTEM_NAME STREQUAL "Emscripten")
  set(TARGET_BROWSER 1)
  if (CMAKE_BUILD_TYPE STREQUAL "Release")
    add_compile_options(-Os)
  endif()
elseif(TARGET_SYSTEM_NAME STREQUAL "Windows")
  set(TARGET_WIN32 1)
else()
  message(FATAL_ERROR "Target '${TARGET_SYSTEM_NAME}' not supported.")
endif()

######################################
# HOST ARCH CHECKS
######################################

if(NOT "${CMAKE_OSX_ARCHITECTURES}" STREQUAL "")
  set(CMAKE_SYSTEM_PROCESSOR "${CMAKE_OSX_ARCHITECTURES}")
endif()

if(NOT "${MSVC_C_ARCHITECTURE_ID}" STREQUAL "")
  set(CMAKE_SYSTEM_PROCESSOR "${MSVC_C_ARCHITECTURE_ID}")
endif()

if(HOST_BROWSER)
  # CMAKE_SYSTEM_PROCESSOR is set to x86 by emscripten
  set(CMAKE_SYSTEM_PROCESSOR "wasm")
endif()

# Unify naming
if(CMAKE_SYSTEM_PROCESSOR STREQUAL "armv7l" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "ARMV7")
  set(CMAKE_SYSTEM_PROCESSOR "arm")
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "i686" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "i386" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "X86")
  set(CMAKE_SYSTEM_PROCESSOR "x86")
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "aarch64" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "ARM64")
  set(CMAKE_SYSTEM_PROCESSOR "arm64")
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "AMD64" OR CMAKE_SYSTEM_PROCESSOR STREQUAL "x64")
  set(CMAKE_SYSTEM_PROCESSOR "x86_64")
endif()

message ("CMAKE_SYSTEM_PROCESSOR=${CMAKE_SYSTEM_PROCESSOR}")

if(CMAKE_SYSTEM_PROCESSOR STREQUAL "x86_64")
  set(HOST_AMD64 1)
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "x86")
  set(HOST_X86 1)
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "arm64")
  set(HOST_ARM64 1)
elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "arm")
  set(HOST_ARM 1)
elseif(CMAKE_SYSTEM_PROCESSOR STREQUAL "wasm")
  set(HOST_WASM 1)
else()
  message(FATAL_ERROR "CMAKE_SYSTEM_PROCESSOR='${CMAKE_SYSTEM_PROCESSOR}' not supported.")
endif()

######################################
# TARGET ARCH CHECKS
######################################

if(NOT TARGET_ARCH)
  set(TARGET_ARCH "${CMAKE_SYSTEM_PROCESSOR}")
endif()

# Unify naming
if(TARGET_ARCH STREQUAL "armv7l" OR TARGET_ARCH STREQUAL "ARMV7")
  set(TARGET_ARCH "arm")
elseif(TARGET_ARCH STREQUAL "i686" OR TARGET_ARCH STREQUAL "i386" OR TARGET_ARCH STREQUAL "X86")
  set(TARGET_ARCH "x86")
elseif(TARGET_ARCH STREQUAL "aarch64" OR TARGET_ARCH STREQUAL "ARM64")
  set(TARGET_ARCH "arm64")
elseif(TARGET_ARCH STREQUAL "AMD64" OR TARGET_ARCH STREQUAL "x64")
  set(TARGET_ARCH "x86_64")
endif()

message("TARGET_ARCH=${TARGET_ARCH}")
message("CMAKE_CROSSCOMPILING=${CMAKE_CROSSCOMPILING}")

if(TARGET_ARCH STREQUAL "x86_64")
  set(TARGET_AMD64 1)
  set(MONO_ARCHITECTURE "\"amd64\"")
  set(TARGET_SIZEOF_VOID_P 8)
  set(SIZEOF_REGISTER 8)
elseif(TARGET_ARCH STREQUAL "x86")
  set(TARGET_X86 1)
  set(MONO_ARCHITECTURE "\"x86\"")
  set(TARGET_SIZEOF_VOID_P 4)
  set(SIZEOF_REGISTER 4)
elseif(TARGET_ARCH STREQUAL "arm64")
  set(TARGET_ARM64 1)
  set(MONO_ARCHITECTURE "\"arm64\"")
  set(TARGET_SIZEOF_VOID_P 8)
  set(SIZEOF_REGISTER 8)
elseif(TARGET_ARCH MATCHES "arm")
  set(TARGET_ARM 1)
  set(MONO_ARCHITECTURE "\"arm\"")
  # FIXME:
  add_definitions("-DARM_FPU_VFP=1")
  set(TARGET_SIZEOF_VOID_P 4)
  set(SIZEOF_REGISTER 4)
elseif(TARGET_ARCH STREQUAL "wasm")
  set(TARGET_WASM 1)
  set(MONO_ARCHITECTURE "\"wasm\"")
  set(TARGET_SIZEOF_VOID_P 4)
  set(SIZEOF_REGISTER 4)
else()
  message(FATAL_ERROR "TARGET_ARCH='${TARGET_ARCH}' not supported.")
endif()

######################################
# HEADER/FUNCTION CHECKS
######################################
include(configure)

######################################
# GCC CHECKS
######################################

if(GCC)
  # We require C99 with some GNU extensions, e.g. `linux` macro
  set (CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -std=gnu99")
  # The runtime code does not respect ANSI C strict aliasing rules
  append("-fno-strict-aliasing" CMAKE_C_FLAGS CMAKE_CXX_FLAGS)
  # We rely on signed overflow to behave
  append("-fwrapv" CMAKE_C_FLAGS CMAKE_CXX_FLAGS)

  set(WARNINGS "-Wall -Wunused -Wmissing-declarations -Wpointer-arith -Wno-cast-qual -Wwrite-strings -Wno-switch -Wno-switch-enum -Wno-unused-value -Wno-attributes -Wmissing-prototypes -Wstrict-prototypes -Wnested-externs -Wno-format-zero-length -Wno-unused-function")

  if (CMAKE_C_COMPILER_ID MATCHES "Clang")
    set(WARNINGS "${WARNINGS} -Qunused-arguments -Wno-tautological-compare -Wno-parentheses-equality -Wno-self-assign -Wno-return-stack-address -Wno-constant-logical-operand -Wno-zero-length-array")
  endif()

  check_c_compiler_flag("-Werror=incompatible-pointer-types" WERROR_INCOMPATIBLE_POINTER_TYPES)
  if(WERROR_INCOMPATIBLE_POINTER_TYPES)
    set(WERROR "-Werror=incompatible-pointer-types")
  endif()
  set(WERROR "-Werror=return-type -Werror-implicit-function-declaration")

  append("${WARNINGS} ${WERROR}" CMAKE_C_FLAGS CMAKE_CXX_FLAGS)

  set(MONO_ZERO_LEN_ARRAY 0)

  if(ENABLE_WERROR)
    append("-Werror" CMAKE_C_FLAGS CMAKE_CXX_FLAGS)
  endif()
endif()

######################################
# LLVM CHECKS
######################################
set(LLVM_LIBS)
if(LLVM_PREFIX)
  if(TARGET_ARCH STREQUAL "x86_64")
    set(llvm_codegen_libs "x86codegen")
  elseif(TARGET_ARCH STREQUAL "x86")
    set(llvm_codegen_libs "x86codegen")
  elseif(TARGET_ARCH STREQUAL "arm64")
    set(llvm_codegen_libs "aarch64codegen")
  elseif(TARGET_ARCH STREQUAL "arm")
    set(llvm_codegen_libs "armcodegen")
  elseif(TARGET_ARCH STREQUAL "wasm")
    set(llvm_codegen_libs "")
  else()
    message(FATAL_ERROR "FIXME: ${TARGET_ARCH}")
  endif()

  if(CMAKE_CROSSCOMPILING)
    # Can't run llvm-config, hardcode results
    set(llvm_config_path "${LLVM_PREFIX}/include/llvm/Config/llvm-config.h")

    # llvm-config --mono-api-version
    file(STRINGS ${llvm_config_path} llvm_api_version_line REGEX "MONO_API_VERSION ")
    string(REGEX REPLACE ".*MONO_API_VERSION ([0-9]+)" "\\1" llvm_api_version ${llvm_api_version_line})

    # llvm-config --cflags
    set(llvm_cflags "-I${LLVM_PREFIX}/include")
    set(llvm_cxxflags "-I${LLVM_PREFIX}/include")

    # llvm-config --system-libs
    set(llvm_system_libs "-lz -lrt -ldl -lpthread -lm")

    # llvm-config --libs analysis core bitwriter mcjit orcjit
    set(llvm_core_libs "-lLLVMOrcJIT -lLLVMJITLink -lLLVMMCJIT -lLLVMExecutionEngine -lLLVMRuntimeDyld -lLLVMAsmPrinter -lLLVMDebugInfoDWARF -lLLVMCodeGen -lLLVMTarget -lLLVMScalarOpts -lLLVMInstCombine -lLLVMAggressiveInstCombine -lLLVMTransformUtils -lLLVMBitWriter -lLLVMAnalysis -lLLVMProfileData -lLLVMObject -lLLVMMCParser -lLLVMMC -lLLVMDebugInfoCodeView -lLLVMDebugInfoMSF -lLLVMBitReader -lLLVMBitstreamReader -lLLVMCore -lLLVMRemarks -lLLVMBinaryFormat -lLLVMSupport -lLLVMDemangle")

    # Check codegen libs and add needed libraries.
    if("${llvm_codegen_libs}" STREQUAL "x86codegen")
      # llvm-config --libs x86codegen
      set(llvm_extra "-lLLVMX86CodeGen -lLLVMGlobalISel -lLLVMX86Desc -lLLVMX86Utils -lLLVMX86Info -lLLVMMCDisassembler -lLLVMSelectionDAG -lLLVMAsmPrinter -lLLVMDebugInfoDWARF -lLLVMCodeGen -lLLVMTarget -lLLVMScalarOpts -lLLVMInstCombine -lLLVMAggressiveInstCombine -lLLVMTransformUtils -lLLVMBitWriter -lLLVMAnalysis -lLLVMProfileData -lLLVMObject -lLLVMMCParser -lLLVMMC -lLLVMDebugInfoCodeView -lLLVMDebugInfoMSF -lLLVMBitReader -lLLVMBitstreamReader -lLLVMCore -lLLVMRemarks -lLLVMBinaryFormat -lLLVMSupport -lLLVMDemangle")
    elseif("${llvm_codegen_libs}" STREQUAL "armcodegen")
      # llvm-config --libs armcodegen
      set(llvm_extra "-lLLVMARMCodeGen -lLLVMGlobalISel -lLLVMSelectionDAG -lLLVMAsmPrinter -lLLVMDebugInfoDWARF -lLLVMCodeGen -lLLVMTarget -lLLVMScalarOpts -lLLVMInstCombine -lLLVMAggressiveInstCombine -lLLVMTransformUtils -lLLVMBitWriter -lLLVMAnalysis -lLLVMProfileData -lLLVMObject -lLLVMMCParser -lLLVMBitReader -lLLVMBitstreamReader -lLLVMCore -lLLVMRemarks -lLLVMARMDesc -lLLVMMCDisassembler -lLLVMMC -lLLVMDebugInfoCodeView -lLLVMDebugInfoMSF -lLLVMBinaryFormat -lLLVMARMUtils -lLLVMARMInfo -lLLVMSupport -lLLVMDemangle")
    elseif("${llvm_codegen_libs}" STREQUAL "aarch64codegen")
      # llvm-config --libs aarch64codegen
      set(llvm_extra "-lLLVMAArch64CodeGen -lLLVMGlobalISel -lLLVMSelectionDAG -lLLVMAsmPrinter -lLLVMDebugInfoDWARF -lLLVMCodeGen -lLLVMTarget -lLLVMScalarOpts -lLLVMInstCombine -lLLVMAggressiveInstCombine -lLLVMTransformUtils -lLLVMBitWriter -lLLVMAnalysis -lLLVMProfileData -lLLVMObject -lLLVMMCParser -lLLVMBitReader -lLLVMBitstreamReader -lLLVMCore -lLLVMRemarks -lLLVMAArch64Desc -lLLVMMC -lLLVMDebugInfoCodeView -lLLVMDebugInfoMSF -lLLVMBinaryFormat -lLLVMAArch64Utils -lLLVMAArch64Info -lLLVMSupport -lLLVMDemangle")
    else()
      message(FATAL_ERROR "FIXME: ${TARGET_ARCH}")
    endif()

    set(llvm_libs ${llvm_core_libs} ${llvm_extra})
  else()
    set(LLVM_CONFIG ${LLVM_PREFIX}/bin/llvm-config${EXE_SUFFIX})
    if (NOT EXISTS ${LLVM_CONFIG})
      message(FATAL_ERROR "LLVM executable '${LLVM_CONFIG}' not found.")
    endif()

    execute_process(COMMAND ${LLVM_CONFIG} --mono-api-version RESULT_VARIABLE llvm_config_res OUTPUT_VARIABLE llvm_api_version OUTPUT_STRIP_TRAILING_WHITESPACE)
    if (NOT ${llvm_config_res} EQUAL 0)
      message(FATAL_ERROR "'${LLVM_CONFIG} --mono-api-version' failed. Use the llvm fork at https://github.com/dotnet/llvm-project.")
    endif()
    execute_process(COMMAND ${LLVM_CONFIG} --cflags OUTPUT_VARIABLE llvm_cflags OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${LLVM_CONFIG} --cxxflags OUTPUT_VARIABLE llvm_cxxflags OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${LLVM_CONFIG} --system-libs OUTPUT_VARIABLE llvm_system_libs OUTPUT_STRIP_TRAILING_WHITESPACE)
    execute_process(COMMAND ${LLVM_CONFIG} --libs analysis core bitwriter mcjit orcjit ${llvm_codegen_libs} OUTPUT_VARIABLE llvm_libs OUTPUT_STRIP_TRAILING_WHITESPACE)
  endif()

  if (${llvm_api_version} LESS 900)
    message(FATAL_ERROR "LLVM version too old.")
  endif()

  set(ENABLE_LLVM 1)
  set(ENABLE_LLVM_RUNTIME 1)
  set(LLVM_LIBS ${llvm_libs} ${llvm_system_libs})
  set(LLVM_LIBDIR "${LLVM_PREFIX}/lib")
  set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} ${llvm_cflags}")
  set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} ${llvm_cxxflags} -fexceptions -fno-rtti")
  add_definitions(-DLLVM_API_VERSION=${llvm_api_version})
endif()

######################################
# ICU CHECKS
######################################
set(ICU_SHIM_PATH "../../../libraries/Native/Unix/System.Globalization.Native")
if(MONO_CROSS_COMPILE)
elseif(HOST_OSX)
  include(FindPkgConfig)
  # FIXME: Handle errors
  # Defines ICU_INCLUDEDIR/ICU_LIBDIR
  set(ENV{PKG_CONFIG_PATH} "{$PKG_CONFIG_PATH}:/usr/local/lib/pkgconfig:/usr/local/opt/icu4c/lib/pkgconfig")
  pkg_check_modules(ICU icu-uc)
  set(OSX_ICU_LIBRARY_PATH /usr/lib/libicucore.dylib)
  set(ICU_FLAGS "-DTARGET_UNIX -DU_DISABLE_RENAMING -Wno-reserved-id-macro -Wno-documentation -Wno-documentation-unknown-command -Wno-switch-enum -Wno-covered-switch-default -Wno-extra-semi-stmt -Wno-unknown-warning-option -Wno-deprecated-declarations")
  set(HAVE_SYS_ICU 1)
elseif(HOST_WASM)
  #set(ICU_CFLAGS "-DTARGET_UNIX -DU_DISABLE_RENAMING -DHAVE_UDAT_STANDALONE_SHORTER_WEEKDAYS -DHAVE_SET_MAX_VARIABLE")
  set(ICU_FLAGS "-DPALEXPORT=\"\" -DTARGET_UNIX -Wno-reserved-id-macro -Wno-documentation -Wno-documentation-unknown-command -Wno-switch-enum -Wno-covered-switch-default -Wno-extra-semi-stmt -Wno-unknown-warning-option")
  set(HAVE_SYS_ICU 1)
  set(STATIC_ICU 1)
  set(ICU_LIBS "icucore")
elseif(HOST_IOS)
  # FIXME:
elseif(HOST_ANDROID)
  set(ICU_FLAGS "-DPALEXPORT=\"\" -DHAVE_UDAT_STANDALONE_SHORTER_WEEKDAYS -DHAVE_SET_MAX_VARIABLE -DTARGET_UNIX -DTARGET_ANDROID -Wno-reserved-id-macro -Wno-documentation -Wno-documentation-unknown-command -Wno-switch-enum -Wno-covered-switch-default -Wno-covered-switch-default -Wno-extra-semi-stmt -Wno-unknown-warning-option")
  set(HAVE_SYS_ICU 1)
elseif(HOST_LINUX)
  include(FindPkgConfig)
  pkg_check_modules(ICU icu-uc)
  set(ICU_FLAGS "-DTARGET_UNIX -DU_DISABLE_RENAMING -Wno-reserved-id-macro -Wno-documentation -Wno-documentation-unknown-command -Wno-switch-enum -Wno-covered-switch-default -Wno-extra-semi-stmt -Wno-unknown-warning-option -Wno-deprecated-declarations")
  set(HAVE_SYS_ICU 1)
elseif(HOST_WIN32)
  set(ICU_FLAGS "-DTARGET_WINDOWS -DPALEXPORT=EXTERN_C")
  set(HAVE_SYS_ICU 1)
else()
  message(FATAL_ERROR "Unknown host")
endif()

######################################
# GC CHECKS
######################################

if (GC_SUSPEND STREQUAL "coop")
  set(ENABLE_COOP_SUSPEND 1)
elseif(GC_SUSPEND STREQUAL "hybrid")
  set(ENABLE_HYBRID_SUSPEND 1)
endif()

######################################
# EGLIB CHECKS
######################################

set(GNUC_PRETTY)
set(GNUC_UNUSED)
set(BREAKPOINT "G_STMT_START { raise(SIGTRAP); } G_STMT_END")
if(GCC)
  set(GNUC_UNUSED "__attribute__((__unused__))")
  set(GNUC_NORETURN "__attribute__((__noreturn__))")
  if(HOST_AMD64 OR HOST_X86)
    set(BREAKPOINT "G_STMT_START { __asm__(\"int \$03\"); } G_STMT_END")
  endif()
endif()

if(IS_BIG_ENDIAN)
  set(ORDER G_BIG_ENDIAN)
else()
  set(ORDER G_LITTLE_ENDIAN)
endif()

if(HOST_WIN32)
  set(PATHSEP "\\")
  set(SEARCHSEP ";")
  set(OS "WIN32")
  set(PIDTYPE "void *")
else()
  set(PATHSEP "/")
  set(SEARCHSEP ":")
  set(OS "UNIX")
  set(PIDTYPE "int")
endif()

# FIXME:
set(GSIZE_FORMAT "\"lu\"")

set(GSIZE "size_t")
set(GSSIZE "ptrdiff_t")

#
# END OF EGLIB CHECKS
#

TEST_BIG_ENDIAN(IS_BIG_ENDIAN)

if(IS_BIG_ENDIAN)
  set(TARGET_BYTE_ORDER G_BIG_ENDIAN)
else()
  set(TARGET_BYTE_ORDER G_LITTLE_ENDIAN)
endif()

######################################
# DEBUG BUILD CHECKS
######################################
function(process_checked_build)
  string(REPLACE "," ";" tmp1 "${CHECKED_BUILD}")
  foreach(arg ${tmp1})
    string(TOUPPER "${arg}" var1)
    set(ENABLE_CHECKED_BUILD_${var1} 1 PARENT_SCOPE)
  endforeach(arg)
endfunction()

if (CMAKE_BUILD_TYPE STREQUAL "Debug")
  # if no explicit -DCHECKED_BUILD=args option, just do ENABLE_CHECKED_BUILD_PRIVATE_TYPES
  if (CHECKED_BUILD)
    set(ENABLE_CHECKED_BUILD 1)
    process_checked_build()
  else()
    set(ENABLE_CHECKED_BUILD 1)
    set(ENABLE_CHECKED_BUILD_PRIVATE_TYPES 1)
  endif()
endif()
### End of debug build checks

######################################
# OTHER CHECKS
######################################
if(HOST_WIN32)
  set(NAME_DEV_RANDOM "\"\"")
else()
  # FIXME:
  set(NAME_DEV_RANDOM "\"/dev/random\"")
endif()
### End of other checks

######################################
# EXTRACT VERSION
######################################
if(CMAKE_HOST_SYSTEM_NAME STREQUAL "Windows")
  file(STRINGS "${CMAKE_CURRENT_BINARY_DIR}/_version.h" product_version_string_line REGEX "VER_PRODUCTVERSION_STR ")
  string(REGEX REPLACE ".*VER_PRODUCTVERSION_STR *(.*)" "\\1" product_version_string ${product_version_string_line})
else()
  file(STRINGS "${CMAKE_CURRENT_BINARY_DIR}/_version.h" product_version_string_line REGEX "sccsid")
  string(REGEX REPLACE ".*Version *(.*)\";" "\"\\1\"" product_version_string ${product_version_string_line})
endif()

file(STRINGS "${CMAKE_CURRENT_BINARY_DIR}/runtime_version.h" runtime_version_major_line REGEX "RuntimeProductMajorVersion ")
file(STRINGS "${CMAKE_CURRENT_BINARY_DIR}/runtime_version.h" runtime_version_minor_line REGEX "RuntimeProductMinorVersion ")
file(STRINGS "${CMAKE_CURRENT_BINARY_DIR}/runtime_version.h" runtime_version_patch_line REGEX "RuntimeProductPatchVersion ")
string(REGEX REPLACE ".*RuntimeProductMajorVersion *([0-9]+)" "\\1" runtime_version_major ${runtime_version_major_line})
string(REGEX REPLACE ".*RuntimeProductMinorVersion *([0-9]+)" "\\1" runtime_version_minor ${runtime_version_minor_line})
string(REGEX REPLACE ".*RuntimeProductPatchVersion *([0-9]+)" "\\1" runtime_version_patch ${runtime_version_patch_line})

set(VERSION "\"${runtime_version_major}.${runtime_version_minor}.${runtime_version_patch}.0\"")
set(FULL_VERSION ${product_version_string})
### End of extract version

######################################
# OS SPECIFIC CHECKS
######################################
if(TARGET_IOS OR TARGET_ANDROID)
  # FIXME: the mobile products use mono_dllmap_insert so allow this
  unset(DISABLE_DLLMAP)
else()
  set(DISABLE_DLLMAP 1)
endif()
### End of OS specific checks

add_subdirectory(mono)

configure_file(cmake/config.h.in config.h)
configure_file(cmake/eglib-config.h.cmake.in mono/eglib/eglib-config.h)  # TODO: eglib-config.h is not needed, we're using hardcoded eglib-config.hw
