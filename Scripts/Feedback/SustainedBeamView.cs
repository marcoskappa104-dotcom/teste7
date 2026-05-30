using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Feixe contínuo (laser) que fica preso ao atirador e gira conforme a direção
    /// enviada pelo servidor durante o canal. Núcleo branco + glow colorido.
    /// Cosmético/client-side. Criado e controlado pelos RPCs de beam do NetworkPlayer.
    /// </summary>
    public class SustainedBeamView : MonoBehaviour
    {
        private LineRenderer _core;   // núcleo fino e claro
        private LineRenderer _glow;   // halo mais grosso e translúcido
        private Transform    _follow; // atirador
        private Vector3      _localOrigin;
        private Vector3      _dir;
        private float        _range;
        private Color        _color;

        private bool  _active;
        private float _fade = 1f;     // 1 = visível; decresce no End

        public static SustainedBeamView Create()
        {
            var go = new GameObject("SustainedBeamView");
            return go.AddComponent<SustainedBeamView>();
        }

        private void Awake()
        {
            _glow = BuildLine("glow", 0.55f, 12);
            _core = BuildLine("core", 0.18f, 8);
        }

        private LineRenderer BuildLine(string n, float width, int order)
        {
            var child = new GameObject(n);
            child.transform.SetParent(transform, false);
            var lr = child.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 6;
            lr.widthMultiplier = width;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            lr.material = new Material(sh);
            lr.sortingOrder = order;
            lr.enabled = false;
            return lr;
        }

        public void Begin(Transform follow, Vector3 localOrigin, Vector3 dir, float range, Color color)
        {
            _follow = follow;
            _localOrigin = localOrigin;
            _dir = Flat(dir);
            _range = Mathf.Max(1f, range);
            _color = color;
            _active = true;
            _fade = 1f;
            if (_core != null) _core.enabled = true;
            if (_glow != null) _glow.enabled = true;
            UpdateLine();
        }

        public void SetDirection(Vector3 dir)
        {
            Vector3 f = Flat(dir);
            if (f.sqrMagnitude > 0.0001f) _dir = f;
        }

        public void End()
        {
            _active = false; // entra em fade-out; LateUpdate destrói no fim
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        private void LateUpdate()
        {
            if (_follow == null) { Destroy(gameObject); return; }

            if (!_active)
            {
                _fade -= Time.deltaTime * 6f; // some rápido (~0.16s)
                if (_fade <= 0f)
                {
                    if (_core != null) _core.enabled = false;
                    if (_glow != null) _glow.enabled = false;
                    Destroy(gameObject);
                    return;
                }
            }
            UpdateLine();
        }

        private void UpdateLine()
        {
            Vector3 origin = _follow.position + _localOrigin;
            Vector3 end    = origin + _dir * _range;

            SetLine(_core, origin, end, Color.Lerp(Color.white, _color, 0.25f), _fade);
            SetLine(_glow, origin, end, _color, _fade * 0.5f);

            // Leve pulsar no núcleo para parecer "energia".
            if (_core != null)
                _core.widthMultiplier = 0.18f * (0.85f + 0.15f * Mathf.Sin(Time.time * 30f));
        }

        private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b, Color c, float alpha)
        {
            if (lr == null) return;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            c.a *= Mathf.Clamp01(alpha);
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
        }
    }
}
