using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace RPG.UI
{

    public class FloatingTextManager : MonoBehaviour
    {
        public static FloatingTextManager Instance { get; private set; }

        [SerializeField] private GameObject floatingTextPrefab;
        [SerializeField] private int        poolSize  = 20;
        [SerializeField] private float      riseSpeed = 2f;
        [SerializeField] private float      lifetime  = 1.2f;

        /// <summary>
        /// Entrada do pool com componentes pré-cacheados.
        /// </summary>
        private struct PoolEntry
        {
            public GameObject  Obj;
            public TextMeshPro Tmp;
        }

        private readonly Queue<PoolEntry> _pool = new Queue<PoolEntry>();
        private Camera                    _cachedCamera;
        private bool                      _isServerOnly;

        // FIX: Pool de objetos ActiveText para evitar coroutines por cada hit
        private class ActiveText
        {
            public PoolEntry Entry;
            public Vector3   StartPos;
            public float     Elapsed;
            public Camera    Cam;
        }
        private readonly List<ActiveText> _activeTexts = new List<ActiveText>();
        private readonly Queue<ActiveText> _activeTextPool = new Queue<ActiveText>();

        // Agrupamento de dano
        private struct DamageGroupKey
        {
            public Vector3 Pos;
            public Color   Col;
            public override int GetHashCode() => Pos.GetHashCode() ^ Col.GetHashCode();
            public override bool Equals(object obj) => obj is DamageGroupKey other && Pos == other.Pos && Col == other.Col;
        }
        private class DamageGroup
        {
            public float  Value;
            public float  TimeRemaining;
            public string Prefix = "";
        }
        private readonly Dictionary<DamageGroupKey, DamageGroup> _damageGroups = new Dictionary<DamageGroupKey, DamageGroup>();
        private const float GROUP_WINDOW = 0.2f; // 200ms

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (Application.isBatchMode)
            {
                _isServerOnly = true;
                Debug.Log("[FloatingTextManager] Servidor dedicado — UI desabilitada.");
                return;
            }

            PrewarmPool();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (!_isServerOnly)
                SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            if (_isServerOnly) return;
            _cachedCamera = Camera.main;
        }

        private void Update()
        {
            if (_isServerOnly) return;

            float dt = Time.deltaTime;

            // Processa grupos de dano
            if (_damageGroups.Count > 0)
            {
                var keys = new List<DamageGroupKey>(_damageGroups.Keys);
                foreach (var key in keys)
                {
                    var group = _damageGroups[key];
                    group.TimeRemaining -= dt;
                    if (group.TimeRemaining <= 0)
                    {
                        ShowImmediate($"{group.Prefix}{Mathf.RoundToInt(group.Value)}", key.Pos, key.Col);
                        _damageGroups.Remove(key);
                    }
                }
            }

            if (_activeTexts.Count == 0) return;

            for (int i = _activeTexts.Count - 1; i >= 0; i--)
            {
                var at = _activeTexts[i];
                at.Elapsed += dt;
                float t = at.Elapsed / lifetime;

                if (t >= 1f)
                {
                    at.Entry.Obj.SetActive(false);
                    _pool.Enqueue(at.Entry);
                    _activeTexts.RemoveAt(i);
                    _activeTextPool.Enqueue(at);
                    continue;
                }

                var obj = at.Entry.Obj;
                obj.transform.position = at.StartPos + Vector3.up * (riseSpeed * t);

                var tmp = at.Entry.Tmp;
                if (tmp != null)
                {
                    var c = tmp.color;
                    c.a = 1f - (t * t);
                    tmp.color = c;
                }

                if (at.Cam != null)
                {
                    Vector3 dir = obj.transform.position - at.Cam.transform.position;
                    if (dir.sqrMagnitude > 0.001f)
                        obj.transform.forward = dir.normalized;
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A câmera pode mudar entre cenas
            _cachedCamera = Camera.main;
        }

        private PoolEntry CreateEntry()
        {
            var obj = Instantiate(floatingTextPrefab, transform);
            obj.SetActive(false);
            var tmp = obj.GetComponent<TextMeshPro>()
                   ?? obj.GetComponentInChildren<TextMeshPro>();
            return new PoolEntry { Obj = obj, Tmp = tmp };
        }

        private void PrewarmPool()
        {
            if (floatingTextPrefab == null)
            {
                Debug.LogWarning("[FloatingTextManager] floatingTextPrefab não configurado.");
                return;
            }

            int size = Mathf.Max(poolSize, 1);
            for (int i = 0; i < size; i++)
            {
                _pool.Enqueue(CreateEntry());
                _activeTextPool.Enqueue(new ActiveText());
            }
        }

        public void Show(string text, Vector3 worldPos, Color color)
        {
            if (_isServerOnly || Application.isBatchMode) return;

            // Tenta agrupar se for um número (dano/cura)
            if (float.TryParse(text, out float val))
            {
                // Arredonda a posição para agrupar hits no mesmo alvo
                Vector3 snappedPos = new Vector3(
                    Mathf.Round(worldPos.x * 10f) / 10f,
                    Mathf.Round(worldPos.y * 10f) / 10f,
                    Mathf.Round(worldPos.z * 10f) / 10f
                );

                var key = new DamageGroupKey { Pos = snappedPos, Col = color };
                if (!_damageGroups.TryGetValue(key, out var group))
                {
                    group = new DamageGroup { Value = val, TimeRemaining = GROUP_WINDOW };
                    _damageGroups[key] = group;
                }
                else
                {
                    group.Value += val;
                    group.TimeRemaining = GROUP_WINDOW; // Reset window
                }
                return;
            }

            ShowImmediate(text, worldPos, color);
        }

        private void ShowImmediate(string text, Vector3 worldPos, Color color)
        {
            if (floatingTextPrefab == null) return;
            if (_cachedCamera == null) _cachedCamera = Camera.main;

            PoolEntry entry = _pool.Count > 0 ? _pool.Dequeue() : CreateEntry();
            ActiveText at = _activeTextPool.Count > 0 ? _activeTextPool.Dequeue() : new ActiveText();

            var obj = entry.Obj;
            obj.transform.position = worldPos + new Vector3(Random.Range(-0.3f, 0.3f), 0f, 0f);
            obj.SetActive(true);

            if (entry.Tmp != null)
            {
                entry.Tmp.text = text;
                entry.Tmp.color = color;
            }

            at.Entry = entry;
            at.StartPos = obj.transform.position;
            at.Elapsed = 0f;
            at.Cam = _cachedCamera;

            _activeTexts.Add(at);
        }
    }
}