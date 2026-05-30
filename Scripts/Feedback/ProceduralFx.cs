using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>Controle de um efeito em andamento (para auras/canalização).</summary>
    public class FxHandle
    {
        internal ProceduralFx.ProceduralFxRunner runner;
        public bool IsAlive => runner != null;
        /// <summary>Para um efeito em loop suavemente (deixa o que já saiu terminar).</summary>
        public void Stop()        { if (runner != null) runner.StopLoop(); }
        /// <summary>Destrói imediatamente.</summary>
        public void Kill()        { if (runner != null) Object.Destroy(runner.gameObject); }
        /// <summary>Move o efeito (ex.: aura presa no player).</summary>
        public void Move(Vector3 p){ if (runner != null) runner.transform.position = p; }
    }

    /// <summary>
    /// Motor de efeitos procedurais. Lê um ProceduralEffect e monta em runtime
    /// disco, anéis, feixe/raio, partículas e flash de luz. Anima e se autodestrói
    /// (ou faz loop até Stop). Sem prefab/asset de arte.
    ///
    ///   ProceduralFx.Play(fx, pos);
    ///   ProceduralFx.Play(fx, pos, dir);
    ///   ProceduralFx.Play(fx, pos, dir, scale);
    ///   var h = ProceduralFx.Play(auraFx, pos); ... h.Stop();
    /// </summary>
    public static class ProceduralFx
    {
        private static Material _lineMat, _addMat, _alphaMat, _discMat;

        private static Material LineMaterial => _lineMat ??= MakeMat(
            Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
        private static Material AddMaterial => _addMat ??= MakeMat(
            Shader.Find("Legacy Shaders/Particles/Additive")
            ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default"));
        private static Material AlphaMaterial => _alphaMat ??= MakeMat(
            Shader.Find("Legacy Shaders/Particles/Alpha Blended")
            ?? Shader.Find("Sprites/Default"));
        private static Material DiscMaterial => _discMat ??= MakeMat(
            Shader.Find("Legacy Shaders/Particles/Additive")
            ?? Shader.Find("Sprites/Default"));

        private static Material MakeMat(Shader s) => new Material(s);

        public static FxHandle Play(ProceduralEffect fx, Vector3 position)
            => Play(fx, position, Vector3.forward, 1f);
        public static FxHandle Play(ProceduralEffect fx, Vector3 position, Vector3 direction)
            => Play(fx, position, direction, 1f);

        public static FxHandle Play(ProceduralEffect fx, Vector3 position, Vector3 direction, float extraScale)
        {
            if (fx == null || Application.isBatchMode) return new FxHandle();

            var go = new GameObject("FX_" + fx.name);
            go.transform.position = position;
            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            if (fx.sound != null)
                AudioSource.PlayClipAtPoint(fx.sound, position, Mathf.Clamp01(fx.volume));

            var runner = go.AddComponent<ProceduralFxRunner>();
            runner.Build(fx, position, dir, fx.globalScale * Mathf.Max(0.01f, extraScale),
                         LineMaterial, AddMaterial, AlphaMaterial, DiscMaterial);

            return new FxHandle { runner = runner };
        }

        /// <summary>Toca o efeito preso a uma transform (segue o objeto). Bom p/ auras/trails.</summary>
        public static FxHandle PlayAttached(ProceduralEffect fx, Transform follow)
            => PlayAttached(fx, follow, Vector3.forward, 1f);

        public static FxHandle PlayAttached(ProceduralEffect fx, Transform follow, Vector3 direction, float extraScale)
        {
            if (fx == null || follow == null || Application.isBatchMode) return new FxHandle();
            var handle = Play(fx, follow.position, direction, extraScale);
            if (handle.runner != null) handle.runner.Follow(follow);
            return handle;
        }

        // ──────────────────────────────────────────────────────────────────
        public class ProceduralFxRunner : MonoBehaviour
        {
            private ProceduralEffect _fx;
            private Vector3 _center, _dir;
            private float _scale, _t, _maxLife;
            private bool _looping;

            private LineRenderer _ring1, _ring2, _beam;
            private Transform _disc;
            private LineRenderer _shock;
            private Light _flash;
            private float _ring1Rot, _ring2Rot;
            private ParticleSystem _loopPs;
            private Transform _follow;
            private Transform[] _orbitNodes;
            private OrbitModule _orbitCfg;

            public void Follow(Transform t) => _follow = t;

            private Material _lineMat, _addMat, _alphaMat, _discMat;

            public void Build(ProceduralEffect fx, Vector3 center, Vector3 dir, float scale,
                              Material line, Material add, Material alpha, Material disc)
            {
                _fx = fx; _center = center; _dir = dir; _scale = scale;
                _lineMat = line; _addMat = add; _alphaMat = alpha; _discMat = disc;
                _looping = fx.loop;
                _maxLife = fx.MaxLifetime();

                if (fx.disc.enabled)      _disc  = MakeDisc(fx.disc);
                if (fx.shockwave.enabled) _shock = MakeShockwave(fx.shockwave);
                if (fx.ring.enabled)      _ring1 = MakeRing(fx.ring,  _lineMat);
                if (fx.ring2.enabled)     _ring2 = MakeRing(fx.ring2, _lineMat);
                if (fx.beam.enabled)      _beam  = MakeBeam(fx.beam,  _lineMat);
                if (fx.burst.enabled)     _loopPs = MakeBurst(fx.burst);
                if (fx.column.enabled)    MakeColumn(fx.column);
                if (fx.orbit.enabled)     MakeOrbit(fx.orbit);
                if (fx.flash.enabled)     _flash = MakeFlash(fx.flash);

                // Screen shake opcional ao disparar.
                if (fx.screenShake > 0.001f)
                {
                    var mgr = CombatFeedbackManager.Instance;
                    if (mgr != null) mgr.Shake(fx.screenShake);
                }

                if (!_looping) Destroy(gameObject, _maxLife + 0.1f);
            }

            public void StopLoop()
            {
                if (_loopPs != null)
                {
                    var em = _loopPs.emission; em.enabled = false;
                    _loopPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                // deixa o último ciclo de anel/feixe terminar
                Destroy(gameObject, 1.0f);
            }

            // ── Construção ────────────────────────────────────────────────
            private Transform MakeDisc(DiscModule m)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "disc";
                var col = go.GetComponent<Collider>(); if (col) Destroy(col);
                go.transform.SetParent(transform, false);
                go.transform.position = _center + Vector3.up * 0.04f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // deita no chão
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _discMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                SetRendererColor(mr, m.color);
                return go.transform;
            }

            private LineRenderer MakeRing(RingModule m, Material mat)
            {
                var child = new GameObject("ring");
                child.transform.SetParent(transform, false);
                var lr = child.AddComponent<LineRenderer>();
                ConfigLine(lr, mat, m.width * _scale);
                lr.loop = true;
                lr.positionCount = m.segments;
                DrawCircle(lr, _center, m.startRadius * _scale, m.segments, 0f);
                SetLineColor(lr, m.color);
                return lr;
            }

            private LineRenderer MakeBeam(BeamModule m, Material mat)
            {
                var child = new GameObject("beam");
                child.transform.SetParent(transform, false);
                var lr = child.AddComponent<LineRenderer>();
                ConfigLine(lr, mat, m.width * _scale);
                lr.loop = false;
                DrawBeam(lr, m);
                SetLineColor(lr, m.color);
                return lr;
            }

            private ParticleSystem MakeBurst(BurstModule m)
            {
                var child = new GameObject("burst");
                child.transform.SetParent(transform, false);
                child.transform.position = _center + Vector3.up * 0.5f;
                if (m.shape == BurstShape.Cone)
                    child.transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);

                var ps = child.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration       = _looping ? 1f : 0.3f;
                main.loop           = _looping;
                main.playOnAwake    = false;
                main.startLifetime  = m.lifetime;
                main.startSpeed     = m.speed * _scale;
                main.startSize      = m.size * _scale;
                main.startColor     = m.color;
                main.gravityModifier = m.gravity;
                main.maxParticles   = Mathf.Max(m.count * (_looping ? 4 : 1), 32);
                main.startDelay     = m.startDelay;

                var emission = ps.emission;
                emission.enabled = true;
                if (_looping)
                {
                    emission.rateOverTime = m.count;
                }
                else
                {
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)m.count) });
                }

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = m.shape switch
                {
                    BurstShape.Hemisphere => ParticleSystemShapeType.Hemisphere,
                    BurstShape.Cone       => ParticleSystemShapeType.Cone,
                    _                     => ParticleSystemShapeType.Sphere,
                };
                if (m.shape == BurstShape.Cone) shape.angle = m.coneAngle;
                shape.radius = 0.1f * _scale;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(m.color, 0f), new GradientColorKey(m.colorEnd, 1f) },
                    new[] { new GradientAlphaKey(Mathf.Max(m.color.a, 0.01f), 0f),
                            new GradientAlphaKey(0f, 1f) });
                col.color = grad;

                if (Mathf.Abs(m.endSizeMul - 1f) > 0.01f)
                {
                    var sol = ps.sizeOverLifetime;
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f,
                        AnimationCurve.Linear(0f, 1f, 1f, Mathf.Max(0f, m.endSizeMul)));
                }

                var psr = ps.GetComponent<ParticleSystemRenderer>();
                psr.material = (m.blend == FxBlend.Additive) ? _addMat : _alphaMat;
                psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                psr.receiveShadows = false;

                if (m.trail)
                {
                    var trails = ps.trails;
                    trails.enabled = true;
                    trails.mode = ParticleSystemTrailMode.PerParticle;
                    trails.lifetime = new ParticleSystem.MinMaxCurve(0.3f);
                    trails.dieWithParticles = true;
                    psr.trailMaterial = _addMat;
                }

                ps.Play();
                return _looping ? ps : null;
            }

            private Light MakeFlash(FlashModule m)
            {
                var child = new GameObject("flash");
                child.transform.SetParent(transform, false);
                child.transform.position = _center + Vector3.up * 0.5f;
                var l = child.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = m.color;
                l.intensity = m.intensity;
                l.range = m.range * _scale;
                l.shadows = LightShadows.None;
                return l;
            }

            private LineRenderer MakeShockwave(ShockwaveModule m)
            {
                var child = new GameObject("shockwave");
                child.transform.SetParent(transform, false);
                var lr = child.AddComponent<LineRenderer>();
                ConfigLine(lr, _lineMat, m.thickness * _scale);
                lr.loop = true;
                lr.positionCount = 56;
                DrawCircle(lr, _center, m.startRadius * _scale, 56, 0f);
                SetLineColor(lr, m.color);
                return lr;
            }

            private void MakeColumn(ColumnModule m)
            {
                var child = new GameObject("column");
                child.transform.SetParent(transform, false);
                child.transform.position = _center;

                var ps = child.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.duration = m.duration;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = m.lifetime;
                main.startSpeed = (m.height / Mathf.Max(0.1f, m.lifetime)) * _scale;
                main.startSize = m.size * _scale;
                main.startColor = m.color;
                main.maxParticles = Mathf.Max(m.count * 2, 64);

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = m.count / Mathf.Max(0.1f, m.duration);

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 2f;                 // quase reto p/ cima
                shape.radius = m.radius * _scale;
                child.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // emite p/ cima

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(m.color, 0f), new GradientColorKey(m.colorEnd, 1f) },
                    new[] { new GradientAlphaKey(Mathf.Max(m.color.a, 0.01f), 0f),
                            new GradientAlphaKey(0f, 1f) });
                col.color = grad;

                var psr = ps.GetComponent<ParticleSystemRenderer>();
                psr.material = (m.blend == FxBlend.Additive) ? _addMat : _alphaMat;
                psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                psr.receiveShadows = false;

                ps.Play();
            }

            private void MakeOrbit(OrbitModule m)
            {
                _orbitCfg = m;
                _orbitNodes = new Transform[Mathf.Max(1, m.count)];
                for (int i = 0; i < _orbitNodes.Length; i++)
                {
                    var node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    node.name = "orbit_" + i;
                    var c = node.GetComponent<Collider>(); if (c != null) Destroy(c);
                    node.transform.SetParent(transform, false);
                    node.transform.localScale = Vector3.one * m.size * _scale;
                    var mr = node.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = _addMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    SetRendererColor(mr, m.color);
                    _orbitNodes[i] = node.transform;
                }
            }

            // ── Animação ──────────────────────────────────────────────────
            private void Update()
            {
                _t += Time.deltaTime;

                if (_follow != null)
                {
                    transform.position = _follow.position;
                    _center = _follow.position;
                }

                if (_ring1 != null) { _ring1Rot += _fx.ring.rotationSpeed  * Time.deltaTime; AnimRing(_ring1, _fx.ring,  _ring1Rot); }
                if (_ring2 != null) { _ring2Rot += _fx.ring2.rotationSpeed * Time.deltaTime; AnimRing(_ring2, _fx.ring2, _ring2Rot); }
                if (_disc  != null) AnimDisc(_disc, _fx.disc);
                if (_shock != null) AnimShockwave(_shock, _fx.shockwave);
                if (_beam  != null) AnimBeam(_beam, _fx.beam);
                if (_orbitNodes != null) AnimOrbit();
                if (_flash != null) AnimFlash(_flash, _fx.flash);
            }

            private void AnimShockwave(LineRenderer lr, ShockwaveModule m)
            {
                float p = Mathf.Clamp01(_t / m.duration);
                float curve = m.curve != null ? m.curve.Evaluate(p) : p;
                float r = Mathf.Lerp(m.startRadius, m.endRadius, curve) * _scale;
                DrawCircle(lr, _center, r, 56, 0f);
                Color c = m.color; c.a *= 1f - p;
                SetLineColor(lr, c);
            }

            private void AnimOrbit()
            {
                var m = _orbitCfg;
                float p = Mathf.Clamp01(_t / m.duration);
                float radius = m.contract ? Mathf.Lerp(m.radius, 0.1f, p) : m.radius;
                radius *= _scale;
                float baseAng = _t * m.angularSpeed * Mathf.Deg2Rad;
                float alpha = 1f - p;
                for (int i = 0; i < _orbitNodes.Length; i++)
                {
                    if (_orbitNodes[i] == null) continue;
                    float a = baseAng + (i / (float)_orbitNodes.Length) * Mathf.PI * 2f;
                    Vector3 pos = _center + new Vector3(Mathf.Cos(a) * radius,
                                                        m.height * _scale,
                                                        Mathf.Sin(a) * radius);
                    _orbitNodes[i].position = pos;
                    var mr = _orbitNodes[i].GetComponent<MeshRenderer>();
                    Color c = m.color; c.a *= alpha;
                    SetRendererColor(mr, c);
                }
            }

            private void AnimRing(LineRenderer lr, RingModule m, float rot)
            {
                float p = _looping ? Mathf.Repeat(_t, m.duration) / m.duration
                                   : Mathf.Clamp01(_t / m.duration);
                float curve = m.radiusCurve != null ? m.radiusCurve.Evaluate(p) : p;
                float radius, alpha = 1f;

                switch (m.mode)
                {
                    case RingMode.Expand:
                        radius = Mathf.Lerp(m.startRadius, m.endRadius, curve) * _scale;
                        if (m.fadeOut) alpha = 1f - p;
                        break;
                    case RingMode.FillUp:
                        radius = Mathf.Lerp(m.startRadius, m.endRadius, curve) * _scale;
                        alpha = 0.9f;
                        break;
                    default:
                        radius = m.endRadius * _scale;
                        alpha = 0.55f + 0.35f * Mathf.Sin(_t * 12f);
                        if (m.fadeOut && !_looping) alpha *= 1f - p;
                        break;
                }
                DrawCircle(lr, _center, radius, m.segments, rot);
                Color c = m.color; c.a *= alpha; SetLineColor(lr, c);
            }

            private void AnimDisc(Transform disc, DiscModule m)
            {
                float p = Mathf.Clamp01(_t / m.duration);
                float r = m.mode switch
                {
                    DiscMode.Expand => Mathf.Lerp(0.1f, m.radius, p),
                    DiscMode.FillUp => Mathf.Lerp(0.1f, m.radius, p),
                    _               => m.radius,
                };
                disc.localScale = Vector3.one * r * 2f * _scale;
                var mr = disc.GetComponent<MeshRenderer>();
                Color c = m.color;
                if (m.fadeOut && m.mode == DiscMode.Expand) c.a *= 1f - p;
                SetRendererColor(mr, c);
            }

            private void AnimBeam(LineRenderer lr, BeamModule m)
            {
                if (m.jagged) DrawBeam(lr, m); // recalcula o zig-zag p/ "tremer"
                if (m.fade)
                {
                    float p = Mathf.Clamp01(_t / m.duration);
                    Color c = m.color; c.a *= 1f - p; SetLineColor(lr, c);
                }
            }

            private void AnimFlash(Light l, FlashModule m)
            {
                float p = Mathf.Clamp01(_t / m.duration);
                float baseI = Mathf.Lerp(m.intensity, 0f, p);
                l.intensity = m.flicker ? baseI * (0.7f + 0.3f * Mathf.PerlinNoise(_t * 30f, 0f)) : baseI;
            }

            // ── Helpers ────────────────────────────────────────────────────
            private void DrawBeam(LineRenderer lr, BeamModule m)
            {
                Vector3 a = _center + Vector3.up * 1.0f;
                Vector3 b = a + _dir * (m.length * _scale);
                if (!m.jagged)
                {
                    lr.positionCount = 2;
                    lr.SetPosition(0, a); lr.SetPosition(1, b);
                    return;
                }
                int n = Mathf.Max(2, m.jagSegments);
                lr.positionCount = n;
                Vector3 perp = Vector3.Cross(_dir, Vector3.up).normalized;
                Vector3 up   = Vector3.Cross(perp, _dir).normalized;
                for (int i = 0; i < n; i++)
                {
                    float f = i / (float)(n - 1);
                    Vector3 p = Vector3.Lerp(a, b, f);
                    if (i != 0 && i != n - 1)
                    {
                        float amp = m.jagAmplitude * _scale;
                        p += perp * Random.Range(-amp, amp) + up * Random.Range(-amp, amp);
                    }
                    lr.SetPosition(i, p);
                }
            }

            private static void ConfigLine(LineRenderer lr, Material mat, float width)
            {
                lr.useWorldSpace = true;
                lr.material = mat;
                lr.widthMultiplier = width;
                lr.numCapVertices = 4;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }

            private static void DrawCircle(LineRenderer lr, Vector3 center, float radius, int segments, float rotDeg)
            {
                float y = center.y + 0.05f;
                float off = rotDeg * Mathf.Deg2Rad;
                for (int i = 0; i < segments; i++)
                {
                    float a = (i / (float)segments) * Mathf.PI * 2f + off;
                    lr.SetPosition(i, new Vector3(
                        center.x + Mathf.Cos(a) * radius, y,
                        center.z + Mathf.Sin(a) * radius));
                }
            }

            private static void SetLineColor(LineRenderer lr, Color c) { lr.startColor = c; lr.endColor = c; }

            private static readonly int ColorId = Shader.PropertyToID("_Color");
            private static readonly int TintId  = Shader.PropertyToID("_TintColor");
            private static MaterialPropertyBlock _mpb;
            private static void SetRendererColor(Renderer r, Color c)
            {
                _mpb ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, c);
                _mpb.SetColor(TintId, c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
