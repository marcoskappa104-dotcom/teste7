using UnityEngine;
using Mirror;
using System.Collections.Generic;

namespace RPG.Network
{
    // ══════════════════════════════════════════════════════════════════════
    // SNAPSHOTS & DTOs
    // Centralização de estruturas de dados usadas para sincronização ou
    // snapshots de estado entre sistemas.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dados completos de inicialização do jogador enviados via RPC.
    /// </summary>
    public struct PlayerInitData
    {
        public string CharName;
        public int    Race;
        public int    Level;
        public long   Exp;
        public long   ExpToNext;
        public int    FreePoints;
        public int    AllocSTR, AllocAGI, AllocVIT, AllocDEX, AllocINT, AllocLUK;
        public int    BaseSTR,  BaseAGI,  BaseVIT,  BaseDEX,  BaseINT,  BaseLUK;
        public float  CurHP, CurMP;
    }

    /// <summary>
    /// Snapshot de estado para o sistema de regeneração.
    /// </summary>
    public struct RegenSnapshot
    {
        public bool         IsDead;
        public float        CurrentHP, MaxHP;
        public float        CurrentMP, MaxMP;
        public RPG.Data.DerivedStats Stats;
    }

    /// <summary>
    /// Snapshot de interação com NPC (loja, quests, diálogos).
    /// </summary>
    public enum NpcQuestOptionState : byte
    {
        Invalid     = 0,
        Offer       = 1,
        InProgress  = 2,
        TurnIn      = 3,
        Locked      = 4,
        AlreadyDone = 5,
    }

    [System.Serializable]
    public struct NpcQuestOption
    {
        public string              QuestId;
        public NpcQuestOptionState State;

        public static NpcQuestOption Invalid(string id)     => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.Invalid };
        public static NpcQuestOption Offer(string id)       => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.Offer };
        public static NpcQuestOption InProgress(string id)  => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.InProgress };
        public static NpcQuestOption TurnIn(string id)      => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.TurnIn };
        public static NpcQuestOption Locked(string id)      => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.Locked };
        public static NpcQuestOption AlreadyDone(string id) => new NpcQuestOption { QuestId = id, State = NpcQuestOptionState.AlreadyDone };
    }

    [System.Serializable]
    public struct NpcInteractionSnapshot
    {
        public string               NpcId;
        public string               DisplayName;
        public string               Role;
        public string               Greeting;
        public List<NpcQuestOption> Options;
    }
}
