# The Android case of Snail.MCP.BuildBridge: the .NET SDK, a JDK, the Android SDK and the maui-android workload.
# Built on a host by build_provision as s3l-build-android:<sdk>; a build_run of kind android runs dotnet inside it.
# The Android SDK's tools come for amd64 only, so the image is amd64 and runs under emulation on an Apple Silicon host.
ARG SDK=10.0
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:${SDK}

ARG CMDLINE_TOOLS=11076708
ARG PLATFORMS="platforms;android-36 platforms;android-35"
ARG BUILD_TOOLS="build-tools;36.0.0"

ENV ANDROID_HOME=/opt/android \
    JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/root/.nuget/packages

RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jdk-headless unzip curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

RUN mkdir -p ${ANDROID_HOME}/cmdline-tools \
 && curl -fsSL "https://dl.google.com/android/repository/commandlinetools-linux-${CMDLINE_TOOLS}_latest.zip" -o /tmp/cmdline-tools.zip \
 && unzip -q /tmp/cmdline-tools.zip -d ${ANDROID_HOME}/cmdline-tools \
 && mv ${ANDROID_HOME}/cmdline-tools/cmdline-tools ${ANDROID_HOME}/cmdline-tools/latest \
 && rm /tmp/cmdline-tools.zip

ENV PATH=${PATH}:${ANDROID_HOME}/cmdline-tools/latest/bin:${ANDROID_HOME}/platform-tools

RUN yes | sdkmanager --licenses >/dev/null \
 && sdkmanager "platform-tools" ${PLATFORMS} ${BUILD_TOOLS}

RUN dotnet workload install maui-android

WORKDIR /work
