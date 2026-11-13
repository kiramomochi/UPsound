# UPsound

Reforço de volume do Windows de 100% até 500%. Fica no canto da barra de tarefas, perto do relógio,
e roda em segundo plano gastando quase nada.

![ícone](assets/UPsound.png)

## Instalar

Baixe o **`UPsound-Setup.exe`** (12 MB) na página de [Releases](../../releases/latest) e rode.
Ele já traz tudo:

1. Instala o UPsound em `C:\Program Files\UPsound` e põe um atalho no menu Iniciar.
2. Instala o **Equalizer APO 1.4.2** (o motor de áudio, gratuito, GPL), se ainda não houver.
   O Equalizer APO abre uma janela própria: marque seus alto-falantes ou fones e clique em OK.
3. Liga o UPsound ao motor, reinicia o áudio do Windows e abre o app no canto.

Um único pedido de administrador. Como o instalador não tem assinatura digital, o Windows pode
mostrar o aviso do SmartScreen: clique em **Mais informações > Executar assim mesmo**. Para remover: Configurações > Aplicativos > UPsound (pergunta se
quer remover o Equalizer APO junto).

## Usar

- **Clique** no ícone de três barras perto do relógio: abre ou esconde a janela.
- **Botão direito** no ícone: predefinições (100 a 500%), ligar/desligar, iniciar com o Windows, Sair.
- **Fechar a janela** (botão vermelho, Esc ou Alt+F4) só a esconde: o reforço continua.
- **Sair** (menu do ícone) fecha o UPsound e, por padrão, volta o som a 100%.
- De qualquer lugar: **Ctrl + Alt + PgUp / PgDn** ajusta 10%, **Ctrl + Alt + Home** liga e desliga.
- Na janela: arraste ou clique na cápsula, roda do mouse, setas (5%), PgUp/PgDn (25%), Home/End.

Na bandeja, sem a janela aberta, o UPsound usa menos de 1 MB de memória ativa e não usa processador
parado. Com "Iniciar com o Windows", ele já entra em segundo plano ao ligar o PC.

## Como funciona

O Equalizer APO processa todo o som do Windows antes de ele chegar ao dispositivo. O UPsound escreve
`UPsound.txt` na pasta de configuração dele com o ganho em decibéis (`Preamp: 8.00 dB` = 250%), e o
`config.txt` inclui esse arquivo. O Equalizer APO aplica na hora, sem atraso.

- 100% = 0 dB · 200% = +6 dB · 300% = +9,5 dB · 500% = +14 dB.
- Acima de 300% o som pode distorcer em músicas e filmes já altos: é o limite do sinal digital.
- Numa instalação nova, o instalador tira a configuração de exemplo do Equalizer APO (que abaixava o
  som em 6 dB e reforçava graves). Ela fica salva em `config.txt.antes-do-UPsound`.
- Se o UPsound for encerrado à força (Gerenciador de Tarefas), o último nível fica valendo até ele
  abrir de novo.

Preferências: `%APPDATA%\UPsound\settings.ini`.

## Compilar

Usa o compilador C# que já vem no Windows 10/11 (.NET Framework 4.8), sem nada para instalar. Na
primeira compilação, `tools\get-apo.ps1` baixa o instalador oficial do Equalizer APO do SourceForge
para `vendor\` e confere o MD5 publicado pelo projeto (`410aab9749ae4673b950bc29a4eb226f`).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `dist\UPsound.exe` (o app, 90 KB) e `dist\UPsound-Setup.exe` (instalador com app e motor).
`tools\make-icon.ps1` gera o ícone do app e os da bandeja.

Capturas sem abrir janela:

```powershell
dist\UPsound.exe --snapshot app.png --theme dark --level 250 --config-dir C:\pasta\de\teste
dist\UPsound-Setup.exe --theme light --snapshot setup.png welcome
```

## Licença do motor

O Equalizer APO é GPLv2 e vai sem modificações. O instalador grava um aviso com o link da licença e
do código-fonte em `C:\Program Files\UPsound`. Para distribuir o UPsound comercialmente, inclua
também o código-fonte do Equalizer APO 1.4.2 (`EqualizerAPO-src-1.4.2.zip`, no mesmo SourceForge).

## Arquivos

- `src\Controller.cs`: segundo plano, bandeja, atalhos globais, memória.
- `src\Tray.cs`: ícone e menu da bandeja (API do Windows, sem WinForms).
- `src\MainWindow.cs`: a janela, que abre no canto.
- `src\Controls.cs`: cápsula, interruptor, controle segmentado, botões.
- `src\Engine.cs`: integração com o Equalizer APO.
- `src\Install.cs`: inicialização com o Windows, desinstalação.
- `setup\`: o instalador (janela e parte que roda como administrador).
