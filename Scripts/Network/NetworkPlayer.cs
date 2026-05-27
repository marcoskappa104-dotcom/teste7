using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Mirror;
using RPG.Data;
using RPG.UI;
using RPG.Managers;
using RPG.Character;
using RPG.Combat;

namespace RPG.Network
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkInventory))]
    [RequireComponent(typeof(PlayerCooldownTracker))]
    [RequireComponent(typeof(PlayerRegenLoop))]
    public partial class NetworkPlayer : NetworkBehaviour, ITargetable
    {
        public static readonly HashSet<NetworkPlayer> All = new HashSet<NetworkPlayer>();

        private const float SAVE_INTERVAL                = 30f; 
        private const float ALLOCATE_MIN_INTERVAL        = 0.3f;
        private const float REGEN_DISPLAY_THRESHOLD      = 1f;
        private const int   MAX_FREE_POINTS              = CharacterData.MAX_LEVEL * CharacterData.POINTS_PER_LEVEL_UP;

        private const float AGENT_ACCELERATION   = 60f;
        private const float AGENT_ANGULAR_SPEED  = 720f;
        private const float AGENT_STOPPING_DIST  = 0.15f;
        private const float AGENT_MIN_SPEED      = 3f;
        private const float AGENT_MAX_SPEED      = 7.5f;

        // ── SyncVars ───────────────────────────────────────────────────────
        [SyncVar(hook = nameof(OnNetNameChanged))]       public string CharacterName         = "...";
        [SyncVar(hook = nameof(OnRaceStrChanged))]       public string RaceStr               = "Paulista";
        [SyncVar(hook = nameof(OnNetLevelChanged))]      public int    Level                 = 1;

        [SyncVar(hook = nameof(OnNetMaxHPChanged))]      public float  MaxHP                 = 1f;
        [SyncVar(hook = nameof(OnNetHPChanged))]         public float  CurrentHP             = 0f;
        [SyncVar(hook = nameof(OnNetMaxMPChanged))]      public float  MaxMP                 = 1f;
        [SyncVar(hook = nameof(OnNetMPChanged))]         public float  CurrentMP             = 0f;

        [SyncVar(hook = nameof(OnNetMovingChanged))]     public bool   IsMoving              = false;
        [SyncVar(hook = nameof(OnNetExpChanged))]        public long   Experience            = 0;
        [SyncVar(hook = nameof(OnNetExpToNextChanged))]  public long   ExperienceToNextLevel = 100;
        [SyncVar(hook = nameof(OnNetFreePointsChanged))] public int    FreeAttributePoints   = 0;
        [SyncVar(hook = nameof(OnStatsVersionChanged))]  public int    StatsVersion          = 0;
        [SyncVar]                                        public int    PartyId               = 0; 

        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedSTR = 0;
        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedAGI = 0;
        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedVIT = 0;
        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedDEX = 0;
        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedINT = 0;
        [SyncVar(hook = nameof(OnAllocChanged))] public int AllocatedLUK = 0;

        [SyncVar] public int BaseSTR = 10;
        [SyncVar] public int BaseAGI = 10;
        [SyncVar] public int BaseVIT = 10;
        [SyncVar] public int BaseDEX = 10;
        [SyncVar] public int BaseINT = 10;
        [SyncVar] public int BaseLUK = 10;

        string  ITargetable.DisplayName => CharacterName;
        float   ITargetable.CurrentHP   => CurrentHP;
        float   ITargetable.MaxHP       => MaxHP;
        bool    ITargetable.IsDead      => Dead;
        Vector3 ITargetable.Position    => transform.position;

        public void OnSelected()   { if (_selectionIndicator) _selectionIndicator.SetActive(true);  }
        public void OnDeselected() { if (_selectionIndicator) _selectionIndicator.SetActive(false); }

        [Header("Visuals")]
        [SerializeField] private GameObject            _selectionIndicator;
        [SerializeField] private TMPro.TMP_Text        _nameTagText;
        [SerializeField] private UnityEngine.UI.Slider _hpBarSlider;

        [Header("Respawn Points")]
        [SerializeField] private Transform[] _respawnPoints;

        // ── Componentes ────────────────────────────────────────────────────
        private NavMeshAgent           _agent;
        private Animator               _animator;
        private PlayerEntity           _playerEntity;
        private NetworkInventory       _inventory;
        private RPG.Quest.QuestManager _questManager;
        private PlayerCooldownTracker  _cooldowns;
        private PlayerRegenLoop        _regenLoop;
        private NetworkPlayerController _controller;

        // ── Estado servidor ───────────────────────────────────────────────
        private CharacterData _serverCharData;
        private DerivedStats  _serverStats;
        private string        _serverAccountUsername;
        private float         _autoSaveTimer;
        private float         _lastAllocateTime         = -999f;
        private bool          _isDirty;

        public DerivedStats ServerStats => _serverStats;

        // ── Estado cliente ────────────────────────────────────────────────
        private CharacterRace _cachedRace = CharacterRace.Paulista;
        private bool          _clientInitialized;
        private bool          _pendingClientInit;
        private CharacterData _pendingInitData;
        private bool          _allocDirty;
        private bool          _equipDirty;
        private float         _lastMovingCmdTime;
        private const float MOVING_CMD_INTERVAL = 0.1f;

        public bool Dead => CurrentHP <= 0f;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent        = GetComponent<NavMeshAgent>();
            _animator     = GetComponentInChildren<Animator>();
            _playerEntity = GetComponent<PlayerEntity>();
            _inventory    = GetComponent<NetworkInventory>();
            _questManager = GetComponent<RPG.Quest.QuestManager>();
            _cooldowns    = GetComponent<PlayerCooldownTracker>();
            _regenLoop    = GetComponent<PlayerRegenLoop>();
            _controller   = GetComponent<NetworkPlayerController>();
        }

        public override void OnStartServer()
        {
            All.Add(this);
            _autoSaveTimer = UnityEngine.Random.Range(0f, SAVE_INTERVAL);
            _cooldowns?.ServerReset();

            _regenLoop.SnapshotProvider = BuildRegenSnapshot;
            _regenLoop.ApplyRegen       = ApplyRegenValues;
            _regenLoop.OnRegenTick      += OnServerRegenTick;
        }

        public override void OnStopServer()
        {
            All.Remove(this);
            _regenLoop?.Stop();
            if (_regenLoop != null)
                _regenLoop.OnRegenTick -= OnServerRegenTick;

            if (_serverCharData != null && !string.IsNullOrEmpty(_serverAccountUsername))
                ServerSaveCharacterForced();
        }

        public override void OnStartClient()
        {
            if (_nameTagText        != null) _nameTagText.text = CharacterName;
            if (_selectionIndicator != null) _selectionIndicator.SetActive(false);
            if (!isLocalPlayer && _agent != null) _agent.enabled = false;

            UpdateCachedRace();
        }

        public override void OnStopClient()
        {
            _clientInitialized = false;
            _pendingClientInit = false;
            _pendingInitData   = null;
            _allocDirty        = false;
            _equipDirty        = false;

            if (_inventory != null)
                _inventory.OnEquipmentChanged -= OnClientEquipmentChanged;
        }

        public override void OnStartLocalPlayer()
        {
            if (_agent != null) _agent.enabled = true;

            if (_inventory != null)
                _inventory.OnEquipmentChanged += OnClientEquipmentChanged;

            if (_pendingClientInit && _pendingInitData != null)
            {
                var data = _pendingInitData;
                _pendingClientInit = false;
                _pendingInitData   = null;
                StartCoroutine(DelayedClientInit(data));
            }
        }

        private void Update()
        {
            if (isServer) ServerUpdate();
            if (!isLocalPlayer || Dead) return;

            ClientMovingUpdate();

            if (_allocDirty)
            {
                _allocDirty = false;
                ApplyAllocatedDataToEntity();
            }
            if (_equipDirty)
            {
                _equipDirty = false;
                ApplyEquipmentDataToEntity();
            }
        }

        [Server]
        private void ServerUpdate()
        {
            _autoSaveTimer -= Time.deltaTime;
            if (_autoSaveTimer <= 0f)
            {
                _autoSaveTimer = SAVE_INTERVAL;
                if (_isDirty) ServerSaveCharacterForced();
            }

            _cooldowns?.ServerTick();
        }

        private void ClientMovingUpdate()
        {
            if (_agent == null || !_agent.enabled) return;
            bool moving = _agent.velocity.sqrMagnitude > 0.05f;
            if (moving != IsMoving && Time.time - _lastMovingCmdTime >= MOVING_CMD_INTERVAL)
            {
                _lastMovingCmdTime = Time.time;
                CmdSetMoving(moving);
            }
        }

        [Command]
        public void CmdSetMoving(bool moving)
        {
            if (connectionToClient == null) return;
            if (Dead && moving) return;
            IsMoving = moving;
        }

        [Server]
        private void ConfigureServerAgent()
        {
            if (_agent == null || _serverStats == null) return;

            _agent.speed            = Mathf.Clamp(_serverStats.MoveSpeed, AGENT_MIN_SPEED, AGENT_MAX_SPEED);
            _agent.acceleration     = AGENT_ACCELERATION;
            _agent.angularSpeed     = AGENT_ANGULAR_SPEED;
            _agent.autoBraking      = false;
            _agent.stoppingDistance = AGENT_STOPPING_DIST;
        }

        [Server]
        public void ServerInitialize(CharacterData charData, string accountUsername)
        {
            if (charData == null || string.IsNullOrEmpty(accountUsername)) return;
            
            _serverAccountUsername = accountUsername;
            _serverCharData        = charData;
            _cachedRace            = charData.Race;

            CharacterName = charData.CharacterName ?? "Player";
            RaceStr       = charData.Race.ToString();
            Level         = Mathf.Clamp(charData.Level, 1, CharacterData.MAX_LEVEL);

            Experience            = Math.Max(0L, charData.Experience);
            ExperienceToNextLevel = Math.Max(0L, charData.ExperienceToNextLevel);

            int freePoints = Math.Max(0, Math.Min(charData.FreeAttributePoints, MAX_FREE_POINTS));
            FreeAttributePoints = freePoints;
            charData.FreeAttributePoints = freePoints;

            int allocLimit = CharacterData.MAX_ALLOCATED_PER_STAT;
            AllocatedSTR = Math.Max(0, Math.Min(charData.AllocatedSTR, allocLimit));
            AllocatedAGI = Math.Max(0, Math.Min(charData.AllocatedAGI, allocLimit));
            AllocatedVIT = Math.Max(0, Math.Min(charData.AllocatedVIT, allocLimit));
            AllocatedDEX = Math.Max(0, Math.Min(charData.AllocatedDEX, allocLimit));
            AllocatedINT = Math.Max(0, Math.Min(charData.AllocatedINT, allocLimit));
            AllocatedLUK = Math.Max(0, Math.Min(charData.AllocatedLUK, allocLimit));

            BaseSTR = charData.BaseAttributes?.STR ?? 10;
            BaseAGI = charData.BaseAttributes?.AGI ?? 10;
            BaseVIT = charData.BaseAttributes?.VIT ?? 10;
            BaseDEX = charData.BaseAttributes?.DEX ?? 10;
            BaseINT = charData.BaseAttributes?.INT ?? 10;
            BaseLUK = charData.BaseAttributes?.LUK ?? 10;

            _inventory?.ServerLoadFromDatabase(charData.CharacterId);
            _inventory?.ServerLoadGemLoadout(charData.CharacterId);
            _inventory?.ServerLoadEquippedFromDatabase(charData.CharacterId);
            _questManager?.ServerLoadFromDatabase(charData.CharacterId, _serverAccountUsername);

            charData.EquipmentBonuses = _inventory != null ? _inventory.BuildEquipmentBonuses() : new EquipmentBonuses();
            _serverStats = charData.GetDerivedStats();

            MaxHP     = Mathf.Min(_serverStats.MaxHP, GameConstants.Combat.MAX_HP);
            MaxMP     = Mathf.Min(_serverStats.MaxMP, GameConstants.Combat.MAX_MP);
            CurrentHP = (charData.CurrentHP > 0f && charData.CurrentHP <= MaxHP) ? charData.CurrentHP : MaxHP;
            CurrentMP = (charData.CurrentMP > 0f && charData.CurrentMP <= MaxMP) ? charData.CurrentMP : MaxMP;

            StatsVersion++;

            var savedPos = new Vector3(charData.PosX, charData.PosY, charData.PosZ);
            if (savedPos.sqrMagnitude > 0.01f)
            {
                transform.position = savedPos;
                if (_agent != null && _agent.isOnNavMesh) _agent.Warp(savedPos);
                
                // FIX: Sincroniza a posição no anti-cheat após o Warp inicial
                _controller?.ServerSyncSafetyPosition(savedPos);
            }

            ConfigureServerAgent();
            _regenLoop?.ServerStart();

            Debug.Log($"[Server] {charData.CharacterName} Lv{Level} inicializado.");
            StartCoroutine(SendInitRpcDelayed(charData));
        }

        [Server]
        public bool ServerCheckAndSetCooldown(int skillIndex, float cooldownDuration)
        {
            return _cooldowns != null
                && _cooldowns.TryCheckAndSetSkill(skillIndex, cooldownDuration,
                    GameConstants.Server.MAX_SKILL_COOLDOWN_SECONDS);
        }

        [Server]
        public bool ServerCheckAndSetCooldownLong(long cooldownKey, float cooldownDuration)
        {
            return _cooldowns != null
                && _cooldowns.TryCheckAndSetBasicAttack(cooldownKey, cooldownDuration,
                    GameConstants.Server.MAX_SKILL_COOLDOWN_SECONDS);
        }

        public CharacterRace GetRaceEnum() => _cachedRace;

        private void UpdateCachedRace()
        {
            if (System.Enum.TryParse<CharacterRace>(RaceStr, out var race))
                _cachedRace = race;
            else
                _cachedRace = CharacterRace.Paulista;
        }

        private void OnNetNameChanged(string _, string v) { if (_nameTagText != null) _nameTagText.text = v; }
        private void OnRaceStrChanged(string _, string __) => UpdateCachedRace();

        private void OnNetMaxHPChanged(float _, float newMax)
        {
            if (_hpBarSlider != null) { _hpBarSlider.maxValue = Mathf.Max(1f, newMax); if (_hpBarSlider.value > newMax) _hpBarSlider.value = newMax; }
            if (isLocalPlayer && _playerEntity != null && _playerEntity.IsInitialized) _playerEntity.SetHPFromServer(CurrentHP, newMax);
        }

        private void OnNetHPChanged(float _, float newHP)
        {
            if (_hpBarSlider != null) { _hpBarSlider.maxValue = Mathf.Max(1f, MaxHP); _hpBarSlider.value = Mathf.Clamp(newHP, 0f, _hpBarSlider.maxValue); _hpBarSlider.gameObject.SetActive(newHP < MaxHP); }
            if (isLocalPlayer && _playerEntity != null && _playerEntity.IsInitialized) _playerEntity.SetHPFromServer(newHP, MaxHP);
        }

        private void OnNetMaxMPChanged(float _, float newMax) { if (isLocalPlayer && _playerEntity != null && _playerEntity.IsInitialized) _playerEntity.SetMPFromServer(CurrentMP, newMax); }
        private void OnNetMPChanged(float _, float newMP) { if (isLocalPlayer && _playerEntity != null && _playerEntity.IsInitialized) _playerEntity.SetMPFromServer(newMP, MaxMP); }
        private void OnNetLevelChanged(int _, int v) { if (isLocalPlayer) UIManager.Instance?.RefreshLevel(v); }
        private void OnNetFreePointsChanged(int _, int newPoints) { if (isLocalPlayer) AttributeWindowUI.Instance?.OnFreePointsUpdated(newPoints); }
        private void OnNetMovingChanged(bool _, bool v) => _animator?.SetBool("IsMoving", v);

        private void OnNetExpChanged(long _, long __) { if (isLocalPlayer) { UIManager.Instance?.RefreshExpBar(Experience, ExperienceToNextLevel); AttributeWindowUI.Instance?.RefreshXPBar(Experience, ExperienceToNextLevel); } }
        private void OnNetExpToNextChanged(long _, long __) { if (isLocalPlayer) { UIManager.Instance?.RefreshExpBar(Experience, ExperienceToNextLevel); AttributeWindowUI.Instance?.RefreshXPBar(Experience, ExperienceToNextLevel); } }
        private void OnAllocChanged(int _, int __) { if (isLocalPlayer) _allocDirty = true; }
        private void OnClientEquipmentChanged() { if (isLocalPlayer) _equipDirty = true; }

        private void ApplyAllocatedDataToEntity()
        {
            if (_playerEntity?.Data == null) return;
            _playerEntity.Data.BaseAttributes.STR = BaseSTR; _playerEntity.Data.BaseAttributes.AGI = BaseAGI; _playerEntity.Data.BaseAttributes.VIT = BaseVIT;
            _playerEntity.Data.BaseAttributes.DEX = BaseDEX; _playerEntity.Data.BaseAttributes.INT = BaseINT; _playerEntity.Data.BaseAttributes.LUK = BaseLUK;
            _playerEntity.Data.AllocatedSTR = AllocatedSTR; _playerEntity.Data.AllocatedAGI = AllocatedAGI; _playerEntity.Data.AllocatedVIT = AllocatedVIT;
            _playerEntity.Data.AllocatedDEX = AllocatedDEX; _playerEntity.Data.AllocatedINT = AllocatedINT; _playerEntity.Data.AllocatedLUK = AllocatedLUK;
            if (_playerEntity.IsInitialized) _playerEntity.FullRefreshStatsFromData();
        }

        private void ApplyEquipmentDataToEntity()
        {
            if (_playerEntity?.Data == null || _inventory == null) return;
            _playerEntity.Data.EquipmentBonuses = _inventory.BuildEquipmentBonuses();
            if (_playerEntity.IsInitialized) _playerEntity.FullRefreshStatsFromData();
        }

        private void OnStatsVersionChanged(int _, int __)
        {
            if (!isLocalPlayer || _playerEntity == null || !_playerEntity.IsInitialized) return;
            if (_inventory != null) _playerEntity.Data.EquipmentBonuses = _inventory.BuildEquipmentBonuses();
            ApplyAllocatedDataToEntity();
            _allocDirty = false; _equipDirty = false;
        }
    }
}
