@rem Copyright 2016, Google Inc.
@rem All rights reserved.
@rem
@rem Redistribution and use in source and binary forms, with or without
@rem modification, are permitted provided that the following conditions are
@rem met:
@rem
@rem     * Redistributions of source code must retain the above copyright
@rem notice, this list of conditions and the following disclaimer.
@rem     * Redistributions in binary form must reproduce the above
@rem copyright notice, this list of conditions and the following disclaimer
@rem in the documentation and/or other materials provided with the
@rem distribution.
@rem     * Neither the name of Google Inc. nor the names of its
@rem contributors may be used to endorse or promote products derived from
@rem this software without specific prior written permission.
@rem
@rem THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
@rem "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
@rem LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
@rem A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
@rem OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
@rem SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
@rem LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
@rem DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
@rem THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
@rem (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
@rem OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

@rem Generate the C# code for .proto files

setlocal

@rem enter Script.Rpc directory
cd /d %~dp0

set NUGET_PATH=%UserProfile%\.nuget\packages\grpc.tools\1.4.1\tools\windows_x86
set PROTO=.\Proto\FunctionRpc.proto
set MSGDIR=.\Messages

if exist %MSGDIR% rmdir /s /q %MSGDIR%
mkdir %MSGDIR%

set OUTDIR=%MSGDIR%\DotNet
mkdir %OUTDIR%
%NUGET_PATH%\protoc.exe %PROTO% --csharp_out %OUTDIR% --grpc_out=%OUTDIR% --plugin=protoc-gen-grpc=%NUGET_PATH%\grpc_csharp_plugin.exe --proto_path=.\Proto

@rem add pragma warning disable labels to generated files

cd ./Messages/DotNet/

for %%f in ("*.cs") do echo #pragma warning disable> %%f.temp
for %%f in ("*.cs") do type %%f >> %%f.temp
for %%f in ("*.cs") do copy /y %%f.temp %%f
for %%f in ("*.temp") do del %%f

cd ../..

endlocal
