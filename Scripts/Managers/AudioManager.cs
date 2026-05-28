using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace RPG.Managers
{
    /// <summary>
    /// Gerenciador central de áudio. Suporta pooling básico de AudioSources
    /// e categorias (Sfx, Music, UI).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private AudioMixer _mainMixer;
        [SerializeField] private int        _poolSize = 10;

        [Header("Standard Clips")]
        public AudioClip ClickSfx;
        public AudioClip LevelUpSfx;
        public AudioClip HitSfx;
        public AudioClip DeathSfx;
        public AudioClip PickupSfx;

        private readonly Queue<AudioSource> _sourcePool = new Queue<AudioSource>();
        private AudioSource _musicSource;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePool();
        }

        private void InitializePool()
        {
            // Fonte para música (loop)
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;

            // Pool para SFX (one-shots)
            for (int i = 0; i < _poolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                _sourcePool.Enqueue(source);
            }
        }

        public void PlaySfx(AudioClip clip, float volume = 1.0f, float pitch = 1.0f)
        {
            if (clip == null) return;

            if (_sourcePool.Count == 0)
            {
                // Se o pool esvaziou, tentamos pegar um que já terminou de tocar
                // mas que ainda não foi devolvido (fallback de segurança)
                Debug.LogWarning("[AudioManager] Pool de áudio esgotado. Considere aumentar _poolSize.");
                return;
            }

            var source = _sourcePool.Dequeue();
            source.clip   = clip;
            source.volume = volume;
            source.pitch  = pitch;
            source.Play();

            StartCoroutine(ReturnToPool(source, clip.length / Mathf.Abs(pitch)));
        }

        private System.Collections.IEnumerator ReturnToPool(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);
            _sourcePool.Enqueue(source);
        }

        public void PlayMusic(AudioClip clip, float volume = 0.5f)
        {
            if (clip == null || _musicSource.clip == clip) return;

            _musicSource.clip   = clip;
            _musicSource.volume = volume;
            _musicSource.Play();
        }

        // --- Helpers Rápidos ---
        public void PlayClick()   => PlaySfx(ClickSfx, 0.6f);
        public void PlayHit()     => PlaySfx(HitSfx, 0.8f, Random.Range(0.9f, 1.1f));
        public void PlayLevelUp() => PlaySfx(LevelUpSfx, 1.0f);
        public void PlayPickup()  => PlaySfx(PickupSfx, 0.7f);
    }
}
