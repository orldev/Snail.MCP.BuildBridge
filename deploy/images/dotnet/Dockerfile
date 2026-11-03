# The Linux case of Snail.MCP.BuildBridge: the .NET SDK with what Native AOT for Linux needs.
# Built on a host by build_provision as s3l-build-dotnet:<sdk>; a build_run of kind linux runs dotnet inside it.
ARG SDK=10.0
FROM mcr.microsoft.com/dotnet/sdk:${SDK}

RUN apt-get update \
 && apt-get install -y --no-install-recommends clang zlib1g-dev git rsync openssh-client \
 && rm -rf /var/lib/apt/lists/*

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/root/.nuget/packages

WORKDIR /work
