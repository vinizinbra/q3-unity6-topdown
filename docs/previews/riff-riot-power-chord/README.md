# Riff Riot — Power Chord

Prefab: `Assets/_Project/Prefabs/Effects/RiffRiot/RiffRiot_PowerChordFx.prefab`.

Três ondas em arco rosa Rift, núcleo quase branco, halo magenta, faíscas de cordas e uma ressonância ciano curta no alcance máximo. Os transientes aparecem no nascimento, incluindo um arco discreto na borda: o efeito responde ao dano imediato. As ondas seguintes são o decaimento visual do acorde.

Glow reforçado: cada onda combina uma aura larga, halo intermediário intenso, corpo rosa, tubo aditivo e núcleo quase branco. As camadas ficam centradas na mesma linha para iluminar os dois lados do arco, com transparência suave até o limite externo. Os materiais aditivos são desenhados depois do corpo rosa para preservar o brilho, sem Bloom.

A versão com descargas elétricas adiciona sete raios ramificados, com núcleos quase brancos e halos rosa/ciano. Três desenhos diferentes piscam em 0,000, 0,065 e 0,130 s, duram entre 0,080 e 0,095 s cada e desaparecem até 0,210 s. As curvas dos raios têm cantos arredondados, pontas suaves e um fade rápido com entrada curta e decaimento progressivo, evitando o corte brusco entre os flashes. Os raios são partículas de malha sem scripts de runtime. Os offsets Y dos filhos ficam entre 0,10 e 0,20 unidade do chão (aura 0,10; halo 0,125; corpo 0,15; tubo 0,175; núcleo 0,20), têm tubos e núcleos mais espessos e expansão inicial mais rápida; um pulso amplo na borda e o clarão na origem reforçam o instante do golpe. As faíscas também são maiores. Os vértices dos halos dos raios entram na mesma validação geométrica do cone.

A raiz contém um ParticleSystem sem emissão, usado para controlar os filhos. Todos os sistemas são one-shot, em espaço local, com escala 1 e frente em +Z. Os arcos são malhas com transparência progressiva nas bordas, inteiramente contidas no setor. Nenhum script de runtime é necessário. O último filho termina aproximadamente em 0,52 s; a raiz tem duração de 0,70 s.

Materiais próprios usam `Mobile/Particles/Additive` e `Mobile/Particles/Alpha Blended`. As malhas amostram o centro da textura existente `Epic Toon FX/Textures/glow1.png`; a suavidade vem das cores dos vértices. Nenhum material ou textura de terceiros foi alterado, e nenhuma textura nova foi criada. `RiffRiot_SoundWaveCone.prefab` foi preservado (hash conferido).

O impacto também contém um flash preenchendo todo o setor, com bordas suaves: rosa aditivo durante 90 ms e núcleo branco rosado durante 75 ms. O flash nasce no tamanho completo, sem expansão ou atraso, e perde intensidade rapidamente. Os quadros adicionais de 25 e 50 ms registram esse instante; em 100 ms o preenchimento já desapareceu.

As imagens foram renderizadas em uma cena temporária de preview do Editor Unity 6000.3.6f1, com URP do projeto, sem pós-processamento, Bloom ou fog. Câmera em (0,36,-32.5), rotação (45,0,0), FOV 14; chão (0.40,0.41,0.47). Contorno laranja de 90° × 6,5 e grade de 1 unidade existem apenas no preview. Os quadros principais são 100, 250 e 400 ms. O quadro de 800 ms confirma o desaparecimento.

Para reproduzir, use `Tools > RiftRaiders > Boss > Power Chord`:

- `Build independent prefab`: reconstrói apenas os assets PowerChord próprios.
- `Render gameplay preview`: renderiza os PNGs nesta pasta sem alterar a cena aberta.
- `Validate cone and lifetime`: verifica geometria e duração em 81 amostras, incluindo uma rotação de mira de 73°, e grava `validation.txt`.

A validação usa os vértices de todas as partículas, incluindo as bordas transparentes dos halos e das faíscas. Veja os números em `validation.txt`. A legibilidade foi inspecionada no preview isolado; uma partida com quatro heróis e outros ataques simultâneos não foi executada.
