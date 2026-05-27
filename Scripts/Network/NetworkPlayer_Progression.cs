using System;
using UnityEngine;
using Mirror;
using RPG.Data;

namespace RPG.Network
{
    public partial class NetworkPlayer
    {
        // ══════════════════════════════════════════════════════════════════
        // Progressão e Atributos
        // ══════════════════════════════════════════════════════════════════

        [Server]
        public void ServerGrantExp(long amount)
        {
            if (_serverCharData == null || amount <= 0) return;
            amount = Math.Min(amount, GameConstants.Server.MAX_XP_PER_GRANT);

            bool leveledUp = _serverCharData.AddExperience(amount);

            Experience            = _serverCharData.Experience;
            ExperienceToNextLevel = _serverCharData.ExperienceToNextLevel;
            Level                 = _serverCharData.Level;

            FreeAttributePoints                  = Mathf.Min(_serverCharData.FreeAttributePoints, MAX_FREE_POINTS);
            _serverCharData.FreeAttributePoints  = FreeAttributePoints;

            if (leveledUp)
            {
                ServerRecalculateStats();
                CurrentHP = MaxHP;
                CurrentMP = MaxMP;
                _serverCharData.CurrentHP = MaxHP;
                _serverCharData.CurrentMP = MaxMP;
                _regenLoop?.ServerStart();
                Debug.Log($"[Server] {CharacterName} → Lv {Level}!");

                _questManager?.NotifyLevelUp(Level);
            }

            Managers.DatabaseManager.Instance?.LogEconomy(_serverCharData.CharacterId, "exp_gain", amount);

            bool largeExpGain = amount > (ExperienceToNextLevel * 0.1f);
            if (leveledUp || largeExpGain) ServerSaveCharacterForced();
            else                           MarkDirty();

            RpcOnExpGained(amount, leveledUp);
        }

        [Command]
        public void CmdAllocateAttribute(int attributeIndex)
        {
            if (connectionToClient == null) return;

            if (Time.time - _lastAllocateTime < ALLOCATE_MIN_INTERVAL)
            {
                RpcAllocateRejected("Aguarde um pouco antes de alocar novamente.");
                return;
            }

            if (FreeAttributePoints <= 0 || _serverCharData == null)
            {
                RpcAllocateRejected("Você não tem pontos suficientes.");
                return;
            }

            if (attributeIndex < 0 || attributeIndex > 5)
            {
                RpcAllocateRejected("Atributo inválido.");
                return;
            }

            _lastAllocateTime = Time.time;

            if (IsAllocationLimitExceeded(attributeIndex))
            {
                Debug.LogWarning($"[Security] {CharacterName} tentou alocar atributo {attributeIndex} além do limite.");
                RpcAllocateRejected("Limite de pontos para este atributo atingido.");
                return;
            }

            FreeAttributePoints--;
            _serverCharData.FreeAttributePoints--;

            switch (attributeIndex)
            {
                case 0: AllocatedSTR++; _serverCharData.AllocatedSTR++; break;
                case 1: AllocatedAGI++; _serverCharData.AllocatedAGI++; break;
                case 2: AllocatedVIT++; _serverCharData.AllocatedVIT++; break;
                case 3: AllocatedDEX++; _serverCharData.AllocatedDEX++; break;
                case 4: AllocatedINT++; _serverCharData.AllocatedINT++; break;
                case 5: AllocatedLUK++; _serverCharData.AllocatedLUK++; break;
            }

            ServerRecalculateStats();
            MarkDirty();
        }

        private bool IsAllocationLimitExceeded(int attributeIndex)
        {
            int limit = CharacterData.MAX_ALLOCATED_PER_STAT;
            return attributeIndex switch
            {
                0 => _serverCharData.AllocatedSTR >= limit,
                1 => _serverCharData.AllocatedAGI >= limit,
                2 => _serverCharData.AllocatedVIT >= limit,
                3 => _serverCharData.AllocatedDEX >= limit,
                4 => _serverCharData.AllocatedINT >= limit,
                5 => _serverCharData.AllocatedLUK >= limit,
                _ => true
            };
        }

        [Server]
        public void ServerOnEquipmentChanged()
        {
            if (_serverCharData == null || _inventory == null) return;

            _serverCharData.EquipmentBonuses = _inventory.BuildEquipmentBonuses();
            ServerRecalculateStats();
            ServerSaveCharacterForced();
        }

        [Server]
        private void ServerRecalculateStats()
        {
            _serverStats = _serverCharData.GetDerivedStats();

            MaxHP = Mathf.Min(_serverStats.MaxHP, GameConstants.Combat.MAX_HP);
            MaxMP = Mathf.Min(_serverStats.MaxMP, GameConstants.Combat.MAX_MP);

            if (CurrentHP > MaxHP) CurrentHP = MaxHP;
            if (CurrentMP > MaxMP) CurrentMP = MaxMP;

            _serverCharData.CurrentHP = CurrentHP;
            _serverCharData.CurrentMP = CurrentMP;

            ConfigureServerAgent();
            StatsVersion++;
        }

        [Server]
        private System.Collections.IEnumerator SendInitRpcDelayed(CharacterData charData)
        {
            yield return null;
            yield return null;
            yield return null;

            RpcInitializeLocalPlayer(new PlayerInitData
            {
                CharName   = charData.CharacterName,
                Race       = (int)charData.Race,
                Level      = charData.Level,
                Exp        = charData.Experience,
                ExpToNext  = charData.ExperienceToNextLevel,
                FreePoints = charData.FreeAttributePoints,
                AllocSTR   = charData.AllocatedSTR,
                AllocAGI   = charData.AllocatedAGI,
                AllocVIT   = charData.AllocatedVIT,
                AllocDEX   = charData.AllocatedDEX,
                AllocINT   = charData.AllocatedINT,
                AllocLUK   = charData.AllocatedLUK,
                BaseSTR    = charData.BaseAttributes.STR,
                BaseAGI    = charData.BaseAttributes.AGI,
                BaseVIT    = charData.BaseAttributes.VIT,
                BaseDEX    = charData.BaseAttributes.DEX,
                BaseINT    = charData.BaseAttributes.INT,
                BaseLUK    = charData.BaseAttributes.LUK,
                CurHP      = CurrentHP,
                CurMP      = CurrentMP
            });
        }
    }
}
