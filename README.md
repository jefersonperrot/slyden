# Slyden (nome de trabalho)

Programa para fotógrafos criarem slideshows em vídeo com a troca de foto sincronizada com a batida da música.
Alternativa mais acessível ao Smart Slides para o mercado brasileiro.

## Diferenciais pretendidos
- Efeitos de movimento (zoom/pan) configuráveis: um efeito para todas as fotos, vários, ou nenhum.
- Padrão global + ajuste por foto (efeito, intensidade, transição).
- Transições opcionais (corte seco ou fade que termina na batida).
- Formatos 16:9 (YouTube) e 9:16 (Reels/Stories).
- Música fornecida pelo usuário (catálogo licenciado fica para depois de validar a demanda).
- Processamento local, em português, preço em reais.

## Estado atual
Protótipo em Python (`prototype-python/`) que valida a ideia: detecta as batidas com librosa, monta o vídeo
com Pillow e codifica com ffmpeg. Serve de especificação para o app definitivo.

A decisão é fazer o produto **todo em C# (.NET + Avalonia)**, sem Python embutido. Ver `TODO.md`.

## Motor em C# (em andamento)
```
src/Slyden.Core    motor: áudio, detecção de batida, plano de trocas, configuração e render (SkiaSharp + ffmpeg)
src/Slyden.Cli     linha de comando para testar o motor
tests/Slyden.Tests testes xUnit
```
Precisa do .NET 9 SDK e de um `ffmpeg` (ao lado do programa, no PATH, ou na variável `SLYDEN_FFMPEG`).
```
dotnet test
dotnet run --project src/Slyden.Cli -- beats musica.mp3 --photos 30 --json batidas.json
dotnet run --project src/Slyden.Cli -- render pasta_fotos saida.mp4 --audio musica.mp3 --config config.json --format 9:16
```
Sem `--audio` o vídeo sai mudo e usa o `bpm` da configuração. Se o `--config` não existir, é criado com os padrões
(efeitos, intensidade, transição e exceção por foto) para você editar. O formato do arquivo é o mesmo de
`prototype-python/exemplo-config.json`.

### Telão (sem música, tempo fixo)
Com `"seconds_per_photo": 8` (e opcionalmente `"fade_seconds": 1`) cada foto dura o tempo indicado, sem depender de BPM.
Vale só sem `--audio`. Formatos: `16:9`, `9:16` e `1:1`.

### Capa e textos
Cada foto aceita `length` (ciclos de batida, para a capa durar mais), `dim` (0–1, escurece a foto) e `texts`:
```json
{ "file": "capa.jpg", "length": 2, "dim": 0.4,
  "texts": [
    { "text": "Wedding day", "font": "Segoe Script", "size": 0.085, "align": "left", "x": 0.08, "y": 0.30 },
    { "text": "Maria e João", "font": "Georgia", "bold": true, "size": 0.12, "align": "center", "color": "#FFF1CC", "span": 2 },
    { "text": "11/11/2026", "font": "Century Gothic", "size": 0.04, "align": "right", "x": 0.92, "y": 0.90 }
  ] }
```
`size` é relativo ao menor lado do quadro; `x`/`y` são frações do quadro; `span` faz o texto continuar nos slides seguintes.
Fontes: instaladas no Windows, na pasta `fonts` ao lado do programa ou em `--fonts pasta`. Fonte inexistente gera um aviso.

## Rodar o protótipo Python (referência)
```
pip install -r prototype-python/requirements.txt
python prototype-python/music.py <pasta_fotos> <musica.mp3> <saida.mp4> <config.json>
```
Veja `prototype-python/exemplo-config.json` para o formato da configuração.

> Fotos de clientes e músicas com direitos autorais não são versionadas (ver `.gitignore`).
