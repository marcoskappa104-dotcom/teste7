Aqui está uma versão muito mais completa e profissional do seu documento, expandindo o sistema de RPG Online, adicionando TODOS os stats derivados importantes, sistemas essenciais de MMORPG/ARPG moderno, integração com combate, economia, multiplayer, progressão e arquitetura modular para Unity + Mirror.

# Sistema de Joias do Poder — Arquitetura Completa do RPG Online

Um dos principais diferenciais do jogo é o sistema de habilidades baseado em um item especial chamado **Joia do Poder**.

Em vez das skills serem aprendidas diretamente pelo personagem através de árvores de talentos tradicionais, as habilidades são concedidas dinamicamente através dessas joias equipáveis.

Esse sistema é inspirado em mecânicas profundas de customização presentes em ARPGs modernos, permitindo enorme liberdade de builds, combinações e progressão do personagem.

O objetivo principal é criar um RPG Online extremamente modular, rejogável, estratégico e com alto nível de personalização.

---

# Filosofia do Sistema

O sistema foi criado para:

* aumentar a liberdade de build
* incentivar experimentação
* evitar classes extremamente limitadas
* permitir diversidade de gameplay
* criar economia entre jogadores
* tornar itens parte central da progressão
* incentivar farm e exploração
* permitir conteúdos endgame complexos
* criar builds únicas
* aumentar longevidade do jogo

---

# Sistema de Joias do Poder

As **Joias do Poder** são itens especiais responsáveis por conceder habilidades, modificadores e efeitos passivos ao personagem.

As joias podem ser adquiridas através de:

* Drop de monstros
* Recompensas de quests
* Dungeons
* Bosses
* Eventos
* Crafting
* NPCs especiais
* Marketplace
* Trade entre jogadores
* Sistema de conquistas
* Guild Wars
* Raids
* PvP ranqueado
* Temporadas
* Battle Pass
* Conteúdo endgame
* Baús raros
* Mapas especiais
* Sistema de reputação
* Eventos globais

---

# Funcionamento do Sistema

O jogador possui uma interface específica dedicada ao gerenciamento das Joias do Poder.

Ao equipar uma joia em um slot válido:

* a habilidade é desbloqueada automaticamente
* a skill é adicionada à barra de habilidades
* os atributos são recalculados
* efeitos visuais são ativados
* animações são habilitadas
* partículas são sincronizadas
* buffs passivos são aplicados
* modificadores são registrados
* o servidor sincroniza os dados para todos os clientes

Ao remover a joia:

* a habilidade é desativada
* removida da barra de skills
* cooldowns são cancelados
* buffs relacionados podem ser removidos
* modificadores deixam de existir
* efeitos visuais são removidos

---

# Estrutura das Joias

Cada Joia do Poder possui propriedades próprias.

---

## Informações básicas

* Nome
* Ícone
* Raridade
* Tipo
* Tier
* Descrição
* Peso
* Valor
* Categoria
* Classe requerida
* Nível requerido
* Item Level
* Durabilidade
* Afinidade elemental
* Origem
* Tags
* Bind status
* Stack size
* Identificação única
* Seed procedural
* Qualidade
* Cor da joia

---

# Dados da habilidade vinculada

* Skill concedida
* Tipo da skill
* Dano base
* Escalabilidade
* Consumo de mana
* Consumo de stamina
* Cooldown
* Tempo de conjuração
* Tempo de canalização
* Range
* Área de efeito
* Quantidade de alvos
* Elemento
* Duração
* Velocidade do projétil
* Quantidade de projéteis
* Chance crítica
* Multiplicador crítico
* Tipo de dano
* Efeito secundário
* Tipo de alvo
* Prioridade da IA
* Tipo de colisão
* Tipo de animação
* Tempo de recuperação
* Custo por segundo
* Resistência ignorada
* Penetração elemental
* Geração de ameaça
* Knockback
* Chance de stun
* Chance de congelamento
* Chance de sangramento
* Chance de veneno
* Chance de queimadura
* Chance de choque

---

# Tipos de Joias

## Joias de habilidade ativa

Concedem habilidades utilizáveis diretamente pelo jogador.

Exemplos:

* Bola de fogo
* Investida
* Cura
* Flechas múltiplas
* Invocação
* Explosão elemental
* Totens
* Armadilhas
* Aura
* Dash
* Escudos mágicos

---

## Joias passivas

Concedem bônus permanentes enquanto equipadas.

Exemplos:

* Velocidade de ataque
* Chance crítica
* Regeneração de mana
* Resistência elemental
* Roubo de vida
* Armadura
* Velocidade de movimento
* Resistência a crowd control
* Redução de cooldown
* Penetração de armadura

---

## Joias de suporte/modificação

Modificam habilidades já existentes.

Exemplos:

* skill causa fogo
* skill ricocheteia
* skill explode ao impacto
* skill ganha stun
* redução de cooldown
* aumento de área
* múltiplos projéteis
* conversão elemental
* aumento de velocidade
* efeito em cadeia
* perfuração
* invocação duplicada
* dano em área
* canalização automática

---

# Sistema de Slots

A interface de Joias possui slots específicos.

Tipos:

* Slot ofensivo
* Slot defensivo
* Slot utilitário
* Slot universal
* Slot lendário
* Slot ancestral
* Slot elemental
* Slot de suporte
* Slot de invocação
* Slot de ultimate

---

# Sistema de raridade

As Joias podem possuir raridades diferentes:

* Comum
* Incomum
* Mágica
* Rara
* Épica
* Lendária
* Mítica
* Ancestral
* Divina
* Única

A raridade influencia:

* poder da skill
* quantidade de modificadores
* efeitos especiais
* aparência visual
* valor de mercado
* quantidade de sockets
* potencial de upgrade
* efeitos visuais
* brilho
* partículas

---

# Sistema de atributos base

Os atributos principais do personagem:

* STR — Força
* AGI — Agilidade
* VIT — Vitalidade
* INT — Inteligência
* DEX — Destreza
* LUK — Sorte

---

# Sistema de atributos derivados

Os atributos derivados são calculados em tempo real com base em:

* atributos base
* equipamentos
* joias
* buffs
* debuffs
* nível
* passivas
* títulos
* pets
* montarias
* refinamentos
* encantamentos

---

# Stats ofensivos

* PhysicalAttack
* MagicAttack
* RangedAttack
* ElementalDamage
* AttackSpeed
* CastSpeed
* CriticalChance
* CriticalDamage
* Accuracy
* ArmorPenetration
* MagicPenetration
* TrueDamage
* LifeSteal
* ManaSteal
* CooldownReduction
* AreaDamage
* AreaRadius
* ProjectileSpeed
* ProjectileCount
* ChainCount
* PierceCount
* RicochetCount
* BleedChance
* PoisonChance
* BurnChance
* FreezeChance
* ShockChance
* StunChance
* KnockbackChance
* ExecuteChance
* SummonDamage
* TrapDamage
* TotemDamage
* ReflectDamage
* DamageOverTime
* CursePower
* AuraPower

---

# Stats defensivos

* MaxHealth
* MaxMana
* MaxStamina
* HealthRegen
* ManaRegen
* StaminaRegen
* Armor
* MagicResistance
* ElementalResistance
* FireResistance
* IceResistance
* LightningResistance
* PoisonResistance
* HolyResistance
* DarkResistance
* ChaosResistance
* DodgeChance
* Evasion
* BlockChance
* BlockAmount
* ParryChance
* CriticalResistance
* CrowdControlResistance
* StunResistance
* FreezeResistance
* BurnResistance
* ShockResistance
* BleedResistance
* CurseResistance
* DamageReduction
* ReflectReduction
* Shield
* Barrier
* LifeOnHit
* ManaOnHit
* DamageAbsorption
* GuardPower

---

# Stats utilitários

* MoveSpeed
* SprintSpeed
* JumpPower
* SwimSpeed
* ClimbSpeed
* GatheringSpeed
* CraftSpeed
* FishingPower
* MiningPower
* Luck
* ItemDropRate
* GoldDropRate
* RareDropRate
* EXPGain
* ReputationGain
* AggroGeneration
* AggroReduction
* DetectionRange
* PetPower
* MountSpeed

---

# Sistema Elemental

O jogo possui sistema elemental avançado.

Elementos:

* Fogo
* Água
* Gelo
* Terra
* Vento
* Raio
* Luz
* Trevas
* Veneno
* Sagrado
* Caos

Cada elemento possui:

* vantagens
* desvantagens
* resistências
* conversões
* efeitos secundários

---

# Sistema de Status Effects

Buffs e debuffs avançados:

* Veneno
* Sangramento
* Congelamento
* Queimadura
* Choque
* Lentidão
* Silêncio
* Cegueira
* Confusão
* Maldição
* Fear
* Root
* Sleep
* Petrificação
* Charm
* Knockback
* Taunt

---

# Sistema de Equipamentos

Tipos de equipamento:

* Arma principal
* Arma secundária
* Capacete
* Armadura
* Luvas
* Botas
* Capa
* Colar
* Anéis
* Braceletes
* Cinto
* Relíquias
* Artefatos

---

# Sistema de Refinamento

Equipamentos e joias podem ser refinados.

Possibilidades:

* aumento de atributos
* efeitos especiais
* bônus raros
* brilho visual
* destruição ao falhar
* proteção de refinamento

---

# Sistema de Crafting

Profissões:

* Ferreiro
* Alquimista
* Encantador
* Joalheiro
* Cozinheiro
* Pescador
* Minerador

---

# Sistema de Multiplayer Online

O jogo utiliza arquitetura server-authoritative usando Mirror.

O servidor possui autoridade total sobre:

* dano
* movimentação
* skills
* cooldowns
* inventário
* economia
* drops
* buffs
* debuffs
* IA
* combate
* experiência
* trade
* crafting

---

# Segurança

O sistema precisa evitar:

* duplicação de itens
* hacks de velocidade
* hacks de skill
* bypass de cooldown
* teleporte ilegal
* packet injection
* manipulation exploits
* memory editing
* spoofing
* fake damage
* fake movement
* fake inventory

Tudo deve ser validado server-side.

---

# Estrutura recomendada de scripts

## Dados

### PowerGemData

Responsável pelos dados da joia.

### SkillData

Responsável pelos dados da skill.

### ItemData

Base de todos os itens do jogo.

### EquipmentData

Dados dos equipamentos.

---

# Runtime

### PlayerStats

Calcula todos os atributos.

### DerivedStatsCalculator

Sistema central de cálculo de stats derivados.

### BuffSystem

Gerencia buffs e debuffs.

### CombatManager

Gerencia combate.

### SkillExecutionSystem

Executa skills.

### DamageSystem

Calcula dano.

### StatusEffectSystem

Gerencia efeitos negativos.

### ThreatSystem

Sistema de aggro.

### AICombatBrain

IA de combate dos monstros.

---

# Sistemas essenciais do RPG Online

* Sistema de party
* Sistema de guilda
* Sistema de raid
* Sistema de PvP
* Arena ranqueada
* Sistema de chat
* Trade entre jogadores
* Marketplace
* Mail system
* Friend system
* Voice chat
* Sistema de títulos
* Sistema de reputação
* Sistema de achievements
* Battle pass
* Eventos globais
* Temporadas
* Leaderboards
* Anti-cheat
* Cross-server
* Matchmaking
* Dungeon finder
* Housing
* Pets
* Montarias
* Companions
* World Bosses
* Dynamic Events
* Day/Night cycle
* Weather system
* Factions
* Karma system
* Open World PvP

---

# Objetivo final do sistema

O sistema de Joias do Poder transforma as habilidades em um dos pilares centrais da progressão do jogo.

Cada jogador pode criar builds totalmente únicas através da combinação de:

* atributos
* equipamentos
* joias
* passivas
* modificadores
* elementos
* refinamentos
* encantamentos
* buffs
* sinergias

Isso cria um RPG Online extremamente profundo, estratégico, competitivo, rejogável e com enorme liberdade de personalização.
