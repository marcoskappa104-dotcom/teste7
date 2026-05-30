using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Um único número de combate. Animado por código (sem dependências):
    ///   • sobe com leve deriva lateral,
    ///   • "punch" de escala no nascimento (mais forte em crítico),
    ///   • fade out no final,
    ///   • billboard contínuo pra câmera.
    /// Gerenciado pelo pool do CombatFeedbackManager — nunca instancie direto.
    /// </summary>
    public class FloatingCombatText : MonoBehaviour
    {
        private CombatFeedbackManager _mgr;
        private TextMesh _tm;

        private float   _life;
        private float   _maxLife;
        private float   _rise;
        private float   _jitterX;
        private bool    _crit;
        private Color   _color;
        private Vector3 _startPos;

        public void Init(CombatFeedbackManager mgr, TextMesh tm)
        {
            _mgr = mgr;
            _tm  = tm;
        }

        public void Play(Vector3 worldPos, string text, Color color, float charSize,
                         float lifetime, float rise, float jitterX, bool crit)
        {
            _startPos      = worldPos;
            transform.position = worldPos;

            _tm.text          = text;
            _tm.characterSize = charSize;
            _color            = color;
            _tm.color         = color;

            _maxLife = Mathf.Max(0.1f, lifetime);
            _life    = 0f;
            _rise    = rise;
            _jitterX = jitterX;
            _crit    = crit;
        }

        public void ForceRecycle()
        {
            if (_mgr != null) _mgr.Recycle(this);
        }

        private void LateUpdate()
        {
            if (_tm == null) return;

            _life += Time.deltaTime;
            float t = _life / _maxLife;            // 0..1
            if (t >= 1f) { ForceRecycle(); return; }

            // Posição: sobe e deriva. Desaceleração suave (ease-out).
            float ease = 1f - (1f - t) * (1f - t);
            Vector3 pos = _startPos;
            pos.y += _rise * ease;
            pos.x += _jitterX * t;
            transform.position = pos;

            // Punch de escala: estoura no início e assenta.
            float punch = _crit ? 1.7f : 1.35f;
            float settle = Mathf.Lerp(punch, 1f, Mathf.Clamp01(t * 4f));
            float pop = (t < 0.12f) ? Mathf.Lerp(0.2f, settle, t / 0.12f) : settle;
            transform.localScale = Vector3.one * pop;

            // Fade: começa a sumir na segunda metade da vida.
            float alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
            Color c = _color; c.a = alpha;
            _tm.color = c;

            // Billboard.
            Transform cam = _mgr != null ? _mgr.CameraTransform : null;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
        }
    }
}
