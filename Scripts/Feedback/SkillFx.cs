using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// VFX de skills 100% procedural — não precisa de prefab nem material de arte.
    /// Desenha anéis e linhas com LineRenderer e os anima por código.
    ///
    /// Chamado pelos RPCs cosméticos do NetworkPlayer (telegraph/impacto/skillshot).
    /// Tudo client-side e descartável; nada disto afeta o jogo autoritativo.
    /// </summary>
    public static class SkillFx
    {
        private static Material _ringMat;

        private static Material RingMaterial
        {
            get
            {
                if (_ringMat == null)
                {
                    // Shaders sempre presentes na engine; tenta o melhor disponível.
                    Shader sh = Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
                    _ringMat = new Material(sh);
                }
                return _ringMat;
            }
        }

        private static readonly Color WarnColor   = new Color(1f, 0.35f, 0.15f, 0.85f);
        private static readonly Color ImpactColor = new Color(1f, 0.78f, 0.30f, 1f);
        private static readonly Color BeamColor   = new Color(0.55f, 0.85f, 1f, 1f);

        // ══════════════════════════════════════════════════════════════════
        // Telegraph (meteoro): anel-alvo fixo + anel interno que "enche"
        // até o impacto, ao longo de 'delay' segundos.
        // ══════════════════════════════════════════════════════════════════
        public static void GroundTelegraph(Vector3 center, float radius, float delay)
        {
            var go = NewFxObject("FX_Telegraph", center);
            var fx = go.AddComponent<SkillFxInstance>();
            fx.RunTelegraph(center, radius, delay, WarnColor);
        }

        // ══════════════════════════════════════════════════════════════════
        // Impacto: anel brilhante que estoura além do raio e some rápido.
        // ══════════════════════════════════════════════════════════════════
        public static void GroundImpact(Vector3 center, float radius)
        {
            var go = NewFxObject("FX_Impact", center);
            var fx = go.AddComponent<SkillFxInstance>();
            fx.RunImpact(center, radius, ImpactColor);
        }

        // ══════════════════════════════════════════════════════════════════
        // Skillshot: feixe reto que aparece e some em ~0.25s.
        // ══════════════════════════════════════════════════════════════════
        public static void SkillshotTrail(Vector3 origin, Vector3 dir, float range)
        {
            var go = NewFxObject("FX_Skillshot", origin);
            var fx = go.AddComponent<SkillFxInstance>();
            fx.RunBeam(origin, origin + dir.normalized * range, BeamColor);
        }

        private static GameObject NewFxObject(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }

        // ──────────────────────────────────────────────────────────────────
        // Componente interno que anima e se autodestrói.
        // ──────────────────────────────────────────────────────────────────
        private class SkillFxInstance : MonoBehaviour
        {
            private const int SEGMENTS = 48;

            private LineRenderer _outer;   // anel-alvo / feixe
            private LineRenderer _inner;   // anel de preenchimento (telegraph)
            private float _t, _duration, _radius;
            private Color _color;
            private enum Kind { Telegraph, Impact, Beam }
            private Kind _kind;

            public void RunTelegraph(Vector3 center, float radius, float delay, Color color)
            {
                _kind = Kind.Telegraph; _duration = Mathf.Max(0.1f, delay);
                _radius = radius; _color = color;
                _outer = MakeRing(0.06f, true);
                _inner = MakeRing(0.10f, true);
                DrawCircle(_outer, center, radius);
            }

            public void RunImpact(Vector3 center, float radius, Color color)
            {
                _kind = Kind.Impact; _duration = 0.35f;
                _radius = radius; _color = color;
                _outer = MakeRing(0.20f, true);
                transform.position = center;
            }

            public void RunBeam(Vector3 a, Vector3 b, Color color)
            {
                _kind = Kind.Beam; _duration = 0.25f; _color = color;
                _outer = MakeLine(0.25f);
                _outer.positionCount = 2;
                _outer.SetPosition(0, a + Vector3.up * 1.0f);
                _outer.SetPosition(1, b + Vector3.up * 1.0f);
            }

            private LineRenderer MakeRing(float width, bool loop)
            {
                var child = new GameObject("ring");
                child.transform.SetParent(transform, false);
                var lr = child.AddComponent<LineRenderer>();
                ConfigureLine(lr, width);
                lr.loop = loop;
                lr.positionCount = SEGMENTS;
                return lr;
            }

            private LineRenderer MakeLine(float width)
            {
                var lr = gameObject.AddComponent<LineRenderer>();
                ConfigureLine(lr, width);
                lr.loop = false;
                return lr;
            }

            private void ConfigureLine(LineRenderer lr, float width)
            {
                lr.useWorldSpace = true;
                lr.material = RingMaterial;
                lr.widthMultiplier = width;
                lr.numCapVertices = 4;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.alignment = LineAlignment.View;
            }

            private static void DrawCircle(LineRenderer lr, Vector3 center, float radius)
            {
                float y = center.y + 0.05f;
                for (int i = 0; i < SEGMENTS; i++)
                {
                    float a = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(
                        center.x + Mathf.Cos(a) * radius, y,
                        center.z + Mathf.Sin(a) * radius));
                }
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float p = Mathf.Clamp01(_t / _duration);

                switch (_kind)
                {
                    case Kind.Telegraph:
                    {
                        // Anel externo pulsa; interno cresce 0→radius (conta regressiva).
                        Color outerC = _color; outerC.a = 0.55f + 0.25f * Mathf.Sin(_t * 14f);
                        SetColor(_outer, outerC);
                        DrawCircle(_inner, transform.position, Mathf.Lerp(0.05f, _radius, p));
                        Color innerC = _color; innerC.a = 0.9f;
                        SetColor(_inner, innerC);
                        break;
                    }
                    case Kind.Impact:
                    {
                        float r = Mathf.Lerp(_radius * 0.2f, _radius * 1.25f, p);
                        DrawCircle(_outer, transform.position, r);
                        Color c = _color; c.a = 1f - p;
                        SetColor(_outer, c);
                        break;
                    }
                    case Kind.Beam:
                    {
                        Color c = _color; c.a = 1f - p;
                        SetColor(_outer, c);
                        break;
                    }
                }

                if (_t >= _duration) Destroy(gameObject);
            }

            private static void SetColor(LineRenderer lr, Color c)
            {
                if (lr == null) return;
                lr.startColor = c;
                lr.endColor   = c;
            }
        }
    }
}
