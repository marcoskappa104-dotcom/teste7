using System.Collections.Generic;
using UnityEngine;

namespace RPG.Data
{
    [System.Serializable]
    public class LootPoolEntry
    {
        public ItemData Item;
        [Range(1, 1000)]
        public int Weight = 100;
        
        public int MinQuantity = 1;
        public int MaxQuantity = 1;

        [Tooltip("Chance individual extra (0-100). Se for 100, depende apenas do peso do pool.")]
        [Range(0f, 100f)]
        public float IndividualChance = 100f;
    }

    [System.Serializable]
    public class LootPool
    {
        public string PoolName = "Novo Pool";
        
        [Tooltip("Chance de este pool inteiro ser processado.")]
        [Range(0f, 100f)]
        public float PoolChance = 100f;

        [Tooltip("Quantos itens deste pool serão sorteados (se o pool passar no teste de chance).")]
        public int RollCount = 1;

        [Tooltip("Se true, o mesmo item pode ser sorteado múltiplas vezes.")]
        public bool AllowDuplicates = false;

        public List<LootPoolEntry> Entries = new List<LootPoolEntry>();

        public List<(string itemId, int quantity)> Roll()
        {
            var results = new List<(string, int)>();

            if (Random.Range(0f, 100f) > PoolChance) return results;
            if (Entries.Count == 0) return results;

            // FIX: Copiamos as entradas se não permitirmos duplicatas para poder remover
            List<LootPoolEntry> pool = AllowDuplicates ? Entries : new List<LootPoolEntry>(Entries);

            for (int i = 0; i < RollCount; i++)
            {
                if (pool.Count == 0) break;

                int currentTotalWeight = 0;
                foreach (var e in pool) currentTotalWeight += e.Weight;
                if (currentTotalWeight <= 0) break;

                int roll = Random.Range(0, currentTotalWeight);
                int acc = 0;
                
                LootPoolEntry selected = null;
                foreach (var e in pool)
                {
                    acc += e.Weight;
                    if (roll < acc)
                    {
                        selected = e;
                        // Teste de chance individual
                        if (Random.Range(0f, 100f) <= e.IndividualChance)
                        {
                            int qty = Random.Range(e.MinQuantity, e.MaxQuantity + 1);
                            results.Add((e.Item.ItemId, qty));
                        }
                        break;
                    }
                }

                if (selected != null && !AllowDuplicates)
                {
                    pool.Remove(selected);
                }
            }

            return results;
        }
    }

    [CreateAssetMenu(menuName = "RPG/Loot Table", fileName = "LootTable_New")]
    public class LootTable : ScriptableObject
    {
        public List<LootPool> Pools = new List<LootPool>();

        public List<(string itemId, int quantity)> GetDrops()
        {
            var allDrops = new List<(string, int)>();
            foreach (var pool in Pools)
            {
                allDrops.AddRange(pool.Roll());
            }
            return allDrops;
        }
    }
}
