#!/usr/bin/env bash
# Prepara um ambiente Linux (ex.: sessões na nuvem) para COMPILAR o projeto.
# O app só roda no Windows, mas compilar no Linux já pega erros de C#/XAML.
# O SDK do Ubuntu não traz o "WindowsDesktop SDK" (WPF); copiamos ele do pacote da Microsoft.
set -euo pipefail

if ! command -v dotnet >/dev/null; then
  apt-get update -qq && apt-get install -y -qq dotnet-sdk-8.0
fi

SDK_DIR=$(dotnet --list-sdks | awk '/^8\./{v=$1; p=$2} END{gsub(/[\[\]]/,"",p); print p"/"v}')
if [ ! -d "$SDK_DIR/Sdks/Microsoft.NET.Sdk.WindowsDesktop" ]; then
  tmp=$(mktemp -d)
  curl -sSL -o "$tmp/sdk.deb" https://packages.microsoft.com/ubuntu/22.04/prod/pool/main/d/dotnet-sdk-8.0/dotnet-sdk-8.0_8.0.129-1_amd64.deb
  dpkg-deb -x "$tmp/sdk.deb" "$tmp/x"
  cp -r "$tmp"/x/usr/share/dotnet/sdk/*/Sdks/Microsoft.NET.Sdk.WindowsDesktop "$SDK_DIR/Sdks/"
  rm -rf "$tmp"
fi
echo "Pronto: rode 'dotnet build src/BottleneckHunter -c Release'"
