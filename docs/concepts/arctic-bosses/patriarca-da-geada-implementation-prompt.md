# Prompt para Claude — implementar Patriarca da Geada

Implemente o **Patriarca da Geada**, boss do World 3 Ártico, neste projeto Unity 6 / Photon Quantum. O design aprovado é um urso polar quadrúpede com uma pata dianteira enorme corrompida por cristais magenta de Rift e placas de gelo ciano nas costas. Queremos o boss completo e jogável usando a estrutura atual, não apenas scripts ou assets desconectados.

Leia as instruções aplicáveis do repositório e inspecione o estado atual antes de editar. Preserve alterações existentes e trabalhos de outros bosses. Tome decisões rotineiras de implementação e prossiga até autoria de assets, integração e validação; reporte claramente qualquer limitação real.

## 1. Estrutura existente

Comece lendo:

- `docs/boss-encounter.md`
- `docs/riff-riot-boss.md`
- `Assets/_Project/Editor/Boss/RiffRiotBossBuilder.cs`
- `Assets/_Project/Editor/Boss/DonoDoCeuBossBuilder.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Boss/BossDataAsset.cs`
- `Assets/_QuantumUser/Simulation/Systems/Enemy/BossSystem.cs`
- `Assets/_QuantumUser/Simulation/Systems/Enemy/BossPhaseUtility.cs`
- `Assets/_QuantumUser/Simulation/QTN/Enemy/BossRuntimeState.qtn`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/EnemyActionData.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/EnemyActionData.View.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/AttackVisualStep.cs`
- Deliveries existentes em `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/Delivery/`, especialmente `GroundAreaDeliveryData`, `GroundBarrageDeliveryData`, `SequenceDeliveryData`, `ChargeDeliveryData`, `BeamDeliveryData`, `AuraDeliveryData` e `KneelDeliveryData`.

Reutilize `BossDataAsset`, `BossRuntimeState`, `EnemyActionData`, seleção de ações, fases, stagger, telegraphs, HUD, intro, derrota, recompensas e debug encounter. Não crie uma máquina de boss paralela em MonoBehaviour. Mecânicas, movimento, dano, status, decisões e aleatoriedade pertencem à simulação determinística Quantum, usando FP e RNG do frame; Unity cuida de apresentação.

Crie um builder idempotente semelhante aos existentes: preservar identificadores/GUIDs ao executar novamente, separar tuning de conteúdo anterior e documentar o builder como fonte de verdade. Faça cópias próprias dos assets que precisar alterar. Não modifique o comportamento dos bosses existentes nem os assets compartilhados para ajustar somente este boss. Extensões ao framework devem ser pequenas, reutilizáveis e opt-in.

## 2. Arte aprovada e caminhos exatos

Use estas duas sheets, **nove poses em cada imagem**:

- `Assets/0_Refs/ArcticBoss/PatriarcaDaGeada/PatriarcaDaGeada-v1-SheetA.png`
- `Assets/0_Refs/ArcticBoss/PatriarcaDaGeada/PatriarcaDaGeada-v1-SheetB.png`

Referências adicionais:

- Mapa de poses: `docs/concepts/arctic-bosses/patriarca-da-geada-sheets.md`
- Concept aprovado: `docs/concepts/arctic-bosses/04-patriarca-da-geada.png`
- Enemies do mundo: `Assets/0_Refs/World3EnemySpriteAtlas-v2.png`

**Este boss substitui a direção do Rei da Geada esqueleto. Não use as sheets `ReiDaGeada-v1-*`.**

As sheets são arte-fonte gerada, não assets finais de animação. Inspecione os contornos e alpha antes de cortar; não suponha que nove cortes iguais isolam perfeitamente todos os membros. Preserve os originais em `0_Refs` e prepare cópias de produção com sprites nomeados. Remova contaminação de fundo/halos nas cópias se presente, preservando os contornos e cores do personagem.

Alinhe os pivôs à raiz lógica do corpo e ao plano de apoio, com escala corporal consistente. Não redimensione cada pose independentemente para caber na mesma caixa: a pose erguida deve ficar mais alta, não virar um urso menor. Verifique a interação com o sprite-fit atual do rig. Cristais e pata estendida não devem aumentar o collider físico do tronco. Corrida e bob visual não podem duplicar movimento/altura já simulados.

Preserve a silhueta geométrica, sombras chapadas, pelagem branca, gelo ciano e corrupção magenta. Algumas recuperações são parecidas: o timing e a troca correta de sprites devem esclarecer os beats; não prometa animação fluida sem validar no jogo.

### Mapa: esquerda para direita, de cima para baixo

| Slot | Sheet A | Sheet B |
| --- | --- | --- |
| 1 | Idle quadrúpede | Sopro: preparação |
| 2 | Caminhada: passo A | Sopro: execução |
| 3 | Caminhada: passo B | Sopro: recuperação |
| 4 | Patada: preparação | Investida: preparação |
| 5 | Patada: execução | Investida: corrida |
| 6 | Patada: recuperação | Investida: freada |
| 7 | Quebra-Gelo: erguido/preparação | Stagger: desequilíbrio |
| 8 | Quebra-Gelo: impacto | Kneel: caído sobre o ombro |
| 9 | Quebra-Gelo: recuperação | Kneel: levantamento |

Use `AttackVisualStep.BodySprite` / `BodySpriteFrames` e hooks atuais. Prepare os sprites de recuperação nos estados corretos; confira que o retorno automático ao idle não sobrescreve a recuperação cedo demais. Caminhada usa A2/A3 com o mecanismo existente ou uma extensão view pequena. Para derrota, B8 pode ser reutilizado pela sequência existente.

Destinos sugeridos:

- Arte: `Assets/_Project/Art/Sprites/Enemy/Arctic/PatriarcaDaGeada/`
- Dados: `Assets/_QuantumUser/Resources/Enemy/Arctic/Boss/PatriarcaDaGeada/`
- Prefab: `Assets/_QuantumUser/Entities/Enemies/Arctic-PatriarcaDaGeadaPrefab.prefab`
- VFX próprios: `Assets/_Project/Prefabs/Effects/PatriarcaDaGeada/`
- Builder: `Assets/_Project/Editor/Boss/PatriarcaDaGeadaBossBuilder.cs`

## 3. Kit aprovado

Todos os números devem ser autoráveis. Use bosses existentes como ponto de partida de HP/dano, com o scaling atual de party/dificuldade aplicado uma única vez. Tempos abaixo são valores iniciais para playtest, não balanceamento validado.

### Patada Corrompida — ataque próximo

- A4: apoia peso na pata normal, torce o corpo e recua a pata corrompida. Telegraph de cone no chão; mira trava antes do golpe.
- A5: varredura em cone, dano e knockback pequeno, **sem slow/freeze**. Um hit por execução, não um por tick.
- A6: pata estendida e flanco exposto durante recuperação visível.
- Reutilize cone/efeitos de dano atuais, com gate de altura do piso. Ponto inicial de windup: aproximadamente 0,8–1 segundo, ajustado ao feel atual.

### Quebra-Gelo — ataque pesado

- A7: fica realmente legível erguido nas patas traseiras, ambas as dianteiras levantadas. É uma pose de antecipação, não um leap; não adicione salto físico só porque o sprite cresce verticalmente.
- A8: pancada com as duas patas; dano em área curta à frente, seguida por **três erupções de gelo sequenciais** avançando numa direção comprometida.
- Cada erupção tem aviso próprio antes de causar dano. Pontos definidos antes de executar, sem perseguir a posição atual do alvo.
- A9: recuperação longa o suficiente para atacar. Não devolva imediatamente para outra pancada.
- Reutilize ground area/barrage/sequence onde fizer sentido; não use uma sequence supondo que ela refaz automaticamente mira/telegraph de cada passo. Audite as limitações reais.
- A pancada não precisa aplicar frio. A diferença principal é área/ritmo, não acumular todos os tipos de punição no mesmo ataque.

### Sopro Polar — dano periódico, Chill e possível Freeze

- B1: peito expandido e cabeça recuada; avisa um cone. B2: pescoço estendido e boca aberta durante o canal. B3: cabeça baixa na recuperação.
- Cone contínuo com ticks determinísticos, dano moderado e acúmulo de frio. Exposição inicial causa lentidão; exposição suficiente pode congelar pelo **sistema atual de Ice/Chill/Freeze**.
- Ponto inicial de canal: cerca de 2–3 segundos. Direção trava no fim da preparação; durante o canal pode acompanhar lentamente com velocidade angular limitada e autorável. Sem snap/retarget instantâneo no sopro.
- Telegraph e VFX precisam acompanhar a área verdadeira; permita contornar pelo flanco. Não atravesse paredes por acidente. Gate de altura/piso para não congelar alguém em outra plataforma com o mesmo XZ.
- Leia `Assets/_QuantumUser/Simulation/Assets/Projectile/SlowEffectData.cs`, `StickySlowEffectData.cs`, `StatusEffectUtility` e a configuração elemental atual. Reutilize o pipeline real, sem um segundo medidor de Chill e sem tratar SlowEffectData como slow plano.
- Respeite a proteção existente contra freeze consecutivo. Se não existir proteção suficiente para heróis, implemente o mínimo necessário de maneira compatível e documentada. Nada de freeze permanente ou resets de imunidade por tick.
- Não programe um follow-up inevitável enquanto alguém ainda está congelado. Preserve a possibilidade de escapar da próxima skill.

### Investida Glacial — aproximação e trilha de slow

- B4: corpo comprimido, cabeça baixa e ombro corrompido à frente; faixa de direção anunciada no piso, mira trava antes do avanço.
- B5: deslocamento real em linha reta e colisão/sweep na simulação. Sem homing após compromisso. **No máximo um hit em cada jogador por passagem**, inclusive se alguém permanecer dentro do collider.
- Audite `ChargeDeliveryData`: seu `HitCooldown` pode permitir múltiplos hits durante uma passagem, então não assuma que `StopOnHit=false` já garante um hit por alvo. Use tracking ou configuração que realmente assegure o requisito.
- B6: freada e recuperação antes de outra ação.
- Deixa uma trilha curta de áreas de gelo que aplicam slow apenas durante a passagem do jogador e um pequeno período de saída, **sem acúmulo de Freeze**. Reutilize `StickySlowEffectData` / sticky slow, com limite de segmentos e TTL.
- Choque em obstáculo sólido válido aumenta a recuperação e pode dar uma exposição breve. `ChargeDeliveryData` já tem `WallExposedDuration` / `WallExposedDamageMultiplier`: usar Rupture/exposição, não self-stun que Boss tier ignora. Garanta que a pausa autorada acompanha a exposição; só aumentar dano recebido não representa uma freada maior.
- Valide chão/parede/bordas na velocidade máxima; não permita atravessar voids ou sair da arena. Não trate qualquer mudança de altura como parede válida sem checar geometria.

## 4. Stagger, fases e regras de encontro

- Boss terrestre quadrúpede. Mantém colisão, gravidade e apoio no piso corretos em desníveis; não precisa de voo ou burrow.
- Reutilize stagger percentual por vida máxima e a ação forçada do framework.
- Sequência de break: **B7 desequilíbrio -> B8 kneel/exposição -> B9 levantamento -> retorno ao combate**. A janela de dano aumentado deve coincidir com B8, usando Rupture/Kneel. A pata continua existindo no design; ele cai sobre o ombro, não perde o braço como o esqueleto anterior.
- Interrupção de patada, sopro, barrage e corrida deve limpar delivery/subestado, restaurar física e impedir casts órfãos. Use deferral quando necessário para segurança, sem esconder o break durante tempo excessivo; não pule cleanup da ação interrompida.
- No break, trilhas de gelo e erupções pendentes deste boss dissipam/cancelam sem causar um hit final. Isso limpa a janela de punição.
- Fase 2 em aproximadamente 50% HP: mesmo kit e sprites. Pode aumentar modestamente deslocamento ou reduzir recuperação; preserve avisos e oportunidades de reação. Corrupção pode pulsar por efeitos. Sem novos ataques, summons ou transformação permanente em bípede.
- Um grande ataque por vez. Evite sopro/freeze seguido de investida sem janela de reação e sobreposição injusta de erupções com kneel.
- Hazards e avisos pertencem a um boss/encounter. Cleanup em morte, derrota, despawn, retry e mudança de estado; não remova efeitos de outros personagens. Pausas precisam congelar os timers de gameplay como no restante do projeto.
- Respeite o jogo **2.5D com altura real**: telegraphs colados às superfícies corretas, avisos correspondentes ao dano, nenhum hit em outro andar só por compartilhar XZ.

## 5. VFX e integração

Inspecione e reutilize partículas existentes de gelo, sopro, impacto e cristais. Crie variantes próprias quando precisar de cor/escala/timing; não edite originais de terceiros. VFX separados dos sprites: sopro, espinhos, trilha, shards, warnings e feedback de status no herói. Não regenere a arte do boss. Documente qualquer placeholder real em vez de afirmar acabamento que não foi verificado.

Use o boss HUD, reveal e defeat atuais. Revele com uma pose aprovada se não houver portrait dedicado. Habilite o prototype real em `DebugPrototype`, Spawn Near Local Player e Jump To This Boss/debug override, seguindo os bosses atuais.

Verifique se já existe World 3/Arctic WorldDefinition e SurvivalConfig: conecte apenas ao config real apropriado se existir. Caso ainda não exista, entregue o encontro acessível pelo debug existente e documente a integração de timeline pendente. Não sobrescreva W1/W2 nem invente progressão de mundos para esconder essa ausência.

## 6. Entrega e validação

Entregue builder idempotente, sprites preparados/recortados, materiais e rig, prefab do boss, dados de ações/deliveries/telegraphs, hazards/VFX e quaisquer componentes/sistemas mínimos necessários. Gere código Quantum pelo workflow atual; não edite arquivos gerados manualmente.

Crie `docs/patriarca-da-geada-boss.md` com kit, tuning, mapa de sprites, caminhos, debug entry e limitações. Compile e valide usando ferramentas disponíveis. Verifique especialmente:

- Leitura das poses e estabilidade de escala/pivô, incluindo alternância de passos e pose alta A7.
- Aim lock, áreas de dano/telegraphs, altura e paredes.
- Um hit por jogador por investida, suporte de chão e freada em obstáculo.
- Chill gradual e freeze sem encadeamento permanente; trilha nunca provoca freeze.
- Stagger durante sopro/corrida/barrage, exposição no kneel e retorno limpo ao idle.
- Cleanup de hazards/avisos na quebra, morte e teardown; pausa e coop com alvos inválidos.
- Builder rerun preservando IDs e ausência de regressões nos bosses existentes.

Faça playtest do encontro onde possível. Diferencie verificação real de testes ainda pendentes, especialmente multiplayer/online. Não declare completo com referências faltando, assets desconectados ou placeholders não reportados.
