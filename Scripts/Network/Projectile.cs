using System.Collections.Generic;
using UnityEngine;
using Mirror;
using RPG.Character;

namespace RPG.Network
{
    /// <summary>
    /// Projétil server-authoritative com DOIS modos:
    ///   • Homing      → segue um alvo (usado por armas ranged: arco/cajado).
    ///   • Directional → viaja reto na direção dada (usado por SKILLSHOTS),
    ///                    com perfuração (pierce) e explosão opcional (AoE).
    ///
    /// O dano é pré-calculado no momento do disparo e aplicado via
    /// MonsterCombat.ServerTakeProjectileDamage (que NÃO reaplica DEF).
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class Projectile : NetworkBehaviour
    {
        private enum Mode { Homing, Directional }

        [Header("Configuração")]
        [SerializeField] private float maxTurnRate    = 360f;
        [SerializeField] private float impactDistance = 0.6f;
        [SerializeField] private float maxLifetime    = 6f;
        [SerializeField] private GameObject hitVfxPrefab;

        // ── SyncVars ───────────────────────────────────────────────────────
        [SyncVar] private uint    _shooterNetId;
        [SyncVar(hook = nameof(OnInitialDirectionChanged))] private Vector3 _initialDirection;
        [SyncVar] private int     _expectedTargetGeneration = -1;
        [SyncVar] private float   _syncSpeed;          // p/ mover no cliente sem NetworkTransform
        [SyncVar] private bool    _syncDirectional;    // modo replicado ao cliente

        [Header("Movimento visual no cliente")]
        [Tooltip("Se NÃO houver NetworkTransform no prefab, o cliente anima o projétil " +
                 "localmente pela direção+velocidade. Deixe ligado; é inofensivo mesmo com NetworkTransform.")]
        [SerializeField] private bool clientPredictMovement = true;

        private void OnInitialDirectionChanged(Vector3 _, Vector3 newDir)
        {
            if (newDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(newDir);
        }

        // ── Estado de servidor ─────────────────────────────────────────────
        private Mode             _mode = Mode.Homing;
        private float            _speed;
        private float            _damage;
        private bool             _crit;
        private float            _spawnTime;
        private NetworkBehaviour _serverTarget;
        private bool             _hitProcessed;

        // Directional (skillshot)
        private float            _maxRange;
        private int              _pierceLeft;
        private float            _aoeRadius;
        private bool             _isPhysical;
        private Vector3          _spawnPos;
        private HashSet<uint>    _hitNetIds;
        private static readonly Collider[] _projAoeBuffer = new Collider[32];
        private static int _targetableMask = -1;

        private float _clientSpawnTime;

        private static int TargetableMask
        {
            get
            {
                if (_targetableMask == -1) _targetableMask = LayerMask.GetMask("Targetable");
                return _targetableMask;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Init — HOMING (compatível com armas ranged existentes)
        // ══════════════════════════════════════════════════════════════════

        [Server]
        public void ServerInitialize(NetworkBehaviour target, uint shooterNetId,
                                     float speed, float damage, bool crit,
                                     int targetSpawnGeneration = -1)
        {
            _mode                     = Mode.Homing;
            _serverTarget             = target;
            _shooterNetId             = shooterNetId;
            _speed                    = Mathf.Max(1f, speed);
            _damage                   = Mathf.Max(0f, damage);
            _crit                     = crit;
            _spawnTime                = Time.time;
            _hitProcessed             = false;
            _expectedTargetGeneration = targetSpawnGeneration;

            if (target != null)
            {
                Vector3 dir = target.transform.position - transform.position;
                dir.y = 0f;
                _initialDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            }
            else _initialDirection = transform.forward;

            transform.rotation = Quaternion.LookRotation(_initialDirection);
        }

        [Server]
        public void ServerInitialize(NetworkBehaviour target, uint shooterNetId,
                                     float speed, float damage, bool crit)
            => ServerInitialize(target, shooterNetId, speed, damage, crit, -1);

        // ══════════════════════════════════════════════════════════════════
        // Init — DIRECTIONAL (skillshot)
        // ══════════════════════════════════════════════════════════════════

        [Server]
        public void ServerInitializeSkillshot(uint shooterNetId, Vector3 direction,
                                              float speed, float damage, bool crit,
                                              float maxRange, int pierce,
                                              float aoeRadius, bool isPhysical,
                                              float hitRadius = -1f)
        {
            _mode         = Mode.Directional;
            _syncDirectional = true;
            _shooterNetId = shooterNetId;
            _speed        = Mathf.Max(1f, speed);
            _syncSpeed    = _speed;
            _damage       = Mathf.Max(0f, damage);
            _crit         = crit;
            _maxRange     = Mathf.Max(1f, maxRange);
            _pierceLeft   = Mathf.Max(0, pierce);
            _aoeRadius    = Mathf.Max(0f, aoeRadius);
            _isPhysical   = isPhysical;
            if (hitRadius > 0f) impactDistance = hitRadius;
            _spawnTime    = Time.time;
            _spawnPos     = transform.position;
            _hitProcessed = false;
            _hitNetIds    = new HashSet<uint>();

            direction.y = 0f;
            _initialDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(_initialDirection);
        }

        private bool _hasNetworkTransform;

        public override void OnStartClient()
        {
            _clientSpawnTime = Time.time;
            if (_initialDirection.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(_initialDirection);

            // Há algum componente de sync de transform? (NetworkTransform, NT Reliable,
            // NT Unreliable, etc.) Detecta por nome para não depender da versão do Mirror.
            _hasNetworkTransform = false;
            foreach (var comp in GetComponents<NetworkBehaviour>())
            {
                if (comp == null || comp == this) continue;
                if (comp.GetType().Name.Contains("NetworkTransform"))
                {
                    _hasNetworkTransform = true;
                    break;
                }
            }

            // Se o prefab não tem nenhum renderer, cria um visual procedural simples
            // para o projétil nunca ficar "invisível".
            if (GetComponentInChildren<Renderer>() == null)
                BuildFallbackVisual();
        }

        private void BuildFallbackVisual()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "fallbackVisual";
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * 0.4f;

            var mr = sphere.GetComponent<MeshRenderer>();
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            var mat = new Material(sh) { color = new Color(1f, 0.6f, 0.2f, 1f) };
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var trail = sphere.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.startWidth = 0.35f;
            trail.endWidth = 0f;
            trail.material = mat;
            trail.numCapVertices = 4;
        }

        // ══════════════════════════════════════════════════════════════════
        // Update
        // ══════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (isServer)
            {
                if (_mode == Mode.Homing) ServerUpdateHoming();
                else                      ServerUpdateDirectional();
                // No Host, o objeto do servidor é o mesmo que o cliente vê:
                // o movimento acima já basta. Em servidor dedicado, segue normal.
                return;
            }

            // CLIENTE PURO: anima localmente se não houver NetworkTransform mexendo
            // na posição (fallback). Inofensivo quando há NT, pois a checagem abaixo
            // só move enquanto o NT ainda não assumiu.
            if (clientPredictMovement && _syncDirectional && _initialDirection.sqrMagnitude > 0.001f)
            {
                if (!_hasNetworkTransform)
                    transform.position += _initialDirection.normalized * (_syncSpeed * Time.deltaTime);
            }

            if (Time.time - _clientSpawnTime > maxLifetime + 0.5f)
                gameObject.SetActive(false);
        }

        // ── Homing (inalterado) ────────────────────────────────────────────

        [Server]
        private void ServerUpdateHoming()
        {
            if (Time.time - _spawnTime > maxLifetime) { NetworkServer.Destroy(gameObject); return; }

            Vector3 desiredDir = _initialDirection;

            if (IsTargetTrackable(_serverTarget))
            {
                Vector3 toTarget = _serverTarget.transform.position - transform.position;
                toTarget.y = 0f;
                float sqr = toTarget.sqrMagnitude;
                if (sqr > 0.001f)
                {
                    desiredDir = toTarget.normalized;
                    if (sqr <= impactDistance * impactDistance) { ApplyHomingImpact(); return; }
                }
            }
            else { _serverTarget = null; NetworkServer.Destroy(gameObject); return; }

            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
            {
                fwd.Normalize();
                float angle   = Vector3.Angle(fwd, desiredDir);
                float maxStep = maxTurnRate * Time.deltaTime;
                if (angle > maxStep)
                {
                    float sign = Mathf.Sign(Vector3.Cross(fwd, desiredDir).y);
                    desiredDir = Quaternion.AngleAxis(maxStep * sign, Vector3.up) * fwd;
                }
                transform.rotation = Quaternion.LookRotation(desiredDir);
            }
            transform.position += transform.forward * (_speed * Time.deltaTime);
        }

        [Server]
        private void ApplyHomingImpact()
        {
            if (_hitProcessed) return;
            _hitProcessed = true;

            if (_serverTarget is NetworkMonsterEntity monster)
            {
                if (_expectedTargetGeneration >= 0 && monster.SpawnGeneration != _expectedTargetGeneration)
                { NetworkServer.Destroy(gameObject); return; }

                if (!monster.IsDead)
                    monster.GetComponent<MonsterCombat>()
                        ?.ServerTakeProjectileDamage(_shooterNetId, _damage, _crit, transform.forward);
            }
            else if (_serverTarget is NetworkPlayer player && !player.Dead)
            {
                player.ServerApplyDamageWithFeedback(_damage);
            }

            RpcOnImpact(transform.position);
            NetworkServer.Destroy(gameObject);
        }

        // ── Directional / Skillshot ─────────────────────────────────────────

        [Server]
        private void ServerUpdateDirectional()
        {
            if (Time.time - _spawnTime > maxLifetime) { NetworkServer.Destroy(gameObject); return; }

            float traveled = Vector3.Distance(_spawnPos, transform.position);
            if (traveled >= _maxRange)
            {
                // Chegou ao fim do alcance sem acertar nada: dissipa (visual suave),
                // não explode como impacto.
                RpcOnFizzle(transform.position);
                NetworkServer.Destroy(gameObject);
                return;
            }

            transform.position += transform.forward * (_speed * Time.deltaTime);

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, impactDistance, _projAoeBuffer, TargetableMask);

            for (int i = 0; i < count; i++)
            {
                if (_projAoeBuffer[i] == null) continue;
                var monster = _projAoeBuffer[i].GetComponentInParent<NetworkMonsterEntity>();
                if (monster == null || monster.IsDead) continue;
                if (_hitNetIds.Contains(monster.netId)) continue;

                _hitNetIds.Add(monster.netId);
                monster.GetComponent<MonsterCombat>()
                    ?.ServerTakeProjectileDamage(_shooterNetId, _damage, _crit, transform.forward);

                // Explosão no ponto de impacto.
                if (_aoeRadius > 0.01f) SplashAt(transform.position);

                if (_pierceLeft <= 0)
                {
                    RpcOnImpact(transform.position);
                    NetworkServer.Destroy(gameObject);
                    return;
                }
                _pierceLeft--;
            }
        }

        [Server]
        private void SplashAt(Vector3 center)
        {
            int count = Physics.OverlapSphereNonAlloc(center, _aoeRadius, _projAoeBuffer, TargetableMask);
            for (int i = 0; i < count; i++)
            {
                if (_projAoeBuffer[i] == null) continue;
                var m = _projAoeBuffer[i].GetComponentInParent<NetworkMonsterEntity>();
                if (m == null || m.IsDead) continue;
                if (_hitNetIds.Contains(m.netId)) continue;
                _hitNetIds.Add(m.netId);
                m.GetComponent<MonsterCombat>()
                    ?.ServerTakeProjectileDamage(_shooterNetId, _damage * 0.6f, _crit, transform.forward);
            }
        }

        // ── Comum ────────────────────────────────────────────────────────

        [Server]
        private static bool IsTargetTrackable(NetworkBehaviour nb)
        {
            if (nb == null) return false;
            if (nb is ITargetable t && t.IsDead) return false;
            return true;
        }

        [ClientRpc]
        private void RpcOnImpact(Vector3 pos)
        {
            if (Application.isBatchMode) return;
            if (hitVfxPrefab != null)
            {
                var vfx = Instantiate(hitVfxPrefab, pos, Quaternion.identity);
                Destroy(vfx, 2f);
            }
            else
            {
                // Impacto procedural padrão (unificado no ProceduralFx).
                RPG.Feedback.ProceduralFx.Play(
                    RPG.Feedback.ProceduralPresets.Impact, pos, Vector3.forward, Mathf.Max(0.6f, _aoeRadius));
            }
        }

        [ClientRpc]
        private void RpcOnFizzle(Vector3 pos)
        {
            if (Application.isBatchMode) return;
            // Dissipação suave no fim do alcance (sem dano).
            RPG.Feedback.ProceduralFx.Play(RPG.Feedback.ProceduralPresets.Fizzle, pos);
        }
    }
}
