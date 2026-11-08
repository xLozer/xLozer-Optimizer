<div align="center">

# ⚡ xLozer Optimizer

**Otimizador de alta performance para Windows focado em jogos e transmissões ao vivo.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/Build-Passing-2ea043.svg)]()

[Português](README.md) | [English](README.en.md)

<p align="center">
  xLozer Optimizer é um utilitário leve e robusto em C# / WPF projetado para eliminar quedas de FPS e micro-stutters durante lives e jogatinas pesadas (FiveM, GTA V, OBS Studio), gerenciando processos em segundo plano e limpando a memória RAM sem comprometer a estabilidade do Windows.
</p>

👉 [Download](#-download) • [Recursos](#-recursos) • [Como Usar](#-como-usar) • [Compilação](#-compilação)

</div>

---

## ✨ Recursos

- **Limpeza Real de Memória RAM:** Esvazia o *Working Set* de aplicações inativas e purga a *Standby List* do Windows diretamente no Kernel (`ntdll.dll`), exibindo o ganho real em MB.
- **Auto-RAM Cleaner:** Monitoramento em tempo real que purga o cache de RAM automaticamente sempre que o uso ultrapassa 85% ou a memória livre cai abaixo de 2 GB.
- **Telemetria de Hardware Completa:** Detecção automática e monitoramento ao vivo de CPU, GPU (Integrada/Dedicada), Memória RAM física, Versão do SO e Armazenamento primário via WMI.
- **Blindagem Inteligente de Processos:** Proteção nativa contra encerramento de componentes do Windows (`ShellHost`, `WebView2`, `dwm`), IDEs (`VS Code`), drivers de vídeo/áudio e subprocessos de jogos e gravadores (FiveM, Cfx.re, OBS Studio).
- **Gerenciador de Serviços do Windows:** Pausa serviços pesados de telemetria e indexação (`SysMain`, `WSearch`, `Windows Update`) travando seu reinício automático durante a sessão.
- **Whitelist Persistente:** Suporte para salvar suas preferências de processos e utilitários de bandeja (como `Twinkle Tray` e `Discord`) em arquivo de configuração local JSON.

---

## 📸 Screenshots

<div align="center">
  <img src="https://i.postimg.cc/bY7Jxt9Y/Screenshot-2026-11-08-191759.png" alt="xLozer Optimizer Dashboard" width="850"/>
</div>

---

## 🚀 Download

1. Acesse a aba **[Releases](https://github.com/xLozer/xLozer-Optimizer/releases)** na barra lateral direita.
2. Baixe o executável autônomo `Stream_Optimizer.exe`.
3. Execute como **Administrador** para permitir a gestão dos privilégios de kernel e serviços do Windows.

---

## 🛠️ Compilação

Para compilar o código-fonte manualmente a partir do repositório:

```powershell
# 1. Clone o repositório
git clone [https://github.com/SEU_USUARIO/xLozer-Optimizer.git](https://github.com/SEU_USUARIO/xLozer-Optimizer.git)
cd xLozer-Optimizer

# 2. Restaure as dependências
dotnet restore

# 3. Compile o executável autônomo (Single-File)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish