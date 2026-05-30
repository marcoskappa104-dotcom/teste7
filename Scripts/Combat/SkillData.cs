using UnityEngine;

namespace RPG.Combat
{
    public enum SkillType { Physical, Magical, Heal, Buff }

    /// <summary>
    /// Para skills Skillshot: COMO o tiro se comporta.
    /// </summary>
    public enum SkillshotType
    {
        /// <summary>Projétil físico que VIAJA (bola de fogo). Acerta o 1º (ou perfura)
        /// e some ao chegar no fim do Range sem acertar nada.</summary>
        Projectile = 0,

        /// <summary>Feixe/laser INSTANTÂNEO em linha reta. Dá dano na hora a tudo na
        /// linha (respeitando PierceCount/MaxTargets). Sem tempo de viagem.</summary>
        Beam = 1,
    }

    /// <summary>
    /// Como a skill é mirada. Define o fluxo no cliente E a validação no servidor.
    /// Substitui o antigo SkillTarget (Enemy/Self/Ally) por algo ARPG-style.
    /// </summary>
    public enum SkillAimMode
    {
        /// <summary>Sem mira. Aplica no próprio player (cura, buff). Ex: Heal.</summary>
        SelfCast = 0,

        /// <summary>AoE instantânea centrada no player. Ex: Nova de gelo, Tornado.</summary>
        AroundSelf = 1,

        /// <summary>Homing: exige cursor SOBRE um inimigo. Ex: Bola de fogo travada.</summary>
        TargetEnemy = 2,

        /// <summary>Projétil na DIREÇÃO do cursor, voa reto. Ex: Skillshot de fogo.</summary>
        Skillshot = 3,

        /// <summary>AoE no PONTO do cursor no chão (com telegraph). Ex: Meteoro.</summary>
        GroundTarget = 4,
    }

    [CreateAssetMenu(menuName = "RPG/Skill Data", fileName = "Skill_New")]
    public class SkillData : ScriptableObject
    {
        [Header("Identidade")]
        public string    Name        = "Skill";
        public SkillType Type        = SkillType.Physical;
        public Sprite    Icon;
        public string    AnimTrigger = "AttackCast";

        [Tooltip("Id do efeito visual desta skill (procurado no VfxLibrary do player). " +
                 "Vazio = usa o visual padrão por modo de mira.")]
        public string    CastVfxId   = "";

        [Header("Mira (define o gameplay)")]
        public SkillAimMode AimMode = SkillAimMode.TargetEnemy;

        [Header("Custos & Cadência")]
        [Min(0f)] public float ManaCost = 10f;
        [Min(0f)] public float Cooldown = 3f;

        [Tooltip("Tempo de conjuração. 0 = instantâneo (recomendado p/ fluidez).")]
        [Min(0f)] public float CastTime = 0f;

        [Tooltip("Se true, andar durante o cast cancela. Deixe false para skills rápidas.")]
        public bool MovementInterruptsCast = false;

        [Header("Dano")]
        [Tooltip("Multiplicador sobre ATK (físico) ou MATK (mágico).")]
        [Min(0f)] public float AtkMultiplier = 1.0f;

        [Header("Alcance")]
        [Tooltip("Distância máxima que a skill atinge:\n" +
                 "• TargetEnemy: distância máx. até o inimigo.\n" +
                 "• Skillshot: distância máx. que o projétil viaja.\n" +
                 "• GroundTarget: distância máx. do ponto até o player.\n" +
                 "• AroundSelf/SelfCast: ignorado.")]
        [Min(0.5f)] public float Range = 9f;

        [Header("Área de Efeito")]
        [Tooltip("Raio da explosão. 0 = single target.\n" +
                 "AroundSelf usa este raio ao redor do player.\n" +
                 "GroundTarget usa este raio no ponto do cursor.\n" +
                 "Skillshot usa como raio de explosão ao impactar (0 = só o alvo atingido).")]
        [Min(0f)] public float AoERadius = 0f;

        [Tooltip("Máx. de alvos atingidos pela AoE/skillshot. 0 = ilimitado.")]
        [Min(0)] public int MaxTargets = 0;

        [Header("GroundTarget (meteoro)")]
        [Tooltip("Atraso entre o cast e o impacto no chão (telegraph). 0 = instantâneo.")]
        [Min(0f)] public float ImpactDelay = 0f;

        [Header("Skillshot — tipo de tiro")]
        [Tooltip("Só usado quando AimMode = Skillshot.\n" +
                 "• Projectile: bola que viaja e some no fim do range.\n" +
                 "• Beam: laser instantâneo em linha reta.")]
        public SkillshotType ShotType = SkillshotType.Projectile;

        [Header("Skillshot › Projétil (ShotType = Projectile)")]
        [Min(1f)]  public float ProjectileSpeed = 22f;
        [Min(1)]   public int   ProjectileCount = 1;
        [Tooltip("Ângulo total do leque quando ProjectileCount > 1 (graus).")]
        [Range(0f, 180f)] public float SpreadAngle = 0f;
        [Tooltip("Quantos inimigos o projétil atravessa antes de sumir. 0 = some no 1º.")]
        [Min(0)] public int PierceCount = 0;
        [Tooltip("Raio de colisão do projétil (quão 'grosso' ele é ao detectar acertos).")]
        [Min(0.1f)] public float ProjectileRadius = 0.5f;

        [Header("Skillshot › Beam/Laser (ShotType = Beam)")]
        [Tooltip("Espessura do feixe ao detectar acertos (raio do cilindro de colisão).")]
        [Min(0.05f)] public float BeamThickness = 0.4f;
        [Tooltip("Por quanto tempo o feixe permanece ativo causando dano (s). " +
                 "0 = dano único instantâneo (laser-pulse).")]
        [Min(0f)] public float BeamDuration = 0f;
        [Tooltip("Se BeamDuration > 0: intervalo entre ticks de dano (s).")]
        [Min(0.05f)] public float BeamTickInterval = 0.2f;

        [Header("Legado")]
        [Tooltip("Mantido por compatibilidade. Skillshot agora usa ShotType. " +
                 "Para TargetEnemy ranged, marque para usar projétil homing.")]
        public bool  UsesProjectile  = false;

        // ── Helpers ────────────────────────────────────────────────────────

        public bool IsPhysical    => Type == SkillType.Physical;
        public bool IsAoE         => AoERadius > 0.01f;
        public bool NeedsAimPoint => AimMode == SkillAimMode.Skillshot
                                  || AimMode == SkillAimMode.GroundTarget;
        public bool NeedsEnemy    => AimMode == SkillAimMode.TargetEnemy;
        public bool IsBeam        => AimMode == SkillAimMode.Skillshot && ShotType == SkillshotType.Beam;
        public bool IsProjectile  => AimMode == SkillAimMode.Skillshot && ShotType == SkillshotType.Projectile;
        public bool IsSustainedBeam => IsBeam && BeamDuration > 0.01f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (AimMode == SkillAimMode.AroundSelf && AoERadius <= 0f)
                Debug.LogWarning($"[SkillData] '{name}' é AroundSelf mas AoERadius=0. Ninguém será atingido.");

            if (IsProjectile && ProjectileSpeed < 1f)
                Debug.LogWarning($"[SkillData] '{name}' é Skillshot/Projectile mas ProjectileSpeed < 1.");

            if (IsBeam && Range < 1f)
                Debug.LogWarning($"[SkillData] '{name}' é Skillshot/Beam mas Range < 1.");
        }
#endif
    }
}
