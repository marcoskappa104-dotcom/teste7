using UnityEngine;
using Mirror;
using RPG.Data;
using RPG.Managers;

namespace RPG.Network
{
    public partial class NetworkPlayer
    {
        // ══════════════════════════════════════════════════════════════════
        // Persistência (Salvamento)
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private void MarkDirty() => _isDirty = true;

        [Server]
        public void ServerSaveCharacterForced()
        {
            if (_serverCharData == null) return;
            if (string.IsNullOrEmpty(_serverCharData.CharacterId)) return;
            if (string.IsNullOrEmpty(_serverAccountUsername)) return;

            // Snapshot de estado atual para o banco
            _serverCharData.CurrentHP = CurrentHP;
            _serverCharData.CurrentMP = CurrentMP;
            _serverCharData.PosX      = transform.position.x;
            _serverCharData.PosY      = transform.position.y;
            _serverCharData.PosZ      = transform.position.z;

            DatabaseManager.Instance?.SaveCharacter(_serverCharData, _serverAccountUsername);
            _inventory?.ServerSaveAll(_serverCharData.CharacterId, _serverAccountUsername);
            _questManager?.ServerSaveAll();
            
            _isDirty = false;
        }

        [Server] 
        public void ServerSaveCharacter() => ServerSaveCharacterForced();
    }
}
