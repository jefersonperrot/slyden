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
- [x] Revisão do usuário (11/11/2026): remover fotos (botão e tecla Delete; só tira do projeto), mover ◀ ▶, + Fotos (reincluir da pasta), tela de Projetos (novo, abrir, duplicar, excluir com confirmação), Salvar (botão e Ctrl+S; salva sozinho ao trocar de projeto ou fechar), cancelar exportação, botão "Criar capa nesta foto", correção do campo de texto que perdia o foco a cada letra
- [x] Projetos em `%APPDATA%\Slyden\Projects\*.slyden` (`ProjectStore`); 133 testes
- [x] Segunda revisão do usuário (11/11/2026): tela Novo projeto (nome, formato, resolução 720p/1080p/1440p/4K, pasta opcional), seleção múltipla (Ctrl/Shift/clique alterna) + Selecionar todas + remover várias, arrastar e soltar fotos/pastas do Explorador, fotos de qualquer pasta (caminho completo quando fora da pasta do projeto), tamanho da foto 30–100% (padrão + por foto) com fundo desfocado ou preto; 148 testes
- [ ] Conferir a tela visualmente depois dessas mudanças
- [x] Reordenar: arrastar fotos (uma ou várias selecionadas) na linha do tempo com marcador azul e rolagem automática nas bordas; ◀ ▶ movem várias de uma vez; `ListReorder` testado (156 testes)
- [x] Prévia animada no app (botão Reproduzir): toca a partir da foto selecionada com efeitos, transições, textos e música (NAudio, só Windows); relógio = música; `SlideshowPlan` é compartilhado com a exportação (mesmo roteiro e mesmo desenho); ~18 ms por quadro a 720 px; 163 testes
- [ ] **Testar a prévia animada** (tocar, parar, trocar de foto durante a reprodução, música longa) e conferir a sincronia com o som
- [ ] Prévia: pausar/retomar, barra para pular para um ponto, tecla Espaço, volume
- [ ] Prévia: quadros ocasionais perdidos nas trocas com fade (p95 ~37 ms); usar miniaturas reduzidas das fotos para acelerar
- [ ] Música em outros sistemas (hoje o som da prévia usa NAudio, só Windows)
- [ ] Aplicar ajustes do painel (efeito, tamanho etc.) a todas as fotos selecionadas
- [ ] Números de posição nos cartões da linha do tempo; atalhos "Para o início/fim"
- [ ] Resolução do vídeo no app depois de criado (hoje só na criação do projeto)
- [x] Fundo desfocado mais claro, estilo CapCut (escurecimento 45% → 10%); `background_blur` e `background_dim` no padrão do arquivo (ainda sem controle na tela)
- [x] Clique simples seleciona uma foto só; Ctrl/Shift para várias
- [x] Sombra atrás da foto reduzida (`shadow`, controle "Sombra da foto" na aba Padrão, padrão 50%); atalho Espaço toca/para a prévia (165 testes)
- [x] "Pasta do projeto" no Novo projeto: o `.slyden` é gravado na pasta escolhida (registrada em `projects.json` para aparecer na lista) e o vídeo exportado sugere essa pasta; 168 testes
- [x] Fundo de cor lisa (opção "Cor" + amostras e campo #RRGGBB na aba Padrão; por foto escolhe "Cor" e usa a cor padrão); 170 testes
- [x] Exportação mostra porcentagem, tempo restante estimado e tempo total ao terminar
- [x] Exportar escolhe formato e resolução na hora (começa igual ao projeto; não altera o projeto)
- [x] Render ~3x mais rápido: quadros desenhados em paralelo (um por núcleo) e entregues em ordem ao ffmpeg, preset x264 `veryfast` (30 fotos/15 s: 1080p 33 s → 12 s; 4K 126 s → 43 s; saída idêntica quadro a quadro)
- [x] Efeito começa já no primeiro quadro (curva quase linear) e intensidade mínima de 1% (antes 5%)
- [x] Seletor de cor completo ("Mais cores…": espectro, paleta, sliders e hexadecimal)
- [x] Modos da música (aba Padrão > Música): "Ajustar os slides à música" (padrão; espalha as fotos pela música toda, trocas nas batidas, vale com ritmo Automático) e "Cortar a música onde os slides acabam"; fade-in e fade-out da música (0–10 s) na exportação; 174 testes
- [x] Segundos por foto (sem música) digitados livremente (0,5 a 120 s), não mais opções fixas
- [x] Botão Remover música (aparece quando há música) e aviso na aba Padrão quando segundos por foto estão definidos mas há música (os segundos são ignorados)
- [x] Desfazer/Refazer (botões no topo, Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z): até 100 passos, alterações em sequência rápida (sliders, digitação) contam como um; cobre fotos, ajustes, textos e música; zerado ao abrir outro projeto
- [x] Remover música pede confirmação (2º clique em 4 s)
- [ ] Segundos por foto COM música (trocar na batida mais próxima de cada N segundos) — avaliar
- [ ] Prévia animada ainda não aplica o fade-in/out da música (só a exportação)
- [ ] Render: 4K ainda ~10 quadros/s (sombra custa ~40%); avaliar sombra pré-calculada, reduzir fotos grandes antes do desenho, codificador de hardware
- [ ] Cor de fundo diferente por foto
- [ ] Abrir projeto `.slyden` de qualquer lugar (Arquivo > Abrir); mover/alterar a pasta de um projeto existente
- [ ] Controles de desfoque e brilho do fundo no painel; sombra por foto
- [ ] Renomear projeto; botão para escolher a foto de capa e levá-la ao início
- [ ] Onda da música com marcas de batida na linha do tempo
- [x] Arrastar o texto na prévia (com ímã no centro), seletor de fonte com as fontes do computador (cada uma mostrada no próprio estilo), botões Centro horizontal / Centro do quadro e clique duplo nos sliders volta ao padrão; 178 testes
- [x] Texto selecionado ganha moldura tracejada na prévia; animação do texto com controles (aparece após X s, fade de entrada, fica visível por Y s, fade de saída; vale em slides contínuos) e capa de exemplo com textos entrando em sequência; configuração padrão do usuário (Salvar como padrão / Voltar ao padrão de fábrica; estilo padrão dos textos novos) em %APPDATA%\Slyden\defaults.json; 187 testes
- [x] Tela de Configurações (botão ⚙ no topo, fora dos projetos): formato, resolução, efeitos, transição, foto/fundo/sombra, música e estilo dos textos novos, salvos na hora; a seção no painel Padrão saiu (menos poluição)
- [x] Presets com nome (ex.: Casamento, Clientes): Configurações > Presets (novo, duplicar, novo a partir do projeto aberto, renomear, excluir, definir o padrão); a tela Novo projeto escolhe o preset; o projeto guarda o preset usado (textos novos seguem o estilo dele); o defaults.json antigo vira o primeiro preset; 189 testes
- [x] Barra de menus (Arquivo, Editar, Projeto, Ajuda com atalhos e Sobre); o topo ficou só com Desfazer/Refazer/Salvar/Exportar
- [x] Tela de projetos em grade de cartões com miniatura (1ª foto), cartão + Novo projeto, busca e ordenação; botões Desfazer/Salvar/Exportar só aparecem com um projeto aberto
- [ ] Tela de projetos: coluna lateral (exportações recentes, abrir .slyden de qualquer pasta, atalhos de presets); botões do cartão só ao passar o mouse
- [x] **Otimizador do tamanho do vídeo (feito):** seletor Qualidade e tamanho na exportação — Rápida (H.264 veryfast CRF18), Equilibrada (H.264 medium CRF22, padrão, ~35% menor que a Rápida com o mesmo VMAF ~95), Compacta (H.265 fast CRF22, ~60% menor, mais lenta, tag hvc1); 197 testes. Resultados do 2º teste (60 s, 1080p): x264 vf crf18 23,6 MB/VMAF 95,0/16 s; x264 med crf22 15,4 MB/94,9/31 s; x264 slow crf21 17,9 MB/95,5/42 s; x265 fast crf22 9,0 MB/94,8/43 s; x265 med crf20 17,0 MB/95,8/78 s. Primeiro teste (24 fotos, 2 min): H.264 CRF20 veryfast 39,5 MB/VMAF 93,9; H.265 CRF22 medium 29,0 MB/95,1
- [ ] Otimizador, para depois: repetir a medição (VMAF) em mais 2 ou 3 projetos (paisagem, retrato, fotos escuras) antes de confiar nas porcentagens; tamanho-alvo (ex.: "caber em 200 MB"); lembrar a última qualidade escolhida; codificadores de hardware (NVENC/AMF/QSV); otimizar também o JPEG das fotos de entrada
- [ ] Sombra das fotos: controle da força da sombra quando o tamanho da foto está abaixo de 100% (hoje só o padrão global de 50%)
- [ ] Tela de boas-vindas (primeiro uso): apresentar o app, criar o primeiro projeto, escolher pasta dos projetos e preset
- [x] Clicar no logo SLYDEN leva à tela de projetos
- (histórico do teste) Teste (24 fotos, 1080p, 2 min, comparado ao mestre sem perdas; VMAF 95+ ≈ imperceptível): H.264 CRF20 veryfast 39,5 MB/VMAF 93,9 (padrão atual); H.264 CRF20 medium 48,8 MB/95,6; CRF20 slow 48,1 MB/95,8; CRF23 slow 30,9 MB/94,5; CRF18 slow 64,2 MB/96,3; H.265 CRF22 medium 29,0 MB/95,1 (146 s); H.265 CRF26 medium 15,4 MB/93,0. Próximos passos: achar o CRF de cada codec que empata em VMAF ~95 e comparar tamanhos; seletor Qualidade e tamanho na exportação (Rápida / Equilibrada / Compacta, H.265 opcional); tamanho-alvo (ex.: 200 MB)
- [x] Duração própria por slide (aba Slide > Duração própria (s); com música arredonda para a batida, sem música é exata) e capa antes da música (1ª foto: capa dura X s sem música, depois a música e as fotos começam; fade-in da imagem em segundos, independente do fade dos textos); prévia e exportação; 196 testes
- [x] Várias músicas no projeto (etapa 1): lista na aba Padrão > Músicas (adicionar várias de uma vez, subir/descer, tirar), início e fim de cada uma ajustados à batida, cruzamento (fade) entre elas e a próxima entra numa batida da anterior; as batidas das faixas viram uma análise só (`MusicBed`), então os cortes das fotos seguem a música que está tocando; prévia e exportação usam a trilha montada; barra colorida com um bloco por música; projetos antigos com uma música continuam abrindo; 207 testes
- [x] Botão ▶ Ouvir em cada música: toca só o trecho escolhido, com marcador amarelo andando na onda; para sozinho no fim, ao editar ou ao tocar a prévia
- [x] Música por trecho: a partir da 2ª música, campo Entra na foto nº (0 = automático, proporcional ao tempo); com algum valor preenchido, as fotos de cada trecho se espalham só pela música dele e a troca de música cai na troca de foto (a info de cada música mostra otos X a Y); vale na prévia e na exportação; 210 testes. Nesse modo o ritmo é sempre ajustado à música (ignora batidas por foto fixas)
- [x] Sons por cima da música (aba Padrão > Sons por cima): voz, efeitos etc.; cada som entra no início de uma foto (Entra na foto nº + espera), com corte (início/fim do trecho), volume e quanto a música abaixa (rampa suave de 0,35 s); a prévia toca o mesmo áudio montado da exportação (AudioMix); funciona também sem música; 215 testes
- [x] Onda e botão ▶ Ouvir em cada som por cima (alças de início/fim, igual às músicas)
- [ ] Sons por cima, depois: mostrar na linha do tempo onde cada som toca; arrastar o som para outra foto
- [x] Faixa da trilha de música abaixo da linha do tempo das fotos (MusicStripView): onda da trilha inteira, marcas das batidas, divisão e nome de cada música; clicar numa música abre a aba Padrão, rola até ela e a destaca
- [ ] Faixa da trilha, depois: marcar onde cada foto troca e onde cada som por cima toca; marcador de posição durante a prévia; clicar para pular a prévia até ali
- [ ] **Testar as músicas na tela** (adicionar 2 ou 3, cortar início/fim, subir/descer, tocar a prévia, exportar)
- [x] Onda do áudio em cada música (alças brancas arrastáveis para início e fim, grudam nas batidas; trecho fora do corte escurecido); 208 testes
- [ ] Músicas, próximas etapas: onda da trilha inteira com marcas das fotos
- [ ] Configurações: pasta padrão dos projetos, pasta de fontes extras, idioma, atalhos
- [ ] Mais estilos de animação de texto (subir suavemente, digitar, zoom); várias configurações salvas (modelos com nome)
- [ ] Arrastar também o tamanho do texto (alças); cursor de mão ao passar sobre o texto; fontes dos textos de exemplo da capa que existam também no Mac
- [ ] Incluir fotos de outras pastas (hoje só da pasta do projeto)
- [ ] Reproduzir a prévia com música; aviso de rosto cortado na foto
- [ ] Miniaturas mais rápidas (hoje decodifica a foto inteira; usar miniatura embutida/decodificação reduzida)
- [ ] Exportar vários formatos de uma vez; otimizador de tamanho
- [ ] Ícone, menus (Arquivo, Editar, Ajuda) e instalador

## A fazer — produto e negócio
- [ ] Definir o nome do produto (hoje é nome de trabalho)
- [x] **Licença do ffmpeg (decidido):** manter o build GPL (x264/x265) chamado como programa separado; o instalador inclui o aviso de licença do ffmpeg, o link do código-fonte da build usada e permite trocar o ffmpeg.exe. Plano B: build LGPL + codificador de hardware. Conferir com um especialista antes de vender (patentes H.264/HEVC)
- [ ] Validar demanda: vídeo de demonstração + página simples com preço (ex.: R$ 29–39/mês) mostrada a outros fotógrafos
- [ ] Música: manter por conta do usuário; depois avaliar catálogo licenciado (perguntar por escrito ao fornecedor se pode embutir no app)
- [ ] Assinatura/licença e instalador (por último)
- [ ] Criar repositório remoto no GitHub e enviar

