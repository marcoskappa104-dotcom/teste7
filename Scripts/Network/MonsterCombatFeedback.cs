using System.Collections;
using UnityEngine;
using Mirror;
using RPG.Feedback;

namespace RPG.Network
{
    /// <summary>
    /// Ponte de feedback de dano do MONSTRO para os clientes.
    ///
    /// Por que no monstro e não no player?
    ///   O dano final (pós-DEF, pós-crit) é calculado dentro de MonsterCombat,
    ///   no servidor. Reportando a partir daqui o número exibido bate exatamente
    ///   com a vida perdida — e UM único hook cobre ataque básico, arma e skills.
    ///
    /// Integração (1 linha): em MonsterCombat.ServerTakeDamageFromPlayer, depois
    /// de calcular o dano final, chame (versão recomendada, com a vida pós-dano):
    ///     GetComponent&lt;MonsterCombatFeedback&gt;()?.ServerReportHit(dano, foiCrit, ehMagico, vidaAtual, vidaMax);
    /// A versão curta (sem vida) também funciona — só não mostra a barra:
    ///     GetComponent&lt;MonsterCombatFeedback&gt;()?.ServerReportHit(dano, foiCrit, ehMagico);
    /// E ao morrer, opcionalmente:
    ///     GetComponent&lt;MonsterCombatFeedback&gt;()?.ServerReportDeath();
    ///
    /// Coloque este componente no prefab do monstro. Funciona com ou sem renderers.
    /// </summary>
    public class MonsterCombatFeedback : NetworkBehaviour
    {
        [Header("Flash de acerto")]
        [SerializeField] private Color _flashColor = Color.white;
        [SerializeField] private float _flashDuration = 0.10f;

        [Header("Barra de vida flutuante")]
        [SerializeField] private bool  _showHealthBar = true;
        [Tooltip("Altura da barra acima do pivô do monstro (m).")]
        [SerializeField] private float _healthBarHeight = 2.0f;

        [Header("Hit-stop (congela a animação no impacto)")]
        [Tooltip("Pausa brevemente a animação do monstro ao apanhar. Seguro p/ rede: " +
                 "mexe só no Animator.speed local, nunca no Time.timeScale.")]
        [SerializeField] private bool  _hitStop = true;
        [Tooltip("Duração do congelamento (s, tempo real).")]
        [SerializeField] private float _hitStopDuration = 0.06f;
        [Tooltip("Hit-stop extra em crítico.")]
        [SerializeField] private float _hitStopCritBonus = 0.05f;

        [Header("Squash & stretch (deforma o mesh no impacto)")]
        [Tooltip("Amassa/estica rapidamente o visual ao apanhar. Aplicado numa transform " +
                 "FILHA (nunca na raiz), então não afeta colisor nem rede.")]
        [SerializeField] private bool  _squashStretch = true;
        [Tooltip("Intensidade: 0.25 = 25% de deformação.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float _squashAmount = 0.22f;
        [Tooltip("Duração total da deformação (s, tempo real).")]
        [SerializeField] private float _squashDuration = 0.18f;

        [Header("Screen shake (só para quem está perto)")]
        [Tooltip("Distância máx. da câmera para o golpe sacudir a tela.")]
        [SerializeField] private float _shakeMaxDistance = 18f;
        [SerializeField] private float _shakeBase = 0.12f;
        [SerializeField] private float _shakeCritBonus = 0.18f;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private MonsterHealthBar _healthBar;
        private Animator _animator;
        private float    _animatorBaseSpeed = 1f;
        private Coroutine _hitStopCo;
        private Transform _visualT;            // transform filha onde aplicar o squash
        private Vector3   _visualBaseScale = Vector3.one;
        private Coroutine _squashCo;
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId  = Shader.PropertyToID("_EmissionColor");

        private Coroutine _flashCo;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _animator  = GetComponentInChildren<Animator>();
            if (_animator != null) _animatorBaseSpeed = _animator.speed;
            _mpb = new MaterialPropertyBlock();

            _visualT = ResolveVisualTransform();
            if (_visualT != null) _visualBaseScale = _visualT.localScale;
        }

        // Acha uma transform FILHA segura para deformar (nunca a raiz, que tem
        // colisor/NetworkTransform). Preferência: a do Animator; senão, a 1ª filha
        // que tenha Renderer. Se não houver filha, o squash é desligado.
        private Transform ResolveVisualTransform()
        {
            if (_animator != null && _animator.transform != transform)
                return _animator.transform;

            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.GetComponentInChildren<Renderer>() != null)
                    return c;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // Servidor → reporta um acerto com o dano REAL já calculado.
        // hpFrac negativo = "não sei a vida" → não mexe na barra.
        // ══════════════════════════════════════════════════════════════════
        [Server]
        public void ServerReportHit(float finalDamage, bool crit, bool magical,
                                    float currentHP = -1f, float maxHP = -1f)
        {
            byte flavor = magical ? (byte)HitFlavor.Magical : (byte)HitFlavor.Physical;
            float hpFrac = (currentHP >= 0f && maxHP > 0f)
                ? Mathf.Clamp01(currentHP / maxHP)
                : -1f;
            RpcOnHit(finalDamage, crit, flavor, hpFrac);
        }

        [Server]
        public void ServerReportMiss()
        {
            RpcOnHit(0f, false, (byte)HitFlavor.Miss, -1f);
        }

        [Server]
        public void ServerReportDeath()
        {
            RpcOnDeath();
        }

        // ══════════════════════════════════════════════════════════════════
        // Clientes → número, flash, shake e barra de vida.
        // ══════════════════════════════════════════════════════════════════
        [ClientRpc]
        private void RpcOnHit(float amount, bool crit, byte flavor, float hpFrac)
        {
            if (Application.isBatchMode) return;
            var mgr = CombatFeedbackManager.Instance;
            if (mgr == null) return;

            mgr.ShowDamage(transform.position, amount, crit, (HitFlavor)flavor);

            if ((HitFlavor)flavor != HitFlavor.Miss)
            {
                if (_flashCo != null) StopCoroutine(_flashCo);
                _flashCo = StartCoroutine(FlashRoutine());

                if (_hitStop && _animator != null)
                {
                    if (_hitStopCo != null) StopCoroutine(_hitStopCo);
                    float dur = _hitStopDuration + (crit ? _hitStopCritBonus : 0f);
                    _hitStopCo = StartCoroutine(HitStopRoutine(dur));
                }

                if (_squashStretch && _visualT != null)
                {
                    if (_squashCo != null) StopCoroutine(_squashCo);
                    float amt = _squashAmount * (crit ? 1.4f : 1f);
                    _squashCo = StartCoroutine(SquashRoutine(amt));
                }

                if (_showHealthBar && hpFrac >= 0f)
                {
                    if (_healthBar == null)
                        _healthBar = MonsterHealthBar.Create(transform, _healthBarHeight);
                    _healthBar.SetHealth(hpFrac);
                }

                // Shake só se a câmera estiver por perto (evita tremor de luta distante).
                var cam = Camera.main;
                if (cam != null)
                {
                    float d = Vector3.Distance(cam.transform.position, transform.position);
                    if (d <= _shakeMaxDistance)
                    {
                        float falloff = 1f - (d / _shakeMaxDistance);
                        float s = (_shakeBase + (crit ? _shakeCritBonus : 0f)) * falloff;
                        mgr.Shake(s);
                    }
                }
            }
        }

        [ClientRpc]
        private void RpcOnDeath()
        {
            if (Application.isBatchMode) return;
            if (_healthBar != null) _healthBar.HideNow();
        }

        private System.Collections.IEnumerator SquashRoutine(float amount)
        {
            // Amassa (achata em Y, alarga em XZ) e volta com leve overshoot elástico.
            // Sempre parte/retorna da escala-BASE → hits seguidos não acumulam.
            float t = 0f;
            float dur = Mathf.Max(0.05f, _squashDuration);

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);

                // Oscilação amortecida: amassado no impacto (p=0) → estica (overshoot)
                // → assenta na base (p=1). k>0 amassa, k<0 estica.
                float k = amount * (1f - p) * Mathf.Cos(p * Mathf.PI * 2f);

                var sc = _visualBaseScale;
                sc.y *= 1f - k;          // amassa = Y menor
                sc.x *= 1f + k * 0.5f;   // ...e mais largo
                sc.z *= 1f + k * 0.5f;
                if (_visualT != null) _visualT.localScale = sc;

                yield return null;
            }

            if (_visualT != null) _visualT.localScale = _visualBaseScale;
            _squashCo = null;
        }

        private System.Collections.IEnumerator HitStopRoutine(float duration)
        {
            // Quase congela e restaura para a velocidade-BASE (capturada no Awake)
            // após 'duration' em TEMPO REAL. Restaurar para a base — e não para a
            // velocidade atual — evita travar o monstro se levar vários hits seguidos.
            _animator.speed = 0.02f;
            yield return new WaitForSecondsRealtime(duration);
            if (_animator != null) _animator.speed = _animatorBaseSpeed;
            _hitStopCo = null;
        }

        private IEnumerator FlashRoutine()
        {
            if (_renderers == null || _renderers.Length == 0) yield break;

            float t = 0f;
            while (t < _flashDuration)
            {
                t += Time.deltaTime;
                float k = 1f - (t / _flashDuration);   // 1 → 0
                ApplyTint(_flashColor * k);
                yield return null;
            }
            ApplyTint(Color.black);   // limpa o override (emissão zero)
            _flashCo = null;
        }

        private void ApplyTint(Color emissive)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                // Define propriedades comuns; o shader usa as que existir, ignora o resto.
                _mpb.SetColor(EmissionId, emissive);
                _mpb.SetColor(BaseColorId, Color.white + emissive);
                _mpb.SetColor(ColorId, Color.white + emissive);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}