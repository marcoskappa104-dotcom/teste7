using System;
using UnityEngine;

namespace RPG.Events
{
    /// <summary>
    /// Sistema de eventos global para desacoplar sistemas (ex: Lógica -> UI).
    /// Isso evita que o código de rede precise conhecer classes de UI específicas.
    /// </summary>
    public static class GameEvents
    {
        // --- Player Events ---
        public static event Action<float, float> OnPlayerHealthChanged;
        public static event Action<float, float> OnPlayerManaChanged;
        public static event Action<int>          OnPlayerLevelUp;
        public static event Action<long, long>   OnPlayerExperienceChanged;
        public static event Action<string>       OnSystemMessage;

        // --- Combat Events ---
        public static event Action<Vector3, float, bool> OnDamageDealt; // Pos, Dmg, IsCrit
        public static event Action<Vector3, float>       OnHealReceived;

        // --- Trigger Methods ---
        public static void TriggerPlayerHealthChanged(float cur, float max) => OnPlayerHealthChanged?.Invoke(cur, max);
        public static void TriggerPlayerManaChanged(float cur, float max)   => OnPlayerManaChanged?.Invoke(cur, max);
        public static void TriggerPlayerLevelUp(int level)                  => OnPlayerLevelUp?.Invoke(level);
        public static void TriggerPlayerExperienceChanged(long cur, long next) => OnPlayerExperienceChanged?.Invoke(cur, next);
        public static void TriggerSystemMessage(string msg)                 => OnSystemMessage?.Invoke(msg);
        
        public static void TriggerDamageDealt(Vector3 pos, float dmg, bool crit) => OnDamageDealt?.Invoke(pos, dmg, crit);
        public static void TriggerHealReceived(Vector3 pos, float amount)        => OnHealReceived?.Invoke(pos, amount);
    }
}
