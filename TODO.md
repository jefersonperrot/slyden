# TODO — Slyden

Atualizar este arquivo a cada etapa concluída ou nova ideia.

## Feito
- [x] Validar a ideia com protótipo em Python
- [x] Troca de foto na batida com música real (librosa; início do compasso por fase mais forte)
- [x] Efeitos de movimento: none, zoom_in, zoom_out, pan_lr, pan_rl, tilt_up, tilt_down
- [x] Padrão global + exceção por foto (efeito, intensidade, transição)
- [x] Transições: corte seco e fade que termina exatamente na batida
- [x] Fotos com proporção diferente do quadro: foto inteira sobre fundo desfocado
- [x] Formato vertical 9:16 (além do 16:9)
- [x] Decisão de tecnologia: tudo em C# (.NET + Avalonia), sem Python embutido
- [x] Repositório Git criado
- [x] Decisão: Python não será distribuído com o produto (fica só como referência/teste de comparação da detecção de batida)

- [x] Solução .NET 9 criada: `Slyden.Core` (motor), `Slyden.Cli`, `Slyden.Tests` (xUnit)
- [x] Áudio via ffmpeg para PCM mono (`AudioDecoder`, `FfmpegLocator`)
- [x] Detecção de batida em C# (FFT própria, mel, força de onset, tempo, programação dinâmica) — sem dependências externas
- [x] Início do compasso (fase mais forte) e troca a cada 2/4/8 batidas ("auto") em `CutPlanner`
- [x] Comparado com o protótipo na mesma música: tempo idêntico (136 BPM) e as 30 trocas coincidem (0 ms); o C# só começa uma troca antes
- [x] 11 testes automatizados passando; detecção de 244 s de áudio em ~2 s
- [x] Render em C# (SkiaSharp + ffmpeg): efeitos, padrão + exceção por foto, corte e fade na batida, 16:9 e 9:16, fundo desfocado, orientação EXIF
- [x] Configuração em JSON (`SlideshowConfig`) compatível com o arquivo do protótipo, com validação e mensagens de erro claras
- [x] CLI: `slyden render <fotos> <saida.mp4> [--audio] [--config] [--format]`; grava em arquivo `.partial.mp4` e só renomeia no fim (cancelar não deixa vídeo pela metade)
- [x] Paridade com o protótipo: mesmos quadros (PSNR 41–47 dB) com a mesma configuração, sem música
- [x] 67 testes automatizados (config, geometria, linha do tempo, desenho em pixels, EXIF, batida)
- [x] Corrigido "tranco" nos movimentos laterais/verticais: a foto ficava parada e arrancava de repente porque o movimento pedia mais do que o zoom permitia (a borda cortava). Agora o movimento usa exatamente a margem livre da foto. O protótipo Python ainda tem esse defeito (não será corrigido; serve só de referência)

- [x] Capa e textos sobre os slides (motor): blocos de texto por foto com fonte, tamanho, cor, negrito/itálico, alinhamento, posição, sombra, contorno, quebra automática e redução para caber; fade de entrada/saída na batida; `span` para o texto continuar em vários slides; `length` para um slide durar vários ciclos (capa mais longa); `dim` para escurecer a foto sob o texto
- [x] Fontes: pasta `fonts` ao lado do programa, `--fonts pasta` ou fontes instaladas; aviso quando a fonte não existe (usa a padrão)
- [x] 115 testes automatizados; capa conferida visualmente em 16:9 e 9:16

## Fazendo
- [ ] (nada em andamento)

## A fazer — motor (C#)
- [ ] Testar a detecção com mais músicas (ritmo lento, andamento variável, sem bateria) e comparar com o protótipo
- [ ] Ajustar o corte de batidas fracas nas pontas (o C# manteve 5 batidas a mais que o protótipo)
- [ ] Desempenho do render: ~8 quadros/s (60 s de vídeo em ~4 min). Medir onde está o gargalo (desenho no Skia x codificação x264) e acelerar (preset, threads, pré-redução de fotos grandes, codificador de hardware)
- [ ] Testar com fotos grandes (6000 px, 20+ MP) e pastas com 300+ fotos (memória)
- [ ] Mensagem útil se o ffmpeg não tiver libx264 / se faltar espaço em disco
- [ ] Conferir compatibilidade do SkiaSharp 4.x com a versão do Avalonia escolhida (ao criar o app)
- [ ] Teste automático de ponta a ponta com ffmpeg (hoje o render completo é conferido manualmente)

## A fazer — qualidade do vídeo
- [ ] Detecção de rosto com AVISO na foto (não corrige sozinho): checar se o rosto fica cortado no enquadramento inicial, final e durante o movimento do efeito; sugerir ajuste (ex.: trocar efeito ou recentrar). Ajuste automático só como botão opcional. Exemplo de teste: rosto cortado no topo na foto 0033
- [ ] Avaliar modelo de rosto em ONNX Runtime (conferir licença do modelo) e testar em fotos de casamento (véu, perfil, de costas)
- [ ] Opção para foto horizontal no formato vertical: inteira com fundo desfocado ou recortada
- [ ] Mais transições (flash, deslizar)
- [ ] Opção de duração por foto manual (fora da batida)
- [ ] Vídeo com várias fotos por tela (colagem) — avaliar

## A fazer — capa e textos sobre os slides
- [ ] Capa sem foto (fundo liso ou desfocado) + textos
- [ ] Ajuste de texto por formato (posição/tamanho diferentes em 16:9 e 9:16); hoje os valores são relativos ao quadro
- [ ] Faixa escura atrás do texto (hoje há sombra, contorno e `dim`)
- [ ] Fontes: incluir no instalador fontes com licença livre (OFL); conferir a licença de cada uma
- [ ] Prévia do texto na interface (arrastar para posicionar)

## A fazer — novas funcionalidades (ideias)
- [x] Formato 1:1 (1080x1080)
- [x] Modo sem música em segundos (telão): `seconds_per_photo` + `fade_seconds`
- [ ] Telão: testar com 300+ fotos (memória/tempo) e avaliar vídeo em loop
- [ ] Duplicar projeto (interface): copia o arquivo de configuração para criar variação (com música / sem música). Opcional: exportar vários formatos de uma vez
- Ideia para depois (fora do escopo atual): clipes de vídeo entre as fotos com corte de início/fim
- [ ] Exportar com otimizador (estilo JpegMini): reduzir o tamanho do vídeo mantendo a qualidade (CRF/preset por conteúdo, tamanho-alvo, comparação de qualidade); também otimizar JPEG das fotos de entrada. Retoma a ideia original do JpegMini

## Interface (Avalonia 12) — primeira versão feita
Direção visual: tema escuro no estilo dos aplicativos Adobe. Maquete aprovada em 11/11/2026. Prévia grande no centro, fotos em linha do tempo embaixo, painel lateral direito.
- [x] Projeto `Slyden.App` (Avalonia 12.1.6); SkiaSharp do motor passou para 3.119.4 (o mesmo do Avalonia); 127 testes passando
- [x] Tela: topo, prévia, formato 16:9/9:16/1:1, controle de movimento, painel (Slide, Texto, Padrão, Exportar), linha do tempo com miniaturas
- [x] Abrir pasta de fotos, escolher música, exportar MP4 com barra de progresso
- [x] Ajuste por foto (efeito, intensidade, transição, ciclos, escurecer) e padrão global; textos (adicionar/remover/editar fonte, tamanho, alinhamento, posição, continuação)
- [ ] **Conferir a tela visualmente** (só foi verificado que abre sem erro; layout e cores ainda não foram vistos)
- [ ] Onda da música com marcas de batida na linha do tempo
- [ ] Reordenar e remover fotos; arrastar o texto na prévia
- [ ] Salvar/abrir projeto (arquivo JSON + pasta) e Duplicar projeto
- [ ] Reproduzir a prévia com música; aviso de rosto cortado na foto
- [ ] Miniaturas mais rápidas (hoje decodifica a foto inteira; usar miniatura embutida/decodificação reduzida)
- [ ] Exportar vários formatos de uma vez; otimizador de tamanho
- [ ] Ícone, menus (Arquivo, Editar, Ajuda) e instalador

## A fazer — produto e negócio
- [ ] Definir o nome do produto (hoje é nome de trabalho)
- [ ] **Licença do ffmpeg:** builds com libx264 são GPL; definir como distribuir (build LGPL com encoder de hardware/OpenH264 ou licença do x264) antes de vender
- [ ] Validar demanda: vídeo de demonstração + página simples com preço (ex.: R$ 29–39/mês) mostrada a outros fotógrafos
- [ ] Música: manter por conta do usuário; depois avaliar catálogo licenciado (perguntar por escrito ao fornecedor se pode embutir no app)
- [ ] Assinatura/licença e instalador (por último)
- [ ] Criar repositório remoto no GitHub e enviar
