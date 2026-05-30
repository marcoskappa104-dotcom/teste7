using System.Collections.Generic;
using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Biblioteca de efeitos: associa um id (string) a um ProceduralEffect.
    /// O SkillData guarda só o id (CastVfxId); o cliente resolve aqui qual
    /// efeito tocar. Assim cada skill tem seu visual sem aumentar o tráfego de
    /// rede (a rede manda apenas o índice da skill).
    ///
    /// Criar: Create → RPG → VFX Library
    /// Atribuir: no componente NetworkPlayer (campo _vfxLibrary).
    /// </summary>
    [CreateAssetMenu(menuName = "RPG/VFX Library", fileName = "VfxLibrary")]
    public class VfxLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Id único, igual ao CastVfxId da skill. Ex.: 'fogo_impacto'.")]
            public string id;
            public ProceduralEffect effect;
        }

        [Tooltip("Mapeie cada id de efeito ao seu ProceduralEffect.")]
        public List<Entry> entries = new List<Entry>();

        private Dictionary<string, ProceduralEffect> _map;

        private void OnEnable() => Rebuild();

        public void Rebuild()
        {
            _map = new Dictionary<string, ProceduralEffect>(entries.Count);
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.id) && e.effect != null)
                    _map[e.id] = e.effect;
        }

        public ProceduralEffect Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_map == null) Rebuild();
            return _map.TryGetValue(id, out var fx) ? fx : null;
        }
    }
}
