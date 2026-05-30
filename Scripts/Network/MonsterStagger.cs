using UnityEngine;
using UnityEngine.AI;
using Mirror;

namespace RPG.Network
{
    /// <summary>
    /// Knockback + stagger do monstro — empurrão e micro-atordoamento ao apanhar.
    /// É o que dá "peso" ao acerto num ARPG.
    ///
    /// SERVER-AUTHORITATIVE: como o movimento do monstro é decidido no servidor,
    /// o empurrão acontece aqui e é replicado pelos clientes pelo SEU sync de
    /// movimento (NetworkTransform ou o sync do seu MonsterAI).
    ///
    /// Integração (2 passos):
    ///   1) Coloque este componente no prefab do monstro.
    ///   2) DOIS hooks no seu código de monstro (servidor):
    ///      a) em MonsterCombat.ServerTakeDamageFromPlayer, depois do dano:
    ///             GetComponent&lt;MonsterStagger&gt;()?.ServerApplyKnockback(
    ///                 atacante.transform.position, force: 2.5f, staggerSeconds: 0.18f);
    ///      b) no topo do tick de movimento/decisão do MonsterAI:
    ///             if (_stagger != null &amp;&amp; _stagger.IsStaggered) return;  // pula este frame
    ///
    /// Sem o hook (b) a IA "luta" contra o empurrão; com ele, o monstro
    /// realmente recua e hesita por uma fração de segundo.
    /// </summary>
    public class MonsterStagger : NetworkBehaviour
    {
        [Header("Limites de segurança")]
        [Tooltip("Distância máxima total de um empurrão (m). Anti-abuso.")]
        [SerializeField] private float _maxKnockbackDistance = 3f;
        [Tooltip("Stagger máximo (s).")]
        [SerializeField] private float _maxStagger = 0.5f;

        /// <summary>True enquanto o monstro está atordoado. A IA deve checar isto.</summary>
        public bool IsStaggered { get; private set; }

        private NavMeshAgent      _agent;
        private CharacterController _cc;

        private Vector3 _remaining;     // deslocamento que falta aplicar (servidor)
        private float   _staggerEndsAt;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _cc    = GetComponent<CharacterController>();
        }

        [Server]
        public void ServerApplyKnockback(Vector3 sourcePos, float force, float staggerSeconds)
        {
            Vector3 dir = transform.position - sourcePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            dir.Normalize();

            float dist = Mathf.Min(_maxKnockbackDistance, Mathf.Max(0f, force));
            _remaining = dir * dist;

            float stagger = Mathf.Clamp(staggerSeconds, 0f, _maxStagger);
            _staggerEndsAt = Time.time + stagger;
            IsStaggered = stagger > 0f;
        }

        [ServerCallback]
        private void Update()
        {
            if (IsStaggered && Time.time >= _staggerEndsAt)
                IsStaggered = false;

            if (_remaining.sqrMagnitude < 0.0001f) return;

            // Consome o empurrão rápido (exponencial) ao longo de poucos frames.
            Vector3 step = _remaining * Mathf.Clamp01(Time.deltaTime * 14f);
            _remaining -= step;
            if (_remaining.sqrMagnitude < 0.0004f) _remaining = Vector3.zero;

            MoveServer(step);
        }

        [Server]
        private void MoveServer(Vector3 delta)
        {
            // Usa o sistema de locomoção que existir, pra não brigar com ele.
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.Move(delta);
            }
            else if (_cc != null && _cc.enabled)
            {
                _cc.Move(delta);
            }
            else
            {
                transform.position += delta;
            }
        }
    }
}
