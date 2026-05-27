using Mirror;
using UnityEngine;

namespace RPG.Network
{
    public static class NetworkLookup
    {
        public static NetworkPlayer FindPlayer(uint netId)
        {
            if (netId == 0) return null;
            if (NetworkServer.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.GetComponent<NetworkPlayer>();
            return null;
        }

        public static NetworkMonsterEntity FindMonster(uint netId)
        {
            if (netId == 0) return null;
            if (NetworkServer.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.GetComponent<NetworkMonsterEntity>();
            return null;
        }

        public static T FindEntity<T>(uint netId) where T : Component
        {
            if (netId == 0) return null;
            if (NetworkServer.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.GetComponent<T>();
            return null;
        }
    }
}
