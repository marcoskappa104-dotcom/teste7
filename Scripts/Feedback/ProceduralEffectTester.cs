using UnityEngine;

namespace RPG.Feedback
{
    /// <summary>
    /// Testador de efeitos procedurais. Arraste em qualquer GameObject, atribua um
    /// ProceduralEffect e dispare:
    ///   • apertando a tecla configurada (Play Mode),
    ///   • pelo botão "Play Now" no menu de contexto do componente,
    ///   • automaticamente em intervalos (autoLoop).
    ///
    /// Some este componente do build final — é só para iteração no Editor.
    /// </summary>
    public class ProceduralEffectTester : MonoBehaviour
    {
        [Tooltip("Efeito a testar.")]
        public ProceduralEffect effect;

        [Tooltip("Onde nasce o efeito (vazio = este objeto).")]
        public Transform spawnPoint;

        [Tooltip("Direção do efeito (para feixe/cone).")]
        public Vector3 direction = Vector3.forward;

        [Header("Disparo")]
        public KeyCode key = KeyCode.T;

        [Tooltip("Se ligado, segue o objeto (aura/trail) em vez de soltar no ponto.")]
        public bool attachToObject = false;

        [Header("Auto")]
        public bool  autoLoop = false;
        [Min(0.1f)] public float interval = 1.5f;

        private float _next;

        private void Update()
        {
            if (Input.GetKeyDown(key)) PlayNow();

            if (autoLoop && Time.time >= _next)
            {
                _next = Time.time + interval;
                PlayNow();
            }
        }

        [ContextMenu("Play Now")]
        public void PlayNow()
        {
            if (effect == null) { Debug.LogWarning("[FX Tester] Nenhum efeito atribuído."); return; }
            Transform t = spawnPoint != null ? spawnPoint : transform;

            if (attachToObject) ProceduralFx.PlayAttached(effect, t);
            else                ProceduralFx.Play(effect, t.position, t.TransformDirection(direction));
        }
    }
}
