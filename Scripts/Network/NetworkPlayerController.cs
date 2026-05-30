using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Combat;

namespace RPG.Network
{

    [RequireComponent(typeof(NavMeshAgent))]
    public class NetworkPlayerController : NetworkBehaviour
    {
        [Header("Layers")]
        [SerializeField] private LayerMask terrainLayer;
        [SerializeField] private LayerMask targetableLayer;
        [SerializeField] private LayerMask itemLayer;
        [Tooltip("Layers que bloqueiam a câmera. Normalmente o mesmo do terrain.")]
        [SerializeField] private LayerMask cameraOcclusionLayer;

        [Header("Câmera")]
        [SerializeField] private float orbitSensitivity = 3f;
        [SerializeField] private float zoomSensitivity  = 5f;
        [SerializeField] private float cameraSmoothTime = 0.05f;
        [SerializeField] private float cameraHeight     = 1.5f;

        [Header("Movimento (Click & Hold)")]
        [SerializeField] private float updateTargetInterval = 0.12f;
        [SerializeField] private float cmdMoveInterval = 0.18f;
        [SerializeField] private float redirectThreshold = 0.4f;

        [Header("Indicador de Movimento")]
        [SerializeField] private GameObject moveIndicatorPrefab;

        [Header("Debug")]
        [SerializeField] private bool debugMovement = false;

        // ── Componentes ────────────────────────────────────────────────────
        private NavMeshAgent           _agent;
        private PlayerEntity           _playerEntity;
        private SkillSystem            _skillSystem;
        private BasicAttackSystem      _basicAttack;
        private NetworkIdentity        _identity;
        private Camera                 _cam;
        private RPG.NPC.NPCInteractor  _npcInteractor;

        // ── Estado de movimento / combate ──────────────────────────────────
        private bool    _holdMoving;
        private bool    _attackHeld;
        private Vector3 _lastSentDestination;
        private float   _lastTargetUpdateTime;
        private float   _lastCmdMoveTime;

        // ── Câmera ─────────────────────────────────────────────────────────
        private float   _yaw         = 45f;
        private float   _pitch       = 45f;
        private float   _distance    = 12f;
        private bool    _orbiting;
        private Vector3 _camVelocity = Vector3.zero;

        private const float PITCH_MIN      = 10f;
        private const float PITCH_MAX      = 80f;
        private const float DIST_MIN       = 3f;
        private const float DIST_MAX       = 30f;
        private const float MAX_MOVE_DIST  = 120f;
        private const float CAM_SKIN_WIDTH = 0.3f;

        private const float AGENT_ACCELERATION   = 60f;
        private const float AGENT_ANGULAR_SPEED  = 720f;
        private const float AGENT_STOPPING_DIST  = 0.15f;
        private const float AGENT_MIN_SPEED     = 3f;
        private const float AGENT_MAX_SPEED     = 7f;

        private float _lastSecurityWarnTime = -999f;
        private const float SECURITY_WARN_INTERVAL = 2f;

        // --- Anti-SpeedHack ---
        private Vector3 _serverLastPosition;
        private float   _serverLastMoveTime;
        private const float SPEED_HACK_TOLERANCE = 1.6f;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent       = GetComponent<NavMeshAgent>();
            _basicAttack = GetComponent<BasicAttackSystem>();
            _identity    = GetComponent<NetworkIdentity>();
        }

        public override void OnStartServer()
        {
            _serverLastPosition = transform.position;
            _serverLastMoveTime = Time.time;
        }

        private void OnEnable()
        {
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnDisable()
        {
            _orbiting        = false;
            _holdMoving      = false;
            _attackHeld      = false;
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public override void OnStartLocalPlayer()
        {
            _playerEntity  = GetComponent<PlayerEntity>();
            _skillSystem   = GetComponent<SkillSystem>();
            _basicAttack   = GetComponent<BasicAttackSystem>();
            _npcInteractor = GetComponent<RPG.NPC.NPCInteractor>();
            _cam           = Camera.main;

            if (_cam == null)
                Debug.LogWarning("[NetworkPlayerController] Camera.main não encontrada.");

            if (_agent != null)
            {
                _agent.acceleration     = AGENT_ACCELERATION;
                _agent.angularSpeed     = AGENT_ANGULAR_SPEED;
                _agent.autoBraking      = false;
                _agent.stoppingDistance = AGENT_STOPPING_DIST;

                if (_playerEntity != null && _playerEntity.Stats != null)
                    _agent.speed = Mathf.Clamp(_playerEntity.Stats.MoveSpeed, AGENT_MIN_SPEED, AGENT_MAX_SPEED);
            }

            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;

            UIManager.Instance?.BindLocalPlayer(_playerEntity);
        }

        private void Update()
        {
            if (!isLocalPlayer) return;
            if (_cam == null) _cam = Camera.main;

            HandleMouseInput();
            HandleSkillInput();
            HandleCameraOrbit();
            HandleUIInput();
        }

        private void LateUpdate()
        {
            if (!isLocalPlayer) return;
            UpdateCameraPosition();
        }

        // ══════════════════════════════════════════════════════════════════
        // Mouse — Diablo-style: segurar sobre inimigo = atacar; chão = mover
        // ══════════════════════════════════════════════════════════════════

        private void HandleMouseInput()
        {
            if (_cam == null) return;
            bool overUI = UIInputUtils.IsPointerOverUI();

            // ── Down ───────────────────────────────────────────────────────
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

                if (TryPickupItem(ray))     { ResetHold(); return; }
                if (TryHandleNpcClick(ray)) { ResetHold(); return; }

                if (TryGetMonsterUnderCursor(ray, out var monster))
                {
                    _attackHeld = true;
                    _holdMoving = false;
                    _basicAttack?.HoldAttack(monster);
                    return;
                }

                // Chão → mover
                _basicAttack?.ReleaseAttack();
                _attackHeld = false;
                if (TryMoveToGround(ray, showIndicator: true))
                {
                    _holdMoving           = true;
                    _lastTargetUpdateTime = Time.time;
                }
            }

            // ── Held ───────────────────────────────────────────────────────
            if (Input.GetMouseButton(0) && !overUI)
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

                if (_attackHeld)
                {
                    // Re-mira para o inimigo sob o cursor; se sair pro chão,
                    // mantém o alvo atual (BasicAttackSystem persegue).
                    if (TryGetMonsterUnderCursor(ray, out var monster))
                        _basicAttack?.HoldAttack(monster);
                }
                else if (_holdMoving)
                {
                    if (Time.time - _lastTargetUpdateTime >= updateTargetInterval)
                    {
                        _lastTargetUpdateTime = Time.time;
                        TryMoveToGround(ray, showIndicator: false);
                    }
                }
            }

            // ── Up ─────────────────────────────────────────────────────────
            if (Input.GetMouseButtonUp(0))
            {
                if (_attackHeld) _basicAttack?.ReleaseAttack();
                _attackHeld = false;
                _holdMoving = false;
            }
        }

        private void ResetHold()
        {
            _holdMoving = false;
            if (_attackHeld) _basicAttack?.ReleaseAttack();
            _attackHeld = false;
        }

        private bool TryGetMonsterUnderCursor(Ray ray, out NetworkMonsterEntity monster)
        {
            monster = null;
            if (targetableLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, targetableLayer)) return false;
            monster = hit.collider.GetComponentInParent<NetworkMonsterEntity>();
            return monster != null && !monster.IsDead;
        }

        private bool TryHandleNpcClick(Ray ray)
        {
            if (targetableLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, targetableLayer)) return false;

            var npc = hit.collider.GetComponentInParent<RPG.NPC.NetworkNPC>();
            if (npc == null) return false;

            _basicAttack?.ReleaseAttack();
            _npcInteractor?.TryInteract(npc);
            return true;
        }

        private bool TryPickupItem(Ray ray)
        {
            if (itemLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, itemLayer)) return false;

            var worldItem = hit.collider.GetComponentInParent<WorldItem>();
            if (worldItem == null) return false;

            if (_identity != null) worldItem.CmdPickUp(_identity.netId);
            return true;
        }

        private bool TryMoveToGround(Ray ray, bool showIndicator)
        {
            int moveLayerMask = terrainLayer != 0
                ? (int)terrainLayer
                : ~(1 << LayerMask.NameToLayer("Targetable"));

            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, moveLayerMask)) return false;

            if (showIndicator)
            {
                _playerEntity?.ClearTarget();
                UIManager.Instance?.ClearTargetPanel();
            }

            Vector3 dest = hit.point;
            if (NavMesh.SamplePosition(dest, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
                dest = navHit.position;

            if (Vector3.Distance(_lastSentDestination, dest) >= redirectThreshold)
            {
                if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(dest);
                _lastSentDestination = dest;
            }

            if (Time.time - _lastCmdMoveTime >= cmdMoveInterval)
            {
                _lastCmdMoveTime = Time.time;
                CmdMoveTo(dest);
            }

            if (showIndicator) SpawnMoveIndicator(hit.point);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // Teclado — skills disparam na hora, miram pelo cursor
        // ══════════════════════════════════════════════════════════════════

        private void HandleSkillInput()
        {
            if (_skillSystem == null) return;
            if (_playerEntity != null && _playerEntity.IsDead) return;
            if (UIInputUtils.IsTypingInInputField()) return;
            if (DialogUI.Instance != null && DialogUI.Instance.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Q)) _skillSystem.TryUseSkill(0);
            if (Input.GetKeyDown(KeyCode.W)) _skillSystem.TryUseSkill(1);
            if (Input.GetKeyDown(KeyCode.E)) _skillSystem.TryUseSkill(2);
            if (Input.GetKeyDown(KeyCode.R)) _skillSystem.TryUseSkill(3);

            // Soltar a tecla encerra um laser sustentado (beam que segue o cursor).
            if (Input.GetKeyUp(KeyCode.Q)) _skillSystem.NotifySkillKeyReleased(0);
            if (Input.GetKeyUp(KeyCode.W)) _skillSystem.NotifySkillKeyReleased(1);
            if (Input.GetKeyUp(KeyCode.E)) _skillSystem.NotifySkillKeyReleased(2);
            if (Input.GetKeyUp(KeyCode.R)) _skillSystem.NotifySkillKeyReleased(3);

            if (Input.GetKeyDown(KeyCode.C)) AttributeWindowUI.Instance?.Toggle();
        }

        private void HandleUIInput()
        {
            if (UIInputUtils.IsTypingInInputField()) return;

            if (Input.GetKeyDown(KeyCode.I)) { EnsureCursorVisible(); InventoryUI.Instance?.Toggle(); }
            if (Input.GetKeyDown(KeyCode.P)) { EnsureCursorVisible(); PowerGemUI.Instance?.Toggle(); }
        }

        private void EnsureCursorVisible()
        {
            if (!_orbiting)
            {
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Câmera (inalterada)
        // ══════════════════════════════════════════════════════════════════

        private void HandleCameraOrbit()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _orbiting        = true;
                Cursor.visible   = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            if (Input.GetMouseButtonUp(1))
            {
                _orbiting        = false;
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (_orbiting)
            {
                _yaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
                _pitch  = Mathf.Clamp(_pitch, PITCH_MIN, PITCH_MAX);
                if (_yaw > 360f)  _yaw -= 360f;
                if (_yaw < -360f) _yaw += 360f;
            }

            _distance -= Input.GetAxis("Mouse ScrollWheel") * zoomSensitivity;
            _distance  = Mathf.Clamp(_distance, DIST_MIN, DIST_MAX);
        }

        private void UpdateCameraPosition()
        {
            if (_cam == null) return;

            Quaternion rot   = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3    pivot = transform.position + Vector3.up * cameraHeight;
            Vector3    dir   = rot * new Vector3(0f, 0f, -1f);

            float effectiveDistance = _distance;
            int   occlusionMask = cameraOcclusionLayer != 0 ? (int)cameraOcclusionLayer : (int)terrainLayer;

            if (occlusionMask != 0
                && Physics.SphereCast(pivot, CAM_SKIN_WIDTH, dir, out RaycastHit camHit, _distance, occlusionMask))
            {
                effectiveDistance = Mathf.Max(DIST_MIN, camHit.distance - CAM_SKIN_WIDTH);
            }

            Vector3 target = pivot + dir * effectiveDistance;
            if (target.y < transform.position.y + 0.5f) target.y = transform.position.y + 0.5f;

            _cam.transform.position = Vector3.SmoothDamp(
                _cam.transform.position, target, ref _camVelocity, cameraSmoothTime);
            _cam.transform.LookAt(pivot);
        }

        // ══════════════════════════════════════════════════════════════════
        // Commands (anti-cheat inalterado)
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdMoveTo(Vector3 destination)
        {
            if (float.IsNaN(destination.x) || float.IsInfinity(destination.x)
                || float.IsNaN(destination.y) || float.IsInfinity(destination.y)
                || float.IsNaN(destination.z) || float.IsInfinity(destination.z))
            {
                Debug.LogWarning("[Security] CmdMoveTo com coordenadas inválidas.");
                return;
            }

            var netPlayer = GetComponent<NetworkPlayer>();
            if (netPlayer == null || netPlayer.Dead) return;

            float timePassed = Time.time - _serverLastMoveTime;
            if (timePassed > 0.05f)
            {
                float actualDistMoved = Vector3.Distance(_serverLastPosition, transform.position);
                float maxSpeed = (_playerEntity != null && _playerEntity.Stats != null)
                    ? _playerEntity.Stats.MoveSpeed : AGENT_MAX_SPEED;

                float maxAllowedDist = (maxSpeed * timePassed * SPEED_HACK_TOLERANCE) + 0.5f;

                if (actualDistMoved > maxAllowedDist)
                {
                    if (Time.time - _lastSecurityWarnTime >= SECURITY_WARN_INTERVAL)
                    {
                        _lastSecurityWarnTime = Time.time;
                        Debug.LogWarning($"[Security] Movimento suspeito: {netPlayer.CharacterName} | Real: {actualDistMoved:0.1} | Max: {maxAllowedDist:0.1} | T: {timePassed:0.00}s");
                    }
                    _agent.Warp(_serverLastPosition);
                    return;
                }
            }

            Vector3 finalDest = destination;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                finalDest = hit.position;
            }
            else
            {
                if (debugMovement)
                    Debug.LogWarning($"[Server] CmdMoveTo: destino fora do NavMesh para {netPlayer.CharacterName}");
                return;
            }

            float totalDist = Vector3.Distance(transform.position, finalDest);
            if (totalDist > MAX_MOVE_DIST)
            {
                if (Time.time - _lastSecurityWarnTime >= SECURITY_WARN_INTERVAL)
                {
                    _lastSecurityWarnTime = Time.time;
                    Debug.LogWarning($"[Security] CmdMoveTo muito longo: dist={totalDist:0.0} | {netPlayer.CharacterName}");
                }
                return;
            }

            _serverLastPosition = transform.position;
            _serverLastMoveTime = Time.time;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(finalDest);
                _agent.speed = (_playerEntity != null && _playerEntity.Stats != null)
                    ? Mathf.Clamp(_playerEntity.Stats.MoveSpeed, AGENT_MIN_SPEED, AGENT_MAX_SPEED)
                    : AGENT_MAX_SPEED;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public void SetEnabled(bool value)
        {
            enabled = value;
            if (!value)
            {
                _holdMoving = false;
                _attackHeld = false;
                _basicAttack?.ReleaseAttack();
                _skillSystem?.CancelCast();

                _orbiting        = false;
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        public void ServerSyncSafetyPosition(Vector3 pos)
        {
            _serverLastPosition = pos;
            _serverLastMoveTime = Time.time;
        }

        private void SpawnMoveIndicator(Vector3 pos)
        {
            if (moveIndicatorPrefab == null) return;
            var go = Instantiate(moveIndicatorPrefab,
                pos + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
            Destroy(go, 0.8f);
        }
    }
}
