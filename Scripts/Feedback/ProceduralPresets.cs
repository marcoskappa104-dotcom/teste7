using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Efeitos padrão gerados em CÓDIGO (sem assets), usados como fallback quando
    /// uma skill não tem um ProceduralEffect próprio na VfxLibrary.
    ///
    /// Antes isso era o "SkillFx" (só linhas). Agora os defaults passam pelo mesmo
    /// motor ProceduralFx — então o fallback já vem com anel + partículas + flash,
    /// e tudo fica unificado num sistema só.
    ///
    /// São criados uma única vez (cacheados). Não precisam estar na cena nem em disco.
    /// </summary>
    public static class ProceduralPresets
    {
        private static ProceduralEffect _impact, _telegraph, _skillshot, _self, _fizzle;

        /// <summary>Impacto genérico (laranja): shockwave + faíscas + flash curto.</summary>
        public static ProceduralEffect Impact => _impact ??= BuildImpact();

        /// <summary>Aviso de área no chão (vermelho): anel que enche + pulso.</summary>
        public static ProceduralEffect Telegraph => _telegraph ??= BuildTelegraph();

        /// <summary>Disparo de skillshot (azul): feixe curto + faíscas cônicas.</summary>
        public static ProceduralEffect Skillshot => _skillshot ??= BuildSkillshot();

        /// <summary>Cura/buff/nova em si mesmo (verde-claro): anel + partículas subindo.</summary>
        public static ProceduralEffect SelfCast => _self ??= BuildSelf();

        /// <summary>Dissipação no fim do alcance (sem dano): aro pequeno que some.</summary>
        public static ProceduralEffect Fizzle => _fizzle ??= BuildFizzle();

        // ──────────────────────────────────────────────────────────────────

        private static ProceduralEffect New(string name)
        {
            var fx = ScriptableObject.CreateInstance<ProceduralEffect>();
            fx.name = name;
            // Por padrão desliga tudo; cada builder liga o que precisa.
            fx.disc.enabled = false;
            fx.shockwave.enabled = false;
            fx.ring.enabled = false;
            fx.ring2.enabled = false;
            fx.burst.enabled = false;
            fx.column.enabled = false;
            fx.orbit.enabled = false;
            fx.beam.enabled = false;
            fx.flash.enabled = false;
            return fx;
        }

        private static ProceduralEffect BuildImpact()
        {
            var fx = New("Preset_Impact");
            fx.shockwave.enabled = true;
            fx.shockwave.color = new Color(1f, 0.78f, 0.35f, 0.8f);
            fx.shockwave.startRadius = 0.2f;
            fx.shockwave.endRadius = 2.2f;
            fx.shockwave.thickness = 0.3f;
            fx.shockwave.duration = 0.3f;

            fx.burst.enabled = true;
            fx.burst.color = new Color(1f, 0.7f, 0.3f, 1f);
            fx.burst.colorEnd = new Color(1f, 0.4f, 0.15f, 0f);
            fx.burst.count = 22;
            fx.burst.speed = 6f;
            fx.burst.size = 0.16f;
            fx.burst.endSizeMul = 0.25f;
            fx.burst.lifetime = 0.4f;
            fx.burst.shape = BurstShape.Hemisphere;

            fx.flash.enabled = true;
            fx.flash.color = new Color(1f, 0.7f, 0.3f);
            fx.flash.intensity = 3.5f;
            fx.flash.range = 5f;
            fx.flash.duration = 0.12f;
            return fx;
        }

        private static ProceduralEffect BuildTelegraph()
        {
            var fx = New("Preset_Telegraph");
            fx.ring.enabled = true;
            fx.ring.mode = RingMode.FillUp;
            fx.ring.color = new Color(1f, 0.3f, 0.15f, 0.9f);
            fx.ring.startRadius = 0.1f;
            fx.ring.endRadius = 1f;     // escalado pelo raio real no Play
            fx.ring.width = 0.12f;
            fx.ring.duration = 0.8f;

            fx.ring2.enabled = true;
            fx.ring2.mode = RingMode.Pulse;
            fx.ring2.color = new Color(1f, 0.3f, 0.15f, 0.7f);
            fx.ring2.endRadius = 1f;
            fx.ring2.width = 0.05f;
            fx.ring2.duration = 0.8f;
            return fx;
        }

        private static ProceduralEffect BuildSkillshot()
        {
            var fx = New("Preset_Skillshot");
            fx.beam.enabled = true;
            fx.beam.color = new Color(0.6f, 0.85f, 1f, 1f);
            fx.beam.width = 0.22f;
            fx.beam.length = 1f;        // escalado pelo range no Play
            fx.beam.duration = 0.22f;
            fx.beam.fade = true;

            fx.burst.enabled = true;
            fx.burst.color = new Color(0.6f, 0.85f, 1f, 1f);
            fx.burst.colorEnd = new Color(0.3f, 0.5f, 1f, 0f);
            fx.burst.count = 14;
            fx.burst.speed = 8f;
            fx.burst.size = 0.1f;
            fx.burst.lifetime = 0.25f;
            fx.burst.shape = BurstShape.Cone;
            fx.burst.coneAngle = 12f;
            fx.burst.trail = true;
            return fx;
        }

        private static ProceduralEffect BuildSelf()
        {
            var fx = New("Preset_Self");
            fx.ring.enabled = true;
            fx.ring.mode = RingMode.Expand;
            fx.ring.color = new Color(0.5f, 1f, 0.6f, 1f);
            fx.ring.startRadius = 1.2f;
            fx.ring.endRadius = 0.2f;   // fecha pra dentro
            fx.ring.width = 0.1f;
            fx.ring.duration = 0.5f;

            fx.burst.enabled = true;
            fx.burst.color = new Color(0.5f, 1f, 0.6f, 1f);
            fx.burst.colorEnd = new Color(0.7f, 1f, 0.8f, 0f);
            fx.burst.count = 24;
            fx.burst.speed = 2.5f;
            fx.burst.size = 0.14f;
            fx.burst.lifetime = 0.7f;
            fx.burst.gravity = -1.5f;   // sobe
            fx.burst.shape = BurstShape.Cone;
            fx.burst.coneAngle = 30f;

            fx.flash.enabled = true;
            fx.flash.color = new Color(0.5f, 1f, 0.6f);
            fx.flash.intensity = 2.5f;
            fx.flash.range = 4f;
            fx.flash.duration = 0.3f;
            return fx;
        }

        private static ProceduralEffect BuildFizzle()
        {
            var fx = New("Preset_Fizzle");
            fx.ring.enabled = true;
            fx.ring.mode = RingMode.Expand;
            fx.ring.color = new Color(0.8f, 0.85f, 1f, 0.7f);
            fx.ring.startRadius = 0.1f;
            fx.ring.endRadius = 0.6f;
            fx.ring.width = 0.06f;
            fx.ring.duration = 0.25f;

            fx.burst.enabled = true;
            fx.burst.color = new Color(0.8f, 0.85f, 1f, 0.8f);
            fx.burst.colorEnd = new Color(0.6f, 0.7f, 1f, 0f);
            fx.burst.count = 8;
            fx.burst.speed = 2f;
            fx.burst.size = 0.08f;
            fx.burst.lifetime = 0.3f;
            fx.burst.shape = BurstShape.Sphere;
            return fx;
        }
    }
}
