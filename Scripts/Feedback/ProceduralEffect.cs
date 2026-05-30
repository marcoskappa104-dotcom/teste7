using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Receita de efeito visual 100% procedural (sem prefab/asset de arte).
    /// Ligue/configure módulos no Inspector; o ProceduralFx monta tudo em runtime
    /// criando GameObjects (LineRenderer, ParticleSystem, Light, Mesh) — a mesma
    /// técnica do SkillFx, agora reutilizável e escalável.
    ///
    /// Criar:  Create → RPG → Procedural Effect
    /// Usar:   ProceduralFx.Play(efeito, posição [, direção] [, escala])
    /// Aura:   var h = ProceduralFx.Play(efeitoComLoop, pos);  ...  h.Stop();
    /// </summary>
    [CreateAssetMenu(menuName = "RPG/Procedural Effect", fileName = "FX_New")]
    public class ProceduralEffect : ScriptableObject
    {
        [Header("Geral")]
        [Tooltip("Multiplica TODOS os tamanhos. Permite reusar um efeito em vários tamanhos.")]
        [Min(0.01f)] public float globalScale = 1f;
        [Tooltip("Aura/canalização: repete até alguém chamar handle.Stop().")]
        public bool loop = false;
        [Tooltip("Som tocado ao disparar (opcional).")]
        public AudioClip sound;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("Intensidade de screen shake ao disparar (0 = nenhum).")]
        [Range(0f, 0.6f)] public float screenShake = 0f;

        [Header("Módulos (ligue os que quiser)")]
        public DiscModule      disc      = new DiscModule      { enabled = false };
        public ShockwaveModule shockwave = new ShockwaveModule { enabled = false };
        public RingModule      ring      = new RingModule();
        public RingModule      ring2     = new RingModule      { enabled = false };
        public BurstModule     burst     = new BurstModule();
        public ColumnModule    column    = new ColumnModule    { enabled = false };
        public OrbitModule     orbit     = new OrbitModule     { enabled = false };
        public BeamModule      beam      = new BeamModule      { enabled = false };
        public FlashModule     flash     = new FlashModule     { enabled = false };

        public float MaxLifetime()
        {
            if (loop) return float.PositiveInfinity;
            float m = 0.05f;
            if (disc.enabled)      m = Mathf.Max(m, disc.duration);
            if (shockwave.enabled) m = Mathf.Max(m, shockwave.duration);
            if (ring.enabled)      m = Mathf.Max(m, ring.duration);
            if (ring2.enabled)     m = Mathf.Max(m, ring2.duration);
            if (burst.enabled)     m = Mathf.Max(m, burst.lifetime + burst.startDelay + 0.15f);
            if (column.enabled)    m = Mathf.Max(m, column.duration + column.lifetime);
            if (orbit.enabled)     m = Mathf.Max(m, orbit.duration);
            if (beam.enabled)      m = Mathf.Max(m, beam.duration);
            if (flash.enabled)     m = Mathf.Max(m, flash.duration);
            return m;
        }
    }

    public enum RingMode  { Expand, FillUp, Pulse }
    public enum DiscMode  { Expand, FillUp, Static }
    public enum BurstShape { Sphere, Hemisphere, Cone }
    public enum FxBlend   { Additive, AlphaBlended }

    [System.Serializable]
    public class DiscModule
    {
        public bool     enabled = false;
        public Color    color   = new Color(1f, 0.3f, 0.2f, 0.5f);
        public DiscMode mode    = DiscMode.FillUp;
        [Min(0.1f)] public float radius   = 3f;
        [Min(0.05f)] public float duration = 0.6f;
        public bool fadeOut = true;
    }

    [System.Serializable]
    public class RingModule
    {
        public bool      enabled = true;
        public Color     color   = new Color(1f, 0.6f, 0.2f, 1f);
        [Tooltip("Expand: cresce e some (impacto). FillUp: cresce e fica (telegraph). Pulse: pisca no raio (área ativa).")]
        public RingMode  mode    = RingMode.Expand;
        [Min(0f)] public float startRadius = 0.2f;
        [Min(0f)] public float endRadius   = 3f;
        [Min(0.01f)] public float width    = 0.12f;
        [Range(8, 128)] public int segments = 48;
        [Min(0.05f)] public float duration  = 0.4f;
        [Tooltip("Graus/seg que o anel gira (visível com width irregular ou textura).")]
        public float rotationSpeed = 0f;
        [Tooltip("Curva do raio (0→1). Deixe linear p/ crescimento constante.")]
        public AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public bool fadeOut = true;
    }

    [System.Serializable]
    public class BurstModule
    {
        public bool       enabled = true;
        public Color      color   = new Color(1f, 0.75f, 0.3f, 1f);
        [Tooltip("Cor no FIM da vida da partícula (gradiente). Deixe = color p/ cor única.")]
        public Color      colorEnd = new Color(1f, 0.75f, 0.3f, 1f);
        public FxBlend    blend   = FxBlend.Additive;
        [Range(1, 400)] public int count = 24;
        [Min(0f)] public float speed     = 4f;
        [Min(0.01f)] public float size   = 0.15f;
        [Tooltip("Tamanho final relativo (1 = igual, 0 = encolhe até sumir, 2 = dobra).")]
        [Min(0f)] public float endSizeMul = 1f;
        [Min(0.05f)] public float lifetime = 0.5f;
        [Tooltip("Atraso antes de emitir (p/ bursts encadeados).")]
        [Min(0f)] public float startDelay = 0f;
        [Tooltip("Gravidade: negativo sobe, positivo cai.")]
        public float gravity = 0f;
        public BurstShape shape = BurstShape.Sphere;
        [Range(1f, 90f)] public float coneAngle = 25f;
        [Tooltip("Rastro nas partículas (trail). Bom p/ faíscas/cometas.")]
        public bool trail = false;
    }

    [System.Serializable]
    public class BeamModule
    {
        public bool  enabled = false;
        public Color color   = new Color(0.55f, 0.85f, 1f, 1f);
        [Min(0.01f)] public float width  = 0.25f;
        [Min(0.1f)]  public float length = 10f;
        [Min(0.05f)] public float duration = 0.25f;
        public bool fade = true;
        [Header("Raio (zig-zag)")]
        [Tooltip("Liga o visual de relâmpago quebrado.")]
        public bool jagged = false;
        [Range(2, 40)] public int  jagSegments = 12;
        [Min(0f)] public float jagAmplitude = 0.5f;
    }

    [System.Serializable]
    public class FlashModule
    {
        public bool  enabled = false;
        public Color color    = Color.white;
        [Min(0f)] public float intensity = 4f;
        [Min(0f)] public float range     = 6f;
        [Min(0.02f)] public float duration = 0.15f;
        public bool flicker = false;
    }

    /// <summary>Onda de choque: disco no chão que expande rápido e some (impacto pesado).</summary>
    [System.Serializable]
    public class ShockwaveModule
    {
        public bool     enabled = false;
        public Color    color   = new Color(1f, 0.9f, 0.6f, 0.7f);
        [Min(0.1f)] public float startRadius = 0.2f;
        [Min(0.2f)] public float endRadius   = 5f;
        [Min(0.05f)] public float duration   = 0.35f;
        [Tooltip("Espessura do anel da onda.")]
        [Min(0.02f)] public float thickness  = 0.4f;
        [Tooltip("Curva da expansão (rápida no início = EaseOut).")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    /// <summary>Coluna/pilar de partículas que sobe (geyser, invocação, buff de poder).</summary>
    [System.Serializable]
    public class ColumnModule
    {
        public bool     enabled = false;
        public Color    color   = new Color(0.6f, 0.4f, 1f, 1f);
        public Color    colorEnd = new Color(0.8f, 0.7f, 1f, 0f);
        public FxBlend  blend   = FxBlend.Additive;
        [Range(1, 400)] public int count = 60;
        [Tooltip("Altura que a coluna alcança (m).")]
        [Min(0.2f)] public float height = 4f;
        [Tooltip("Raio da base da coluna (m).")]
        [Min(0.05f)] public float radius = 0.6f;
        [Min(0.01f)] public float size  = 0.18f;
        [Min(0.05f)] public float lifetime = 0.8f;
        [Tooltip("Por quanto tempo emite (s).")]
        [Min(0.05f)] public float duration = 0.5f;
    }

    /// <summary>Partículas orbitando o centro (aura de energia, carregamento, buff ativo).</summary>
    [System.Serializable]
    public class OrbitModule
    {
        public bool     enabled = false;
        public Color    color   = new Color(0.4f, 0.8f, 1f, 1f);
        [Range(1, 24)] public int count = 6;
        [Tooltip("Raio da órbita (m).")]
        [Min(0.1f)] public float radius = 1.2f;
        [Tooltip("Velocidade angular (graus/seg).")]
        public float angularSpeed = 180f;
        [Min(0.02f)] public float size = 0.2f;
        [Min(0.05f)] public float duration = 1f;
        [Tooltip("Altura vertical da órbita acima do centro (m).")]
        public float height = 1f;
        [Tooltip("Se o raio 'puxa para dentro' ao longo do tempo (sugar energia).")]
        public bool  contract = false;
    }
}
