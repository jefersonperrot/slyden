# TODO — SlideBeat

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

## Fazendo
- [ ] (nada em andamento)

## A fazer — motor (C#)
- [ ] Criar solução .NET (biblioteca do motor + app Avalonia)
- [ ] Decodificar áudio via ffmpeg para PCM
- [ ] Detecção de batida em C# (força de onset + tempo + programação dinâmica) e comparar com o protótipo
- [ ] Escolha do início do compasso (fase mais forte) e troca a cada 2/4/8 batidas ("auto")
- [ ] Render dos quadros com SkiaSharp e envio ao ffmpeg
- [ ] Efeitos, transições e configuração (padrão + por foto) iguais ao protótipo
- [ ] Formatos 16:9 e 9:16
- [ ] Testes automatizados (imagens de referência / tempos das trocas)

## A fazer — qualidade do vídeo
- [ ] Enquadramento automático com detecção de rosto (ex.: um rosto cortado no topo na foto de teste 0033)
- [ ] Opção para foto horizontal no formato vertical: inteira com fundo desfocado ou recortada
- [ ] Mais transições (flash, deslizar)
- [ ] Opção de duração por foto manual (fora da batida)
- [ ] Vídeo com várias fotos por tela (colagem) — avaliar

## A fazer — interface (Avalonia)
- [ ] Escolher pasta de fotos e música
- [ ] Tela de padrões (efeitos, intensidade, transição, ritmo)
- [ ] Ajuste por foto + reordenar fotos
- [ ] Pré-visualização rápida
- [ ] Exportar (16:9 / 9:16) com barra de progresso

## A fazer — produto e negócio
- [ ] Definir o nome do produto (hoje é nome de trabalho)
- [ ] **Licença do ffmpeg:** builds com libx264 são GPL; definir como distribuir (build LGPL com encoder de hardware/OpenH264 ou licença do x264) antes de vender
- [ ] Validar demanda: vídeo de demonstração + página simples com preço (ex.: R$ 29–39/mês) mostrada a outros fotógrafos
- [ ] Música: manter por conta do usuário; depois avaliar catálogo licenciado (perguntar por escrito ao fornecedor se pode embutir no app)
- [ ] Assinatura/licença e instalador (por último)
- [ ] Criar repositório remoto no GitHub e enviar
