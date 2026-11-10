# ShotKit

Ferramenta de captura de tela para Windows, com anotações, OCR, imagens fixadas na tela e captura com rolagem.

Sem telemetria, sem anúncios e sem compras: nenhum dado sai do seu computador.

## Recursos

- **Captura de área, janela ou tela inteira**, com suporte a vários monitores e DPI diferentes.
- **Anotações** direto na captura: retângulo, elipse, linha, seta, lápis, marca-texto, texto, numeração, mosaico e desfoque. Tem cor, espessura, preenchimento, desfazer e refazer.
- **OCR**: extrai o texto de qualquer parte da tela usando o reconhecimento de texto do próprio Windows, que funciona offline.
- **Fixar na tela**: deixa a captura flutuando sobre as outras janelas.
- **Captura com rolagem**: junta várias telas de uma página longa numa única imagem.
- **Salvar ou copiar** em PNG, JPG ou BMP.
- Fica na **bandeja do sistema**, com atalhos globais configuráveis e opção de iniciar com o Windows.

## Instalação

1. Baixe o `ShotKit-Setup-<versão>.exe` mais recente na página de [Releases](https://github.com/RafaelCelso/Shot-Kit/releases).
2. Execute o instalador. Ele não precisa de permissão de administrador nem do .NET instalado.

> O instalador ainda não tem assinatura digital, por isso o Windows SmartScreen pode mostrar o aviso "O Windows protegeu o computador". Clique em **Mais informações → Executar assim mesmo**.

**Requisitos:** Windows 10 versão 2004 ou mais recente, ou Windows 11 (64 bits).

## Atalhos padrão

| Ação                    | Atalho           |
| ----------------------- | ---------------- |
| Capturar área           | `PrintScreen`    |
| Extrair texto (OCR)     | `Ctrl + Alt + O` |
| Capturar tela inteira   | `Ctrl + Alt + F` |
| Captura com rolagem     | `Ctrl + Alt + R` |

Você pode alterar os atalhos em **Configurações**, no menu do ícone da bandeja.

> **Windows 11:** por padrão, a tecla PrintScreen abre a Ferramenta de Captura do Windows. Para usá-la no ShotKit, desative *"Usar a tecla Print Screen para abrir a captura de tela"* em **Configurações → Acessibilidade → Teclado**.

### Durante a captura

| Ferramenta / ação   | Tecla              |
| ------------------- | ------------------ |
| Retângulo           | `R`                |
| Elipse              | `E`                |
| Linha               | `L`                |
| Seta                | `A`                |
| Lápis               | `P`                |
| Marca-texto         | `H`                |
| Texto               | `T`                |
| Numeração           | `N`                |
| Mosaico             | `M`                |
| Desfoque            | `B`                |
| Desfazer / refazer  | `Ctrl + Z` / `Ctrl + Y` |
| Fixar na tela       | `F3`               |
| Salvar              | `Ctrl + S`         |
| Copiar e fechar     | `Enter` ou `Ctrl + C` |
| Cancelar            | `Esc`              |

## Onde ficam os arquivos

- **Capturas salvas:** `Imagens\ShotKit`, ou a pasta que você escolher nas Configurações.
- **Configurações e log:** `%LocalAppData%\ShotKit`.
- **Programa instalado:** `%LocalAppData%\Programs\ShotKit`.

## Para desenvolvedores

### Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php), só para gerar o instalador: `winget install JRSoftware.InnoSetup`

### Executar em modo de desenvolvimento

```powershell
dotnet run --project src\ShotKit
```

### Gerar o instalador

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

O script publica o app com o .NET embutido (*self-contained*) e compila o instalador. O resultado sai em `artifacts\installer\ShotKit-Setup-<versão>.exe`.

A versão vem do campo `<Version>` em `src\ShotKit\ShotKit.csproj`.

### Estrutura

```
src/ShotKit/
  App.xaml(.cs)   Bandeja, atalhos globais, instância única
  Native/         Chamadas Win32 (P/Invoke)
  Services/       Captura, OCR, exportação, configurações, rolagem
  Views/          Janelas: sobreposição de captura, OCR, imagem fixada, configurações
installer/
  ShotKit.iss     Script do Inno Setup
  build.ps1       Publica o app e gera o instalador
```

## Licença

Distribuído sob a licença MIT. Veja o arquivo [LICENSE](LICENSE).
