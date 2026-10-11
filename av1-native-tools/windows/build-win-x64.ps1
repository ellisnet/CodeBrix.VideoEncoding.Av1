# =============================================================================================
# build-win-x64.ps1 - build codebrix_av1.dll for the win-x64 runtime identifier
# =============================================================================================
#
#   Written on Linux on 2026-10-10 as a copy of CodeBrix.Audio.FlacWriter's script of the same
#   name (run successfully, unchanged, by Jeremy that day on his x64 Windows 11 machine), adapted to
#   libaom. FIRST RUN for libaom the same day, on the same machine (Visual Studio Professional 2026
#   18.10.3, cl 19.51, cmake 4.4.3, Strawberry Perl 5.42.3, NASM 3.02): built, passed the whole
#   gate including the 1871-check smoke test, and was adopted, with this script unchanged and no
#   fix needed (see ..\BUILD-PROVENANCE.txt). If something breaks on another machine, fix it IN THE
#   SCRIPT (or build-common.ps1) and record it there.
#
# USAGE (from any PowerShell prompt - the script sets up the compiler environment itself):
#
#     cd av1-native-tools\windows
#     .\build-win-x64.ps1              # build, gate, and (gate COMPLETE) adopt into src\
#     .\build-win-x64.ps1 -NoAdopt     # build + gate only
#
# MSVC (cl) through CMake's Visual Studio generator, -A x64, static CRT. Output:
# ..\output\win-x64\ ; adopted into ..\..\src\CodeBrix.VideoEncoding.Av1\runtimes\win-x64\native\
# (codebrix_av1.dll + LICENSE-Aom.txt), the .pdb into ..\unstripped\win-x64\.
# It installs nothing. Anything missing is named, with the command that installs it.
# Prerequisites: Visual Studio 2022+ (or Build Tools) with "Desktop development with C++",
# cmake >= 3.16, Perl and nasm on PATH (libaom's CMake runs Perl; its x86-64 assembly needs nasm -
# the same nasm the dav1d builds use).
# =============================================================================================

[CmdletBinding()]
param(
    # Where cmake builds and the gate runs. Kept out of the repository.
    [string] $BuildRoot = (Join-Path $env:TEMP 'codebrix-av1-build-win-x64'),
    [switch] $NoAdopt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\build-common.ps1"

if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw "build-win-x64.ps1 runs on an x64 Windows machine; this host is $env:PROCESSOR_ARCHITECTURE."
}

Invoke-WindowsAomBuild -Rid 'win-x64' -Platform 'x64' -AomTargetCpu 'x86_64' -ExpectedMachine 0x8664 -VcVarsArch 'x64' `
                          -Route 'native (x64 host)' -CanRun $true -BuildRoot $BuildRoot `
                          -ScriptName 'build-win-x64.ps1' -NoAdopt:$NoAdopt
