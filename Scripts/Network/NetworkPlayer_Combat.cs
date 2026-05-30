using UnityEngine;
using Mirror;
using RPG.Data;

namespace RPG.Network
{
    public partial class NetworkPlayer
    {
        // ══════════════════════════════════════════════════════════════════
        // Morte / Respawn / Dano
        // ══════════════════════════════════════════════════════════════════

        [Server]
        public void ServerApplyDamage(float dmg)
        {
            if (Dead || _serverImmunityTimer > 0f) return;
            dmg = SanitizeAmount(dmg);
            if (dmg <= 0f) return;

            _regenLoop?.NotifyDamageTaken();
            CurrentHP = Mathf.Max(0f, CurrentHP - dmg);
            if (_serverCharData != null) _serverCharData.CurrentHP = CurrentHP;
            if (CurrentHP <= 0f) ServerDie();
        }

        [Server]
        public void ServerApplyDamageWithFeedback(float dmg)
        {
            if (Dead || _serverImmunityTimer > 0f) return;
            dmg = SanitizeAmount(dmg);
            if (dmg <= 0f) return;

            _regenLoop?.NotifyDamageTaken();
            float before    = CurrentHP;
            CurrentHP       = Mathf.Max(0f, CurrentHP - dmg);
            float actual    = before - CurrentHP;

            if (_serverCharData != null) _serverCharData.CurrentHP = CurrentHP;
            if (actual > 0f) RpcShowDamageTaken(actual);
            if (CurrentHP <= 0f) ServerDie();
        }

        [Server]
        public void ServerApplyHeal(float amount)
        {
            if (Dead) return;
            amount = SanitizeAmount(amount);
            if (amount <= 0f) return;

            float before = CurrentHP;
            CurrentHP    = Mathf.Min(MaxHP, CurrentHP + amount);
            float healed = CurrentHP - before;

            if (_serverCharData != null) _serverCharData.CurrentHP = CurrentHP;
            if (healed > 0f) RpcShowHeal(healed);
        }

        [Server]
        public void ServerRestoreMP(float amount)
        {
            if (Dead) return;
            amount = SanitizeAmount(amount);
            if (amount <= 0f) return;

            CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
            if (_serverCharData != null) _serverCharData.CurrentMP = CurrentMP;
        }

        [Server]
        public void ServerConsumeMP(float amount)
        {
            amount = SanitizeAmount(amount);
            CurrentMP = Mathf.Max(0f, CurrentMP - amount);
            if (_serverCharData != null) _serverCharData.CurrentMP = CurrentMP;
        }

        [Server]
        private void ServerDie()
        {
            CurrentHP = 0f;
            _regenLoop?.Stop();
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();

            IsMoving = false;

            // --- Penalidade de Morte ---
            long penalty = 0;
            if (_serverCharData != null && Level > 1)
            {
                // FIX: Penalidade de 5% do XP total do nível atual.
                // Cap mínimo de 1% do nível para garantir que a morte tenha peso.
                penalty = (long)(ExperienceToNextLevel * 0.05f);
                long minPenalty = (long)(ExperienceToNextLevel * 0.01f);
                if (penalty < minPenalty) penalty = minPenalty;

                // Não remove mais do que o player tem no nível atual para evitar frustração extrema
                if (penalty > Experience) penalty = Experience;

                _serverCharData.RemoveExperience(penalty);
                Experience = _serverCharData.Experience;
                MarkDirty();
                RpcShowMessageToOwner($"Você morreu e perdeu {penalty} XP.");
            }

            _cooldowns?.ServerClearAll();
            ServerSaveCharacterForced();
            RpcPlayerDied(penalty);
        }

        [Server]
        private void ServerRespawn()
        {
            if (_serverStats == null) return;

            Vector3 pos = GetRespawnPosition();
            transform.position = pos;
            if (_agent != null && _agent.isOnNavMesh) _agent.Warp(pos);

            MaxHP     = Mathf.Min(_serverStats.MaxHP, GameConstants.Combat.MAX_HP);
            MaxMP     = Mathf.Min(_serverStats.MaxMP, GameConstants.Combat.MAX_MP);
            CurrentHP = MaxHP * 0.5f;
            CurrentMP = MaxMP * 0.5f;

            // FIX: Imunidade temporária (5s) após respawn para evitar death-loop
            _serverImmunityTimer = 5f;

            // FIX: Sincroniza a posição no anti-cheat após o Respawn/Warp
            _controller?.ServerSyncSafetyPosition(pos);

            if (_serverCharData != null)
            {
                _serverCharData.CurrentHP = CurrentHP;
                _serverCharData.CurrentMP = CurrentMP;
                ServerSaveCharacterForced();
            }

            _regenLoop?.ResetCombatSuppression();
            ConfigureServerAgent();
            _regenLoop?.ServerStart();

            RpcOnRespawned(pos, CurrentHP, MaxHP, CurrentMP, MaxMP);
        }

        [Server]
        private Vector3 GetRespawnPosition()
        {
            if (_respawnPoints != null && _respawnPoints.Length > 0)
            {
                var pt = _respawnPoints[UnityEngine.Random.Range(0, _respawnPoints.Length)];
                if (pt != null) return pt.position;
            }

            if (_serverCharData != null)
            {
                var nm = RPGNetworkManager.singleton;
                if (nm != null)
                {
                    Vector3 racePos = nm.GetSpawnPositionForRace(_serverCharData.Race, _serverCharData);
                    if (racePos.sqrMagnitude > 0.01f) return racePos;
                }
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(Vector3.zero, out UnityEngine.AI.NavMeshHit hit, 50f, UnityEngine.AI.NavMesh.AllAreas))
                return hit.position;

            return Vector3.zero;
        }

        private static float SanitizeAmount(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Mathf.Max(0f, v);
        }

        [Command]
        public void CmdRequestRespawn()
        {
            if (connectionToClient == null) return;
            if (!Dead) return;
            ServerRespawn();
        }

        private RegenSnapshot BuildRegenSnapshot()
        {
            return new RegenSnapshot
            {
                IsDead    = Dead,
                CurrentHP = CurrentHP, MaxHP = MaxHP,
                CurrentMP = CurrentMP, MaxMP = MaxMP,
                Stats     = _serverStats
            };
        }

        [Server]
        private void ApplyRegenValues(float newHP, float newMP)
        {
            CurrentHP = newHP;
            CurrentMP = newMP;
            if (_serverCharData != null)
            {
                _serverCharData.CurrentHP = CurrentHP;
                _serverCharData.CurrentMP = CurrentMP;
            }
        }

        private void OnServerRegenTick(float hpRestored, float mpRestored)
        {
            RpcShowRegenTick(hpRestored, mpRestored);
        }
    }
}
