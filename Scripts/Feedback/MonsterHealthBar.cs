using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Barra de vida flutuante sobre o monstro. 100% procedural (dois quads),
    /// sem prefab nem Canvas. Billboard pra câmera, cor verde→vermelho,
    /// aparece ao receber dano e some depois de um tempo ocioso.
    ///
    /// Não lê a vida sozinha — é alimentada por MonsterCombatFeedback (que recebe
    /// a fração de vida do servidor no momento do dano). Assim não depende de
    /// saber o nome do campo de HP do seu MonsterCombat.
    /// </summary>
    public class MonsterHealthBar : MonoBehaviour
    {
        private const float WIDTH  = 1.0f;
        private const float HEIGHT = 0.12f;

        private Transform _fill;
        private MeshRenderer _fillR, _bgR;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float _frac = 1f;
        private float _hideAt;          // Time.time em que deve sumir
        private float _autoHide = 4f;
        private bool  _visible;

        private static readonly Color BgColor   = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color FullColor = new Color(0.30f, 0.85f, 0.30f, 1f);
        private static readonly Color LowColor  = new Color(0.90f, 0.20f, 0.18f, 1f);

        public static MonsterHealthBar Create(Transform parent, float heightOffset)
        {
            var root = new GameObject("HealthBar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * heightOffset;

            var bar = root.AddComponent<MonsterHealthBar>();
            bar.Build();
            bar.SetVisible(false);
            return bar;
        }

        private void Build()
        {
            _mpb = new MaterialPropertyBlock();

            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            var mat = new Material(sh);

            var bg = MakeQuad("bg", mat, BgColor, out _bgR);
            bg.localScale = new Vector3(WIDTH, HEIGHT, 1f);
            bg.localPosition = Vector3.zero;

            var fillGo = MakeQuad("fill", mat, FullColor, out _fillR);
            _fill = fillGo;
            _fill.localPosition = new Vector3(0f, 0f, -0.01f);   // levemente à frente
            SetFraction(1f);
        }

        private Transform MakeQuad(string n, Material mat, Color c, out MeshRenderer mr)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = n;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(transform, false);
            mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetColor(mr, c);
            return go.transform;
        }

        /// <summary>Atualiza a barra com a fração de vida (0..1) e a mostra.</summary>
        public void SetHealth(float frac)
        {
            _frac = Mathf.Clamp01(frac);
            SetFraction(_frac);
            SetColor(_fillR, Color.Lerp(LowColor, FullColor, _frac));
            SetVisible(_frac > 0.001f);
            _hideAt = Time.time + _autoHide;
        }

        public void HideNow() => SetVisible(false);

        private void SetFraction(float f)
        {
            if (_fill == null) return;
            // Ancorado à esquerda: encolhe a largura e desloca pra esquerda.
            _fill.localScale = new Vector3(WIDTH * f, HEIGHT * 0.85f, 1f);
            _fill.localPosition = new Vector3(-(WIDTH * (1f - f)) * 0.5f, 0f, -0.01f);
        }

        private void SetVisible(bool v)
        {
            if (_visible == v) return;
            _visible = v;
            if (_bgR != null)   _bgR.enabled = v;
            if (_fillR != null) _fillR.enabled = v;
        }

        private void SetColor(MeshRenderer mr, Color c)
        {
            if (mr == null) return;
            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, c);
            mr.SetPropertyBlock(_mpb);
        }

        private void LateUpdate()
        {
            if (!_visible) return;

            // Some sozinha quando o monstro fica um tempo sem apanhar (e está cheio).
            if (Time.time >= _hideAt && _frac >= 0.999f)
                SetVisible(false);

            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
