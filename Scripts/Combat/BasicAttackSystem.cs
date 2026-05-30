using UnityEngine;
using UnityEngine.AI;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Network;
using RPG.Data;

namespace RPG.Combat
{
    /// <summary>
    /// Ataque básico ARPG-style: SEGURE o botão sobre um inimigo para atacar.
    ///
    /// MUDANÇAS vs. versão antiga:
    ///   • Sem duplo-clique. O controller chama HoldAttack(monster) a cada frame
    ///     enquanto o botão estiver pressionado e o cursor sobre um inimigo vivo,
    ///     e ReleaseAttack() ao soltar.
    ///   • Persegue até o range da arma e ataca na cadência do ASPD.
    ///   • Não depende mais de "alvo selecionado" travado nem do walk-to-skill.
    /// </summary>
    [RequireComponent(typeof(PlayerEntity))]
    [RequireComponent(typeof(NetworkIdentity))]
    public class BasicAttackSystem : NetworkBehaviour
    {
        [Header("Perseguição")]
        [Tooltip("Frequência de envio de CmdMoveTo durante a perseguição (s).")]
        [SerializeField] private float moveCommandInterval = 0.18f;
        [SerializeField] private float chaseRedirectThreshold = 0.5f;

        private const float DEST_FRACTION      = 0.80f;
        private const float RANGE_CHECK_MARGIN = 1.05f;
        private const float CHASE_STOP_DIST    = 0.15f;
        private const float IDLE_STOP_DIST     = 0.5f;
        private const float MIN_INTERVAL       = 0.2f;
        private const float MAX_INTERVAL       = 3f;
        private const float ROTATION_SPEED     = 12f;
        private const float DEFAULT_INTERVAL   = 1.2f;

        private PlayerEntity            _player;
        private NavMeshAgent            _agent;
        private Animator                _animator;
        private NetworkPlayerController _controller;
        private NetworkIdentity         _identity;
        private NetworkInventory        _inventory;

        private NetworkMonsterEntity _attackTarget;
        private bool                 _attacking;
        private float                _attackTimer;
        private float                _lastMoveCmd;
        private Vector3              _lastChaseDestination = Vector3.positiveInfinity;

        private WeaponAttackProfile _currentProfile;
        private float _cachedAttackInterval = DEFAULT_INTERVAL;
        private bool  _attackIntervalDirty  = true;

        private bool _subscribedPlayer;
        private bool _subscribedInventory;

        public bool  IsAutoAttacking => _attacking;
        public float AttackRange     => _currentProfile?.Range ?? 2.5f;
        public WeaponAttackProfile CurrentProfile => _currentProfile;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _player     = GetComponent<PlayerEntity>();
            _agent      = GetComponent<NavMeshAgent>();
            _animator   = GetComponentInChildren<Animator>();
            _controller = GetComponent<NetworkPlayerController>();
            _identity   = GetComponent<NetworkIdentity>();
            _inventory  = GetComponent<NetworkInventory>();

            _currentProfile = WeaponAttackProfile.Default(WeaponType.Unarmed);
        }

        public override void OnStartLocalPlayer()
        {
            SubscribePlayer();
            SubscribeInventory();
            RefreshWeaponProfile();
            _attackIntervalDirty = true;
        }

        public override void OnStopClient()
        {
            UnsubscribePlayer();
            UnsubscribeInventory();
            StopAttacking();
        }

        private void OnDestroy()
        {
            UnsubscribePlayer();
            UnsubscribeInventory();
        }

        private void SubscribePlayer()
        {
            if (_subscribedPlayer || _player == null) return;
            _player.OnDeathChanged += OnPlayerDeathChanged;
            _player.OnStatsChanged += OnPlayerStatsChanged;
            _subscribedPlayer = true;
        }

        private void UnsubscribePlayer()
        {
            if (!_subscribedPlayer || _player == null) return;
            _player.OnDeathChanged -= OnPlayerDeathChanged;
            _player.OnStatsChanged -= OnPlayerStatsChanged;
            _subscribedPlayer = false;
        }

        private void SubscribeInventory()
        {
            if (_subscribedInventory || _inventory == null) return;
            _inventory.OnEquipmentChanged += OnEquipmentChanged;
            _subscribedInventory = true;
        }

        private void UnsubscribeInventory()
        {
            if (!_subscribedInventory || _inventory == null) return;
            _inventory.OnEquipmentChanged -= OnEquipmentChanged;
            _subscribedInventory = false;
        }

        private void OnPlayerDeathChanged(bool isDead) { if (isDead) StopAttacking(); }
        private void OnPlayerStatsChanged()            { _attackIntervalDirty = true; }

        private void OnEquipmentChanged()
        {
            RefreshWeaponProfile();
            _attackIntervalDirty = true;
        }

        private void RefreshWeaponProfile()
        {
            if (_inventory == null)
            {
                _currentProfile = WeaponAttackProfile.Default(WeaponType.Unarmed);
                return;
            }

            string weaponId = _inventory.GetEquipped(EquipmentSlot.Weapon);
            if (string.IsNullOrEmpty(weaponId))
            {
                _currentProfile = WeaponAttackProfile.Default(WeaponType.Unarmed);
                return;
            }

            var item = ItemDatabase.Instance?.GetItem(weaponId);
            _currentProfile = (item != null && item.IsWeapon)
                ? item.GetEffectiveAttackProfile()
                : WeaponAttackProfile.Default(WeaponType.Unarmed);
        }

        private void Update()
        {
            if (!isLocalPlayer) return;
            if (_player == null || !_player.IsInitialized) return;

            if (_player.IsDead) { if (_attacking) StopAttacking(); return; }

            if (_attacking) UpdateAttack();
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública (chamada pelo controller enquanto o botão está pressionado)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mantém o ataque sobre 'monster'. Chame todo frame enquanto o botão
        /// de ataque estiver pressionado e o cursor sobre um inimigo vivo.
        /// </summary>
        public void HoldAttack(NetworkMonsterEntity monster)
        {
            if (IsGone(monster)) return;
            if (!_attacking || _attackTarget != monster)
                BeginAttack(monster);
        }

        /// <summary>Solte o botão de ataque.</summary>
        public void ReleaseAttack() => StopAttacking();

        /// <summary>Para o ataque sem mexer no agente (cancelamento suave).</summary>
        public void CancelAutoAttackSoft()
        {
            _attacking            = false;
            _attackTarget         = null;
            _attackTimer          = 0f;
            _lastChaseDestination = Vector3.positiveInfinity;
        }

        /// <summary>Para o ataque e zera o movimento.</summary>
        public void CancelAutoAttack() => StopAttacking();

        private void StopAttacking()
        {
            if (!_attacking) return;
            CancelAutoAttackSoft();
            StopAgentMovement();
        }

        private void BeginAttack(NetworkMonsterEntity monster)
        {
            RefreshWeaponProfile();
            _attackIntervalDirty  = true;
            _attackTarget         = monster;
            _attacking            = true;
            _attackTimer          = GetAttackInterval(); // ataca quase imediato
            _lastChaseDestination = Vector3.positiveInfinity;

            _player.SetTarget(monster);
            UIManager.Instance?.UpdateTargetPanel(monster);
        }

        // ══════════════════════════════════════════════════════════════════
        // Loop
        // ══════════════════════════════════════════════════════════════════

        private void UpdateAttack()
        {
            if (IsGone(_attackTarget))
            {
                StopAttacking();
                _player.ClearTarget();
                UIManager.Instance?.ClearTargetPanel();
                return;
            }

            float dist           = Vector3.Distance(transform.position, _attackTarget.Position);
            float range          = _currentProfile.Range;
            float effectiveRange = range * RANGE_CHECK_MARGIN;

            if (dist > effectiveRange) ChaseTarget(range);
            else                       AttackInRange();
        }

        private void AttackInRange()
        {
            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
            {
                _agent.ResetPath();
                _agent.stoppingDistance = IDLE_STOP_DIST;
                _lastChaseDestination   = Vector3.positiveInfinity;
            }

            _attackTimer += Time.deltaTime;
            if (_attackTimer >= GetAttackInterval())
            {
                _attackTimer = 0f;
                ExecuteBasicAttack();
            }

            RotateTowardsTarget();
        }

        private void RotateTowardsTarget()
        {
            if (_attackTarget == null) return;
            Vector3 dir = _attackTarget.Position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir),
                    ROTATION_SPEED * Time.deltaTime);
        }

        private void ChaseTarget(float weaponRange)
        {
            if (_attackTarget == null || _agent == null || !_agent.isOnNavMesh) return;

            Vector3 destination = CalculateChaseDestination(_attackTarget.Position, weaponRange);

            if (Vector3.Distance(destination, _lastChaseDestination) >= chaseRedirectThreshold)
            {
                _agent.stoppingDistance = CHASE_STOP_DIST;
                _agent.SetDestination(destination);
                _lastChaseDestination = destination;
            }

            if (Time.time - _lastMoveCmd >= moveCommandInterval)
            {
                _lastMoveCmd = Time.time;
                _controller?.CmdMoveTo(destination);
            }
        }

        private Vector3 CalculateChaseDestination(Vector3 targetPos, float weaponRange)
        {
            Vector3 toTarget = targetPos - transform.position;
            float   dist     = toTarget.magnitude;
            float   safe     = weaponRange * DEST_FRACTION;

            if (dist <= safe * 0.95f) return transform.position;

            Vector3 destination = targetPos - toTarget.normalized * safe;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;
            return destination;
        }

        private void ExecuteBasicAttack()
        {
            if (IsGone(_attackTarget) || _identity == null) return;

            if (_currentProfile.ManaCost > 0f && _player.CurrentMP < _currentProfile.ManaCost)
            {
                UIManager.Instance?.ShowMessage("<color=red>MP insuficiente!</color>");
                StopAttacking();
                return;
            }

            string trigger = !string.IsNullOrEmpty(_currentProfile.AnimTrigger)
                ? _currentProfile.AnimTrigger : "Attack";
            CmdPlayAttackAnimation(trigger);

            _attackTarget.CmdBasicAttack(_identity.netId, _currentProfile.Range);
        }

        [Command] private void CmdPlayAttackAnimation(string t) => RpcPlayAttackAnimation(t);
        [ClientRpc] private void RpcPlayAttackAnimation(string t) { if (_animator != null) _animator.SetTrigger(t); }

        // ══════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════

        private float GetAttackInterval()
        {
            if (!_attackIntervalDirty) return _cachedAttackInterval;
            float baseInterval = DEFAULT_INTERVAL;
            if (_player != null && _player.IsInitialized && _player.Stats != null)
                baseInterval = 1f / Mathf.Max(0.1f, _player.Stats.ASPD);
            float mod = _currentProfile?.AttackIntervalMultiplier ?? 1f;
            _cachedAttackInterval = Mathf.Clamp(baseInterval * mod, MIN_INTERVAL, MAX_INTERVAL);
            _attackIntervalDirty  = false;
            return _cachedAttackInterval;
        }

        private void StopAgentMovement()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.ResetPath();
            _agent.stoppingDistance = IDLE_STOP_DIST;
            _lastChaseDestination   = Vector3.positiveInfinity;
        }

        private static bool IsGone(NetworkMonsterEntity t) => t == null || t.IsDead;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            float r = _currentProfile?.Range ?? 2.5f;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, r);
        }
#endif
    }
}
