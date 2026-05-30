using System;
using System.Collections;
using UnityEngine;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Network;
using RPG.Data;

namespace RPG.Combat
{
    /// <summary>
    /// Sistema de skills ARPG-style (Diablo / PoE).
    ///
    /// MUDANÇAS vs. versão antiga:
    ///   • SEM target-lock obrigatório. A mira vem do CURSOR no momento do uso.
    ///   • SEM walk-to-range. A skill dispara de onde o player está.
    ///   • Cast instantâneo é o padrão. CastTime > 0 só p/ skills pesadas e,
    ///     por padrão, NÃO é cancelado por movimento (configurável na skill).
    ///   • Comando vai para o PLAYER (CmdUseSkill), não para o monstro.
    ///
    /// Modos de mira (resolvidos aqui no cliente):
    ///   SelfCast / AroundSelf → sem mira.
    ///   TargetEnemy           → raycast do cursor; precisa acertar um inimigo.
    ///   Skillshot             → direção = (ponto do cursor no chão − player).
    ///   GroundTarget          → ponto do cursor no chão (clamped ao Range).
    /// </summary>
    [RequireComponent(typeof(PlayerEntity))]
    public class SkillSystem : NetworkBehaviour
    {
        public const int MAX_SKILLS = 4;

        [Header("Mira")]
        [Tooltip("Layers que contam como 'chão' para mira de skillshot/ground target.")]
        [SerializeField] private LayerMask groundMask;
        [Tooltip("Layers de inimigos/alvos selecionáveis (mesma dos monstros).")]
        [SerializeField] private LayerMask targetableMask;

        [Header("Debug")]
        [Tooltip("Loga no Console o motivo de cada skill não disparar. Deixe ligado até ajustar tudo.")]
        [SerializeField] private bool debugLogs = true;

        private const float INSTANT_CAST_EPS = 0.05f;
        private const float RAYCAST_DIST      = 300f;

        // ── Input buffer (mantém a fluidez quando se aperta durante um cast) ─
        private const float INPUT_BUFFER_WINDOW = 0.4f;
        private int   _bufferedSkillIndex = -1;
        private float _bufferTimestamp    = -1f;

        // ── Componentes ────────────────────────────────────────────────────
        private PlayerEntity     _player;
        private Animator         _animator;
        private NetworkInventory _inventory;
        private RPG.Network.NetworkPlayer    _netPlayer;

        // ── Cooldown visual ────────────────────────────────────────────────
        private readonly float[] _uiCooldownTimers = new float[MAX_SKILLS];

        // ── Cast ───────────────────────────────────────────────────────────
        private Coroutine _castCoroutine;
        private bool      _isCasting;

        // ── Canal de beam (laser que segue o cursor) ───────────────────────
        private bool      _beamChanneling;
        private int       _beamSkillIndex = -1;
        private float     _beamEndsAt;
        private Vector3   _lastBeamDir;
        private const float BEAM_AIM_SEND_INTERVAL = 0.05f; // ~20 Hz
        private float     _beamSendTimer;

        private bool _subscribedPlayer;
        private bool _subscribedInventory;

        // ── Eventos p/ UI ──────────────────────────────────────────────────
        public event Action<int, float>    OnCooldownStarted;
        public event Action<int>           OnSkillFired;
        public event Action                OnSkillBarNeedsRefresh;
        public event Action<string, float> OnCastStarted;
        public event Action<float>         OnCastProgress;
        public event Action                OnCastFinished;

        public bool IsCasting => _isCasting;
        public int  SkillCount => MAX_SKILLS;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _player    = GetComponent<PlayerEntity>();
            _animator  = GetComponentInChildren<Animator>();
            _inventory = GetComponent<NetworkInventory>();
            _netPlayer = GetComponent<RPG.Network.NetworkPlayer>();
        }

        public override void OnStartLocalPlayer()
        {
            SubscribeInventory();
            SubscribePlayer();
        }

        public override void OnStopClient()
        {
            UnsubscribeInventory();
            UnsubscribePlayer();
            CancelCast();
        }

        private void OnDestroy()
        {
            UnsubscribeInventory();
            UnsubscribePlayer();
            CancelCast();
        }

        private void SubscribeInventory()
        {
            if (_subscribedInventory || _inventory == null) return;
            _inventory.OnGemLoadoutChanged += OnGemLoadoutChanged;
            _subscribedInventory = true;
        }

        private void UnsubscribeInventory()
        {
            if (!_subscribedInventory || _inventory == null) return;
            _inventory.OnGemLoadoutChanged -= OnGemLoadoutChanged;
            _subscribedInventory = false;
        }

        private void SubscribePlayer()
        {
            if (_subscribedPlayer || _player == null) return;
            _player.OnDeathChanged += OnPlayerDeathChanged;
            _subscribedPlayer = true;
        }

        private void UnsubscribePlayer()
        {
            if (!_subscribedPlayer || _player == null) return;
            _player.OnDeathChanged -= OnPlayerDeathChanged;
            _subscribedPlayer = false;
        }

        private void OnGemLoadoutChanged()
        {
            if (isLocalPlayer) OnSkillBarNeedsRefresh?.Invoke();
        }

        private void OnPlayerDeathChanged(bool isDead)
        {
            if (isDead) CancelCast();
        }

        private void Update()
        {
            if (!isLocalPlayer) return;

            for (int i = 0; i < MAX_SKILLS; i++)
                if (_uiCooldownTimers[i] > 0f)
                    _uiCooldownTimers[i] -= Time.deltaTime;

            // Buffer de input: se apertou durante um cast, dispara logo que liberar.
            if (_bufferedSkillIndex != -1)
            {
                if (Time.time - _bufferTimestamp > INPUT_BUFFER_WINDOW)
                {
                    _bufferedSkillIndex = -1;
                }
                else if (!_isCasting)
                {
                    int idx = _bufferedSkillIndex;
                    _bufferedSkillIndex = -1;
                    TryUseSkill(idx);
                }
            }

            if (_isCasting && _player.IsDead) CancelCast();

            TickBeamChannel();
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public SkillData GetSkill(int index)
        {
            if (index < 0 || index >= MAX_SKILLS) return null;
            return _inventory?.GetEquippedSkill(index);
        }

        public float GetUICooldown(int i)
            => (i >= 0 && i < MAX_SKILLS) ? Mathf.Max(0f, _uiCooldownTimers[i]) : 0f;

        public bool IsOnUICooldown(int i) => GetUICooldown(i) > 0f;

        /// <summary>
        /// Ponto de entrada chamado pelo controller ao apertar Q/W/E/R.
        /// Resolve a mira AGORA (cursor) e dispara. Nada de selecionar antes.
        /// </summary>
        public void TryUseSkill(int index)
        {
            if (!isLocalPlayer) return;
            if (index < 0 || index >= MAX_SKILLS) return;
            if (_player == null) { Log("BLOQUEADO: PlayerEntity ausente."); return; }
            if (!_player.IsInitialized) { Log("BLOQUEADO: player ainda não inicializado (IsInitialized=false)."); return; }
            if (_player.IsDead) { Log("BLOQUEADO: player morto."); return; }

            // Durante um cast, enfileira (input buffering).
            if (_isCasting)
            {
                _bufferedSkillIndex = index;
                _bufferTimestamp    = Time.time;
                return;
            }

            var skill = GetSkill(index);
            if (skill == null)
            {
                Log($"BLOQUEADO: nenhum SkillData no slot {index} (GetEquippedSkill retornou null).");
                UIManager.Instance?.ShowMessage($"Nenhuma Joia equipada no slot {SlotName(index)}!");
                return;
            }

            Log($"Tentando '{skill.Name}' slot {index} | AimMode={skill.AimMode} | MP={_player.CurrentMP}/{skill.ManaCost}");

            // Predição cliente: MP e cooldown (servidor revalida).
            if (_player.CurrentMP < skill.ManaCost)
            {
                Log("BLOQUEADO: MP insuficiente (cliente).");
                UIManager.Instance?.ShowMessage("<color=red>MP insuficiente!</color>");
                return;
            }
            if (IsOnUICooldown(index))
            {
                Log($"BLOQUEADO: em cooldown ({GetUICooldown(index):0.0}s).");
                UIManager.Instance?.ShowMessage($"{skill.Name}: aguarde {GetUICooldown(index):0.0}s");
                return;
            }

            // Resolve a mira conforme o modo.
            if (!ResolveAim(skill, index, out SkillCastInfo info, out string aimError))
            {
                Log($"BLOQUEADO: mira falhou ({aimError}).");
                if (!string.IsNullOrEmpty(aimError))
                    UIManager.Instance?.ShowMessage(aimError);
                return;
            }

            StartCastAndSend(skill, info);
        }

        // ══════════════════════════════════════════════════════════════════
        // Resolução de mira (cliente)
        // ══════════════════════════════════════════════════════════════════

        private bool ResolveAim(SkillData skill, int index, out SkillCastInfo info, out string error)
        {
            info  = default;
            error = null;

            Camera cam = _player.MainCamera != null ? _player.MainCamera : Camera.main;
            if (cam == null && skill.AimMode != SkillAimMode.SelfCast
                            && skill.AimMode != SkillAimMode.AroundSelf)
            {
                error = "Câmera indisponível.";
                return false;
            }

            switch (skill.AimMode)
            {
                case SkillAimMode.SelfCast:
                case SkillAimMode.AroundSelf:
                    info = SkillCastInfo.Self(index);
                    return true;

                case SkillAimMode.TargetEnemy:
                {
                    if (!TryRaycastEnemy(cam, out NetworkMonsterEntity monster))
                    {
                        error = "Mire em um inimigo.";
                        return false;
                    }
                    float dist = Vector3.Distance(transform.position, monster.Position);
                    if (dist > skill.Range * 1.1f)
                    {
                        error = "Alvo fora de alcance.";
                        return false;
                    }
                    info = SkillCastInfo.ForTarget(index, monster.netId);
                    FaceTowards(monster.Position);
                    return true;
                }

                case SkillAimMode.Skillshot:
                {
                    if (!TryRaycastGround(cam, out Vector3 point))
                    {
                        error = "Mira inválida.";
                        return false;
                    }
                    Vector3 origin = transform.position + Vector3.up * 1.2f;
                    Vector3 dir = point - transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                    dir.Normalize();
                    info = SkillCastInfo.ForDirection(index, origin, dir);
                    FaceTowards(transform.position + dir);
                    return true;
                }

                case SkillAimMode.GroundTarget:
                {
                    if (!TryRaycastGround(cam, out Vector3 point))
                    {
                        error = "Mire em um ponto no chão.";
                        return false;
                    }
                    // Clampa o ponto ao alcance máximo (servidor revalida).
                    Vector3 flat = point - transform.position; flat.y = 0f;
                    if (flat.magnitude > skill.Range)
                        point = transform.position + flat.normalized * skill.Range + Vector3.up * point.y;
                    info = SkillCastInfo.ForGround(index, point);
                    FaceTowards(point);
                    return true;
                }
            }

            error = "Modo de mira desconhecido.";
            return false;
        }

        private bool TryRaycastEnemy(Camera cam, out NetworkMonsterEntity monster)
        {
            monster = null;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // Se a máscara não foi atribuída no Inspector (== 0), cai para "todas as
            // layers" para ainda funcionar; mesmo assim só aceita NetworkMonsterEntity.
            int mask = targetableMask != 0 ? (int)targetableMask : ~0;

            if (Physics.Raycast(ray, out RaycastHit hit, RAYCAST_DIST, mask, QueryTriggerInteraction.Collide))
            {
                monster = hit.collider.GetComponentInParent<NetworkMonsterEntity>();
                if (monster != null && !monster.IsDead) return true;
            }

            // Fallback final: varre tudo e pega o primeiro monstro sob o cursor.
            if (targetableMask != 0 &&
                Physics.Raycast(ray, out RaycastHit hit2, RAYCAST_DIST, ~0, QueryTriggerInteraction.Collide))
            {
                monster = hit2.collider.GetComponentInParent<NetworkMonsterEntity>();
                if (monster != null && !monster.IsDead) return true;
            }

            monster = null;
            return false;
        }

        private bool TryRaycastGround(Camera cam, out Vector3 point)
        {
            point = Vector3.zero;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            int mask = groundMask != 0 ? (int)groundMask : ~0;
            if (Physics.Raycast(ray, out RaycastHit hit, RAYCAST_DIST, mask))
            {
                point = hit.point;
                return true;
            }
            // Fallback: intercepta o plano horizontal na altura do player.
            Plane plane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        private void FaceTowards(Vector3 worldPos)
        {
            Vector3 dir = worldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        // ══════════════════════════════════════════════════════════════════
        // Cast → envio
        // ══════════════════════════════════════════════════════════════════

        private void StartCastAndSend(SkillData skill, SkillCastInfo info)
        {
            float castTime = 0f;
            if (skill.CastTime > 0f && _player.Stats != null)
                castTime = StatsCalculator.CalculateEffectiveCastTime(skill.CastTime, _player.Stats.CastSpeed);

            if (castTime <= INSTANT_CAST_EPS)
            {
                Send(skill, info);
                return;
            }

            if (_castCoroutine != null) StopCoroutine(_castCoroutine);
            _castCoroutine = StartCoroutine(CastRoutine(skill, info, castTime));
        }

        private IEnumerator CastRoutine(SkillData skill, SkillCastInfo info, float castTime)
        {
            _isCasting = true;
            OnCastStarted?.Invoke(skill.Name, castTime);

            if (!string.IsNullOrEmpty(skill.AnimTrigger))
                _animator?.SetTrigger("CastStart");

            float elapsed = 0f;
            bool  cancelled = false;
            Vector3 startPos = transform.position;

            while (elapsed < castTime)
            {
                elapsed += Time.deltaTime;

                if (_player.IsDead) { cancelled = true; break; }

                if (skill.MovementInterruptsCast
                    && (transform.position - startPos).sqrMagnitude > 0.04f)
                {
                    cancelled = true;
                    break;
                }

                OnCastProgress?.Invoke(elapsed / castTime);
                yield return null;
            }

            _isCasting     = false;
            _castCoroutine = null;
            OnCastFinished?.Invoke();

            if (!cancelled) Send(skill, info);
        }

        private void Send(SkillData skill, SkillCastInfo info)
        {
            if (!info.IsFinite())
            {
                Log("Mira não-finita — abortado.");
                return;
            }

            if (_animator != null && !string.IsNullOrEmpty(skill.AnimTrigger))
                CmdPlayAnim(skill.AnimTrigger);

            _netPlayer?.CmdUseSkill(info);
            Log($"CmdUseSkill {info.SkillIndex} ({skill.AimMode})");

            // Se for um laser sustentado, inicia o canal que segue o cursor enquanto segura.
            if (skill.IsSustainedBeam)
                BeginBeamChannel(info.SkillIndex, skill, info.AimDirection);
        }

        // ── Canal de beam: segue o cursor enquanto a tecla está segurada ────
        private void BeginBeamChannel(int index, SkillData skill, Vector3 initialDir)
        {
            _beamChanneling = true;
            _beamSkillIndex = index;
            _beamEndsAt     = Time.time + skill.BeamDuration;
            _lastBeamDir    = initialDir;
            _beamSendTimer  = 0f;
        }

        /// <summary>O controller chama isto quando a tecla da skill de beam é solta.</summary>
        public void NotifySkillKeyReleased(int index)
        {
            if (_beamChanneling && _beamSkillIndex == index)
                EndBeamChannel();
        }

        private void EndBeamChannel()
        {
            if (!_beamChanneling) return;
            _beamChanneling = false;
            _beamSkillIndex = -1;
            _netPlayer?.CmdEndBeam();
        }

        private void TickBeamChannel()
        {
            if (!_beamChanneling) return;

            // Encerra ao fim da duração (o servidor também encerra sozinho).
            if (Time.time >= _beamEndsAt || _player == null || _player.IsDead)
            {
                EndBeamChannel();
                return;
            }

            _beamSendTimer -= Time.deltaTime;
            if (_beamSendTimer > 0f) return;
            _beamSendTimer = BEAM_AIM_SEND_INTERVAL;

            // Recalcula a direção pelo cursor e envia ao servidor (com throttle).
            Camera cam = _player.MainCamera != null ? _player.MainCamera : Camera.main;
            if (cam == null) return;
            if (!TryRaycastGround(cam, out Vector3 point)) return;

            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();

            // Evita spam se a direção mal mudou.
            if (Vector3.Dot(dir, _lastBeamDir) > 0.9995f) return;
            _lastBeamDir = dir;

            FaceTowards(transform.position + dir);
            _netPlayer?.CmdUpdateBeamAim(dir);
        }

        public void CancelCast()
        {
            if (_castCoroutine != null)
            {
                StopCoroutine(_castCoroutine);
                _castCoroutine = null;
            }
            if (_isCasting)
            {
                _isCasting = false;
                OnCastFinished?.Invoke();
            }
        }

        // Confirmação / rejeição vindas do servidor (mantém compatibilidade c/ RPCs)
        public void OnServerSkillConfirmed(int skillIndex, float cooldownDuration)
        {
            if (skillIndex < 0 || skillIndex >= MAX_SKILLS) return;
            _uiCooldownTimers[skillIndex] = cooldownDuration;
            OnCooldownStarted?.Invoke(skillIndex, cooldownDuration);
            OnSkillFired?.Invoke(skillIndex);
        }

        public void OnServerSkillRejected(int skillIndex, string reason)
        {
            UIManager.Instance?.ShowMessage(reason);
            Log($"Skill {skillIndex} rejeitada: {reason}");
        }

        // ── Animação ───────────────────────────────────────────────────────

        [Command]
        private void CmdPlayAnim(string trigger) => RpcPlayAnim(trigger);

        [ClientRpc]
        private void RpcPlayAnim(string trigger)
        {
            if (_animator != null && !string.IsNullOrEmpty(trigger))
                _animator.SetTrigger(trigger);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string SlotName(int i) => i switch
        { 0 => "Q", 1 => "W", 2 => "E", 3 => "R", _ => i.ToString() };

        private void Log(string msg)
        {
            if (!debugLogs) return;
            Debug.Log($"[SkillSystem] {msg}");
        }
    }
}
