using UnityEngine;
using Mirror;
using RPG.Data;
using RPG.UI;
using RPG.Managers;

namespace RPG.Network
{
    public partial class NetworkPlayer
    {
        // ══════════════════════════════════════════════════════════════════
        // ClientRpcs
        // ══════════════════════════════════════════════════════════════════

        [ClientRpc]
        private void RpcInitializeLocalPlayer(PlayerInitData d)
        {
            if (!isLocalPlayer) return;

            var data = new CharacterData
            {
                CharacterName         = d.CharName,
                Race                  = (CharacterRace)d.Race,
                Level                 = d.Level,
                Experience            = d.Exp,
                ExperienceToNextLevel = d.ExpToNext,
                FreeAttributePoints   = d.FreePoints,
                AllocatedSTR          = d.AllocSTR,
                AllocatedAGI          = d.AllocAGI,
                AllocatedVIT          = d.AllocVIT,
                AllocatedDEX          = d.AllocDEX,
                AllocatedINT          = d.AllocINT,
                AllocatedLUK          = d.AllocLUK,
                CurrentHP             = d.CurHP,
                CurrentMP             = d.CurMP,
                BaseAttributes = new BaseAttributes
                {
                    STR = d.BaseSTR, AGI = d.BaseAGI, VIT = d.BaseVIT,
                    DEX = d.BaseDEX, INT = d.BaseINT, LUK = d.BaseLUK
                },
                EquipmentBonuses = _inventory != null
                    ? _inventory.BuildEquipmentBonuses()
                    : new EquipmentBonuses()
            };

            if (_playerEntity == null)
            {
                _pendingClientInit = true;
                _pendingInitData   = data;
                return;
            }

            if (_clientInitialized) return;
            _clientInitialized = true;
            StartCoroutine(DelayedClientInit(data));
        }

        private System.Collections.IEnumerator DelayedClientInit(CharacterData data)
        {
            yield return null;

            if (_playerEntity == null)
            {
                _playerEntity = GetComponent<RPG.Character.PlayerEntity>();
                if (_playerEntity == null)
                {
                    Debug.LogError("[NetworkPlayer] PlayerEntity não encontrado.");
                    yield break;
                }
            }

            if (_inventory != null)
                data.EquipmentBonuses = _inventory.BuildEquipmentBonuses();

            _playerEntity.InitializeFromServer(data);
            UIManager.Instance?.BindLocalPlayer(_playerEntity);
            AttributeWindowUI.Instance?.BindPlayer(_playerEntity);
        }

        [ClientRpc]
        private void RpcPlayerDied(long xpPenalty)
        {
            if (_animator != null) _animator.SetBool("IsDead", true);

            if (!isLocalPlayer) return;

            if (_agent != null) { _agent.ResetPath(); _agent.isStopped = true; }
            GetComponent<NetworkPlayerController>()?.SetEnabled(false);
            GetComponent<Combat.SkillSystem>()?.CancelCast();

            _playerEntity?.OnServerDeath();
            DeathScreenUI.Show(this, xpPenalty);
            
            if (isLocalPlayer)
                AudioManager.Instance?.PlaySfx(AudioManager.Instance.DeathSfx);
        }

        [ClientRpc]
        private void RpcOnRespawned(Vector3 position, float hp, float maxHp, float mp, float maxMp)
        {
            if (_animator != null) _animator.SetBool("IsDead", false);

            if (!isLocalPlayer) return;

            if (_agent != null) { _agent.isStopped = false; _agent.Warp(position); }
            GetComponent<NetworkPlayerController>()?.SetEnabled(true);
            _playerEntity?.OnServerRespawn(position, hp, maxHp, mp, maxMp);
            DeathScreenUI.Hide();
        }

        [ClientRpc]
        public void RpcPlayAnimation(string trigger) => _animator?.SetTrigger(trigger);

        [ClientRpc]
        private void RpcOnExpGained(long amount, bool leveledUp)
        {
            if (!isLocalPlayer) return;
            if (Dead && !leveledUp) return;

            FloatingTextManager.Instance?.Show($"+{amount} XP",
                transform.position + Vector3.up * 2f, Color.cyan);

            if (leveledUp)
            {
                FloatingTextManager.Instance?.Show("LEVEL UP!",
                    transform.position + Vector3.up * 2.5f, Color.yellow);
                UIManager.Instance?.ShowMessage("Level up! Você evoluiu!");
                AudioManager.Instance?.PlayLevelUp();
            }
        }

        [ClientRpc]
        private void RpcShowDamageTaken(float dmg)
        {
            FloatingTextManager.Instance?.Show($"-{dmg:0}",
                transform.position + Vector3.up * 2f,
                new Color(1f, 0.25f, 0.25f));

            if (isLocalPlayer)
                AudioManager.Instance?.PlayHit();
        }

        [ClientRpc]
        private void RpcShowRegenTick(float hpRestored, float mpRestored)
        {
            if (!isLocalPlayer) return;
            Vector3 basePos = transform.position + Vector3.up * 2f;
            if (hpRestored >= REGEN_DISPLAY_THRESHOLD)
                FloatingTextManager.Instance?.Show($"+{hpRestored:0} HP",
                    basePos, new Color(0.4f, 1f, 0.4f));
            if (mpRestored >= REGEN_DISPLAY_THRESHOLD)
                FloatingTextManager.Instance?.Show($"+{mpRestored:0} MP",
                    basePos + new Vector3(0.3f, 0.2f, 0f), new Color(0.4f, 0.7f, 1f));
        }

        [ClientRpc]
        private void RpcShowHeal(float amount)
        {
            FloatingTextManager.Instance?.Show($"+{amount:0} HP",
                transform.position + Vector3.up * 1.5f, Color.green);
        }

        [ClientRpc]
        public void RpcSkillConfirmed(int skillIndex, float cooldown)
        {
            if (!isLocalPlayer) return;
            GetComponent<Combat.SkillSystem>()?.OnServerSkillConfirmed(skillIndex, cooldown);
        }

        [ClientRpc]
        private void RpcAllocateRejected(string reason)
        {
            if (!isLocalPlayer) return;
            AttributeWindowUI.Instance?.OnAllocationFailed(reason);
        }

        [ClientRpc]
        public void RpcSkillRejected(int skillIndex, string reason)
        {
            if (!isLocalPlayer) return;
            GetComponent<Combat.SkillSystem>()?.OnServerSkillRejected(skillIndex, reason);
        }

        [TargetRpc]
        public void RpcShowMessageToOwner(string msg)
        {
            UIManager.Instance?.ShowMessage(msg);
        }
    }
}
