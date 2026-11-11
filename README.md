# SlideBeat (nome de trabalho)

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
src/SlideBeat.Core    motor: áudio, detecção de batida, plano de trocas
src/SlideBeat.Cli     linha de comando para testar o motor
tests/SlideBeat.Tests testes xUnit
```
Precisa do .NET 9 SDK e de um `ffmpeg` (ao lado do programa, no PATH, ou na variável `SLIDEBEAT_FFMPEG`).
```
dotnet test
dotnet run --project src/SlideBeat.Cli -- beats musica.mp3 --photos 30 --json batidas.json
```

## Rodar o protótipo Python (referência)
```
pip install -r prototype-python/requirements.txt
python prototype-python/music.py <pasta_fotos> <musica.mp3> <saida.mp4> <config.json>
```
Veja `prototype-python/exemplo-config.json` para o formato da configuração.

> Fotos de clientes e músicas com direitos autorais não são versionadas (ver `.gitignore`).
