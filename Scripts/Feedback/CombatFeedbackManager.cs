using System.Collections.Generic;
using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Sabor visual de um número de combate. Define cor e tamanho.
    /// </summary>
    public enum HitFlavor : byte
    {
        Physical = 0,  // branco
        Magical  = 1,  // azul
        Heal     = 2,  // verde (prefixo +)
        Miss     = 3,  // cinza ("MISS")
    }

    /// <summary>
    /// Núcleo do "game feel": números de dano flutuantes (pooled) e screen shake.
    ///
    /// • Singleton client-side. Auto-cria se ninguém colocou na cena, então
    ///   "só funciona" — mas você pode arrastar um na cena para configurar cores.
    /// • Sem TextMeshPro: usa TextMesh (built-in). Faz billboard pra câmera.
    /// • [DefaultExecutionOrder(1000)] garante que o shake é aplicado DEPOIS
    ///   do script de follow da câmera, então o tremor não é sobrescrito.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CombatFeedbackManager : MonoBehaviour
    {
        // ── Singleton ───────────────────────────────────────────────────────
        private static CombatFeedbackManager _instance;
        public static CombatFeedbackManager Instance
        {
            get
            {
                if (_instance == null && Application.isPlaying)
                {
                    var go = new GameObject("[CombatFeedbackManager]");
                    _instance = go.AddComponent<CombatFeedbackManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("Cores dos números")]
        [SerializeField] private Color _physicalColor = Color.white;
        [SerializeField] private Color _magicalColor  = new Color(0.56f, 0.83f, 1f);
        [SerializeField] private Color _critColor     = new Color(1f, 0.69f, 0.13f);
        [SerializeField] private Color _healColor     = new Color(0.40f, 0.88f, 0.42f);
        [SerializeField] private Color _missColor     = new Color(0.7f, 0.7f, 0.7f);

        [Header("Movimento dos números")]
        [SerializeField] private float _lifetime      = 0.9f;
        [SerializeField] private float _riseSpeed      = 1.8f;
        [SerializeField] private float _horizontalJitter = 0.5f;
        [SerializeField] private float _baseCharSize   = 0.08f;
        [SerializeField] private int   _baseFontSize    = 64;

        [Header("Pool")]
        [SerializeField] private int _maxLivePopups = 40;

        [Header("Screen shake")]
        [SerializeField] private float _shakeDecay = 1.6f;
        [SerializeField] private float _maxShake   = 0.6f;

        private readonly Queue<FloatingCombatText> _pool = new Queue<FloatingCombatText>();
        private readonly List<FloatingCombatText>  _live = new List<FloatingCombatText>();
        private Font _font;

        private Transform _camT;
        private float     _shakeAmount;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            // Fonte built-in (Unity 2022+ usa LegacyRuntime.ttf; antigos, Arial.ttf).
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Mostra um número de dano/cura no mundo.</summary>
        public void ShowDamage(Vector3 worldPos, float amount, bool crit, HitFlavor flavor)
        {
            if (_font == null) return;

            Color color = flavor switch
            {
                HitFlavor.Magical => _magicalColor,
                HitFlavor.Heal    => _healColor,
                HitFlavor.Miss    => _missColor,
                _                 => _physicalColor,
            };
            if (crit) color = _critColor;

            string text = flavor switch
            {
                HitFlavor.Miss => "MISS",
                HitFlavor.Heal => "+" + Mathf.RoundToInt(amount),
                _              => Mathf.RoundToInt(amount).ToString(),
            };

            float scale = crit ? 1.6f : 1f;
            SpawnPopup(worldPos, text, color, scale, crit);
        }

        /// <summary>Aplica um tremor de tela. intensity ~0.1 (leve) a 0.6 (forte).</summary>
        public void Shake(float intensity)
        {
            _shakeAmount = Mathf.Min(_maxShake, Mathf.Max(_shakeAmount, intensity));
        }

        // ══════════════════════════════════════════════════════════════════
        // Internals
        // ══════════════════════════════════════════════════════════════════

        private void SpawnPopup(Vector3 pos, string text, Color color, float scale, bool crit)
        {
            // Limita o número de popups vivos pra não poluir nem custar caro.
            if (_live.Count >= _maxLivePopups)
            {
                var oldest = _live[0];
                _live.RemoveAt(0);
                oldest.ForceRecycle();
            }

            FloatingCombatText fct = _pool.Count > 0 ? _pool.Dequeue() : CreatePopup();
            fct.gameObject.SetActive(true);
            _live.Add(fct);

            float jitter = Random.Range(-_horizontalJitter, _horizontalJitter);
            fct.Play(
                pos + Vector3.up * 1.6f,
                text, color,
                _baseCharSize * scale,
                _lifetime,
                _riseSpeed * (crit ? 1.25f : 1f),
                jitter, crit);
        }

        private FloatingCombatText CreatePopup()
        {
            var go = new GameObject("DamagePopup");
            go.transform.SetParent(transform, false);

            var tm = go.AddComponent<TextMesh>();
            tm.font          = _font;
            tm.fontSize      = _baseFontSize;
            tm.characterSize = _baseCharSize;
            tm.anchor        = TextAnchor.MiddleCenter;
            tm.alignment     = TextAlignment.Center;
            tm.fontStyle     = FontStyle.Bold;

            var mr = go.GetComponent<MeshRenderer>();
            if (_font != null) mr.sharedMaterial = _font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var fct = go.AddComponent<FloatingCombatText>();
            fct.Init(this, tm);
            return fct;
        }

        internal void Recycle(FloatingCombatText fct)
        {
            _live.Remove(fct);
            fct.gameObject.SetActive(false);
            _pool.Enqueue(fct);
        }

        private void LateUpdate()
        {
            // Billboard: a câmera pode ter mudado; FloatingCombatText lê isto.
            if (_camT == null && Camera.main != null) _camT = Camera.main.transform;
            if (_camT == null) return;

            // ExecutionOrder=1000 garante que rodamos DEPOIS do follow da câmera,
            // que já escreveu a posição "limpa" deste frame. Apenas somamos o tremor;
            // como o follow reescreve do zero no próximo frame, não há acúmulo/drift.
            if (_shakeAmount > 0.0001f)
            {
                Vector3 offset = new Vector3(
                    Random.value * 2f - 1f,
                    Random.value * 2f - 1f,
                    0f) * _shakeAmount;
                _camT.localPosition += offset;
                _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, _shakeDecay * Time.deltaTime);
            }
        }

        public Transform CameraTransform => _camT != null ? _camT
            : (Camera.main != null ? (_camT = Camera.main.transform) : null);
    }
}
