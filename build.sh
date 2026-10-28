#!/usr/bin/env bash
# Cross-compile WindowsPersonalizer.exe from Linux or macOS (needs the .NET 8 SDK).
set -euo pipefail
cd "$(dirname "$0")"
dotnet publish src/WindowsPersonalizer/WindowsPersonalizer.csproj -c Release -o release
echo "Built: release/WindowsPersonalizer.exe"
