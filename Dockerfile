FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-and-test

# Native Linux libraries required by Avalonia + Skia headless rendering
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    libice6 \
    libsm6 \
    libx11-6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

# Expect context containing sibling backend/ and desktop/ folders (created automatically by setup.ps1 -Docker / setup.sh --docker)
COPY backend /workspace/backend
COPY desktop /workspace/desktop

WORKDIR /workspace/desktop

RUN find /workspace -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + \
 && dotnet restore NephroEdge.Desktop.slnx --disable-parallel \
 && dotnet build NephroEdge.Desktop.slnx --no-restore -c Debug /p:UseSharedCompilation=false -nodeReuse:false -maxCpuCount:1

ENV NEPHROEDGE_SCREENSHOT_DIR=/tmp/nephroedge-screenshots
CMD ["dotnet", "run", "--project", "NephroEdge.Desktop.HeadlessTests", "--no-build"]
