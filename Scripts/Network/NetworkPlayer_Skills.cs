using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using RPG.Data;
using RPG.Combat;
using RPG.Feedback;

namespace RPG.Network
{
    /// <summary>
    /// Execução server-authoritative de skills, dirigida por SkillAimMode.
    ///
    /// Fluxo: cliente manda CmdUseSkill(info) → servidor valida (MP, cooldown,
    /// range por modo, sanitização) → aplica dano conforme o modo → confirma/rejeita.
    ///
    /// Substitui o caminho antigo (Cmd na entidade do monstro + self-skill separado).
    /// Mantém anti-cheat rígido: o cliente só sugere a mira; o servidor decide tudo.
    /// </summary>
    public partial class NetworkPlayer
    {
        // Prefab do projétil de skillshot (opcional). Atribua no prefab do player.
        // IMPORTANTE: o mesmo prefab precisa estar em RPGNetworkManager.spawnablePrefabs
        // para ser registrado na rede. Se ficar NULL, o skillshot usa sweep instantâneo.
        [SerializeField] private GameObject _skillProjectilePrefab;

        [Header("VFX por skill (recomendado)")]
        [Tooltip("Biblioteca id→efeito. O CastVfxId de cada skill é resolvido aqui.")]
        [SerializeField] private RPG.Feedback.VfxLibrary _vfxLibrary;

        [Header("Efeitos padrão por modo (fallback; vazio = SkillFx simples)")]
        [SerializeField] private RPG.Feedback.ProceduralEffect _skillshotFx;
        [SerializeField] private RPG.Feedback.ProceduralEffect _groundTelegraphFx;
        [SerializeField] private RPG.Feedback.ProceduralEffect _groundImpactFx;

        // Tolerância de range para compensar latência (cliente otimista).
        private const float SKILL_RANGE_TOLERANCE = 1.15f;

        // Espessura da linha do skillshot (sweep).
        private const float SKILLSHOT_PROBE_RADIUS = 0.5f;

        private static int _skillTargetableMask = -1;
        private static readonly Collider[] _skillAoeBuffer = new Collider[48];
        private static readonly RaycastHit[] _skillLineBuffer = new RaycastHit[48];

        private static int SkillTargetableMask
        {
            get
            {
                if (_skillTargetableMask == -1)
                    _skillTargetableMask = LayerMask.GetMask("Targetable");
                return _skillTargetableMask;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Comando único de uso de skill
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdUseSkill(SkillCastInfo info)
        {
            if (connectionToClient == null) return;
            if (Dead || _serverStats == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[CmdUseSkill] abortado no servidor: Dead={Dead}, _serverStats={( _serverStats==null ? "NULL" : "ok")}");
#endif
                return;
            }
            if (!info.IsFinite())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[CmdUseSkill] abortado no servidor: SkillCastInfo não-finito.");
#endif
                return;
            }

            int index = info.SkillIndex;
            if (index < 0 || index >= NetworkInventory.GEM_SLOT_COUNT) return;

            var skill = _inventory?.GetEquippedSkill(index);
            if (skill == null)
            {
                RpcSkillRejected(index, "Nenhuma joia equipada neste slot.");
                return;
            }

            // 1) Cooldown (autoritativo)
            if (!ServerCheckAndSetCooldown(index, skill.Cooldown))
            {
                if (_cooldowns != null && _cooldowns.TryGetSkillEndTime(index, out float end))
                    RpcSkillRejected(index, $"{skill.Name}: aguarde {Mathf.Max(0f, end - Time.time):0.0}s");
                return;
            }

            // 2) Mana
            if (CurrentMP < skill.ManaCost)
            {
                RpcSkillRejected(index, "MP insuficiente!");
                // devolve o cooldown setado? Não — manter simples: o cliente revalida MP antes.
                return;
            }

            // 3) Dispatch por modo. Retorna false se a mira falhar a validação.
            bool ok = skill.AimMode switch
            {
                SkillAimMode.SelfCast     => ServerSkill_Self(skill),
                SkillAimMode.AroundSelf   => ServerSkill_AroundSelf(skill),
                SkillAimMode.TargetEnemy  => ServerSkill_TargetEnemy(skill, info),
                SkillAimMode.Skillshot    => ServerSkill_Skillshot(skill, info),
                SkillAimMode.GroundTarget => ServerSkill_GroundTarget(skill, info),
                _                         => false
            };

            if (!ok)
            {
                RpcSkillRejected(index, "Alvo/mira inválidos.");
                return;
            }

            ServerConsumeMP(skill.ManaCost);

            if (!string.IsNullOrEmpty(skill.AnimTrigger))
                RpcPlayAnimation(skill.AnimTrigger);

            RpcSkillConfirmed(index, skill.Cooldown);
        }

        // ══════════════════════════════════════════════════════════════════
        // SelfCast — cura / buff no próprio player
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private bool ServerSkill_Self(SkillData skill)
        {
            if (skill.Type == SkillType.Heal)
            {
                float heal = Mathf.Max(10f, _serverStats.MATK * skill.AtkMultiplier);
                ServerApplyHeal(heal);
            }
            else if (skill.Type == SkillType.Buff)
            {
                // Placeholder: aplique seu BuffSystem aqui quando existir.
            }
            RpcSelfVisual(transform.position, 1.5f, skill.CastVfxId);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // AroundSelf — nova/tornado: AoE centrada no player
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private bool ServerSkill_AroundSelf(SkillData skill)
        {
            float radius = Mathf.Max(0.5f, skill.AoERadius);
            ServerApplyAoEDamage(transform.position, radius, skill, skill.MaxTargets);
            RpcSelfVisual(transform.position, radius, skill.CastVfxId);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // TargetEnemy — homing: dano direto no monstro mirado
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private bool ServerSkill_TargetEnemy(SkillData skill, SkillCastInfo info)
        {
            var monster = NetworkLookup.FindMonster(info.TargetNetId);
            if (monster == null || monster.IsDead) return false;

            float dist = Vector3.Distance(transform.position, monster.transform.position);
            if (dist > skill.Range * SKILL_RANGE_TOLERANCE)
            {
                RpcShowMessageToOwner("Alvo fora de alcance.");
                return false;
            }

            var combat = monster.GetComponent<MonsterCombat>();
            if (combat == null) return false;

            // Single target + (opcional) splash ao redor do alvo.
            combat.ServerTakeDamageFromPlayer(this, _serverStats, skill.IsPhysical, skill);

            if (skill.IsAoE)
                ServerApplyAoEDamage(monster.transform.position, skill.AoERadius, skill,
                                     skill.MaxTargets, ignore: monster);

            RpcGroundImpact(monster.transform.position, Mathf.Max(0.6f, skill.AoERadius), skill.CastVfxId);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // Skillshot — projétil/linha na direção do cursor
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private bool ServerSkill_Skillshot(SkillData skill, SkillCastInfo info)
        {
            Vector3 dir = info.AimDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
            dir.Normalize();

            if (skill.IsBeam)
                return ServerSkillshot_Beam(skill, dir, info.SkillIndex);
            return ServerSkillshot_Projectile(skill, dir);
        }

        // ── BEAM / LASER (instantâneo em linha reta) ───────────────────────
        // ── Estado do canal de beam (servidor) ─────────────────────────────
        private Vector3 _beamDir;             // direção corrente do feixe (atualizada pelo cliente)
        private bool    _beamChanneling;      // canal ativo?
        private int     _beamSkillIndex = -1; // qual skill está canalizando
        private const float BEAM_AIM_TOLERANCE = 0.001f;

        [Server]
        private bool ServerSkillshot_Beam(SkillData skill, Vector3 dir, int skillIndex)
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f;

            if (skill.IsSustainedBeam)
            {
                // Inicia o canal. A direção pode ser atualizada via CmdUpdateBeamAim
                // enquanto o jogador segura a tecla (laser que segue o cursor).
                _beamDir        = dir;
                _beamChanneling = true;
                _beamSkillIndex = skillIndex;
                StartCoroutine(SustainedBeamRoutine(skill));
            }
            else
            {
                // Pulso instantâneo: um único dano em linha.
                ApplyBeamDamage(skill, origin, dir);
                RpcBeamVisual(origin, dir, skill.Range, 0f, skill.CastVfxId);
            }
            return true;
        }

        [Server]
        private System.Collections.IEnumerator SustainedBeamRoutine(SkillData skill)
        {
            float elapsed = 0f;
            float tick = Mathf.Max(0.05f, skill.BeamTickInterval);

            // Avisa os clientes para começarem a desenhar o feixe persistente,
            // que vão atualizar pela direção replicada (RpcBeamAimUpdate).
            RpcBeamBegin(skill.Range, skill.CastVfxId);

            while (elapsed < skill.BeamDuration && !Dead && _beamChanneling)
            {
                Vector3 origin = transform.position + Vector3.up * 1.0f;
                ApplyBeamDamage(skill, origin, _beamDir);
                yield return new WaitForSeconds(tick);
                elapsed += tick;
            }

            _beamChanneling = false;
            _beamSkillIndex = -1;
            RpcBeamEnd();
        }

        /// <summary>Cliente envia a direção atual do cursor durante o canal do beam.</summary>
        [Command]
        public void CmdUpdateBeamAim(Vector3 direction)
        {
            if (!_beamChanneling) return;

            direction.y = 0f;
            if (!IsFiniteVec(direction) || direction.sqrMagnitude < BEAM_AIM_TOLERANCE) return;
            _beamDir = direction.normalized;

            // Replica a direção para os clientes atualizarem o visual do feixe.
            RpcBeamAimUpdate(_beamDir);
        }

        /// <summary>Cliente pede para encerrar o canal (soltou a tecla).</summary>
        [Command]
        public void CmdEndBeam()
        {
            _beamChanneling = false;
        }

        private static bool IsFiniteVec(Vector3 v)
            => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
              || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        [Server]
        private void ApplyBeamDamage(SkillData skill, Vector3 origin, Vector3 dir)
        {
            float thickness = Mathf.Max(0.05f, skill.BeamThickness);
            int hits = Physics.SphereCastNonAlloc(
                origin, thickness, dir, _skillLineBuffer, skill.Range, SkillTargetableMask);

            System.Array.Sort(_skillLineBuffer, 0, hits,
                Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));

            int maxHits = skill.MaxTargets > 0
                ? skill.MaxTargets
                : (skill.PierceCount > 0 ? skill.PierceCount + 1 : int.MaxValue);
            int applied = 0;

            for (int i = 0; i < hits && applied < maxHits; i++)
            {
                var monster = _skillLineBuffer[i].collider.GetComponentInParent<NetworkMonsterEntity>();
                if (monster == null || monster.IsDead) continue;
                var combat = monster.GetComponent<MonsterCombat>();
                if (combat == null) continue;

                combat.ServerTakeDamageFromPlayer(this, _serverStats, skill.IsPhysical, skill);
                applied++;
            }
        }

        // ── PROJÉTIL (viaja e some no fim do range) ────────────────────────
        [Server]
        private bool ServerSkillshot_Projectile(SkillData skill, Vector3 dir)
        {
            // Projétil viajante de verdade (se houver prefab configurado).
            if (_skillProjectilePrefab != null)
            {
                SpawnSkillProjectiles(skill, dir, _skillProjectilePrefab);
                RpcSkillshotVisual(transform.position + Vector3.up * 1.0f, dir, skill.Range, skill.CastVfxId);
                return true;
            }

            // Fallback sem prefab: sweep instantâneo em linha (hitscan), respeitando Range.
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            float probe = Mathf.Max(0.1f, skill.ProjectileRadius);
            int hits = Physics.SphereCastNonAlloc(
                origin, probe, dir, _skillLineBuffer, skill.Range, SkillTargetableMask);

            System.Array.Sort(_skillLineBuffer, 0, hits,
                Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));

            int maxHits = skill.PierceCount + 1;
            int applied = 0;
            Vector3 firstHitPoint = origin + dir * skill.Range;
            bool gotFirst = false;

            for (int i = 0; i < hits && applied < maxHits; i++)
            {
                var monster = _skillLineBuffer[i].collider.GetComponentInParent<NetworkMonsterEntity>();
                if (monster == null || monster.IsDead) continue;
                var combat = monster.GetComponent<MonsterCombat>();
                if (combat == null) continue;

                if (!gotFirst) { firstHitPoint = monster.transform.position; gotFirst = true; }

                combat.ServerTakeDamageFromPlayer(this, _serverStats, skill.IsPhysical, skill);
                applied++;
            }

            if (skill.IsAoE && gotFirst)
                ServerApplyAoEDamage(firstHitPoint, skill.AoERadius, skill, skill.MaxTargets);

            RpcSkillshotVisual(origin, dir, skill.Range, skill.CastVfxId);
            return true;
        }

        [Server]
        private void SpawnSkillProjectiles(SkillData skill, Vector3 baseDir, GameObject prefab)
        {
            int count = Mathf.Max(1, skill.ProjectileCount);
            float spread = skill.SpreadAngle;
            Vector3 origin = transform.position + Vector3.up * 1.0f;

            for (int i = 0; i < count; i++)
            {
                float angle = 0f;
                if (count > 1 && spread > 0f)
                    angle = -spread * 0.5f + (spread / (count - 1)) * i;

                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * baseDir;
                Quaternion rot = Quaternion.LookRotation(dir);

                var go   = Instantiate(prefab, origin + dir * 0.5f, rot);
                var proj = go.GetComponent<Projectile>();
                if (proj == null)
                {
                    Debug.LogWarning("[Skillshot] Prefab de projétil SEM componente Projectile — destruindo. " +
                                     "Adicione o script Projectile (e NetworkIdentity) ao prefab.");
                    Destroy(go); continue;
                }

                // Dano pré-calculado (consistente com o caminho de armas).
                float dmg = ComputeSkillDamage(skill, out bool crit);
                NetworkServer.Spawn(go);
                proj.ServerInitializeSkillshot(
                    netId, dir, skill.ProjectileSpeed, dmg, crit,
                    skill.Range, skill.PierceCount, skill.AoERadius, skill.IsPhysical,
                    skill.ProjectileRadius);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Skillshot] Spawn projétil dir={dir} speed={skill.ProjectileSpeed} range={skill.Range}");
#endif
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GroundTarget — meteoro: AoE no ponto do cursor (com telegraph)
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private bool ServerSkill_GroundTarget(SkillData skill, SkillCastInfo info)
        {
            Vector3 point = info.AimPoint;

            // Anti-cheat: ponto não pode estar além do alcance do player.
            Vector3 flat = point - transform.position; flat.y = 0f;
            if (flat.magnitude > skill.Range * SKILL_RANGE_TOLERANCE)
            {
                RpcShowMessageToOwner("Mira fora de alcance.");
                return false;
            }

            if (skill.ImpactDelay > 0.01f)
            {
                RpcGroundTelegraph(point, Mathf.Max(0.5f, skill.AoERadius), skill.ImpactDelay, skill.CastVfxId);
                StartCoroutine(GroundImpactDelayed(point, skill));
            }
            else
            {
                ServerApplyAoEDamage(point, Mathf.Max(0.5f, skill.AoERadius), skill, skill.MaxTargets);
                RpcGroundImpact(point, Mathf.Max(0.5f, skill.AoERadius), skill.CastVfxId);
            }
            return true;
        }

        [Server]
        private IEnumerator GroundImpactDelayed(Vector3 point, SkillData skill)
        {
            yield return new WaitForSeconds(skill.ImpactDelay);
            if (this == null || !isServer) yield break;
            ServerApplyAoEDamage(point, Mathf.Max(0.5f, skill.AoERadius), skill, skill.MaxTargets);
            RpcGroundImpact(point, Mathf.Max(0.5f, skill.AoERadius), skill.CastVfxId);
        }

        // ══════════════════════════════════════════════════════════════════
        // Helper central de AoE — atinge todos os monstros num raio
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private void ServerApplyAoEDamage(Vector3 center, float radius, SkillData skill,
                                          int maxTargets, NetworkMonsterEntity ignore = null)
        {
            int count = Physics.OverlapSphereNonAlloc(
                center, radius, _skillAoeBuffer, SkillTargetableMask);

            int applied = 0;
            for (int i = 0; i < count; i++)
            {
                if (_skillAoeBuffer[i] == null) continue;

                var monster = _skillAoeBuffer[i].GetComponentInParent<NetworkMonsterEntity>();
                if (monster == null || monster.IsDead || monster == ignore) continue;

                var combat = monster.GetComponent<MonsterCombat>();
                if (combat == null) continue;

                combat.ServerTakeDamageFromPlayer(this, _serverStats, skill.IsPhysical, skill);
                applied++;

                if (maxTargets > 0 && applied >= maxTargets) break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Cálculo de dano de skill (usado quando precisamos pré-computar)
        // ══════════════════════════════════════════════════════════════════

        [Server]
        private float ComputeSkillDamage(SkillData skill, out bool crit)
        {
            crit = StatsCalculator.RollCrit(_serverStats.CRIT);
            float baseAtk = skill.IsPhysical
                ? _serverStats.ATK * skill.AtkMultiplier
                : _serverStats.MATK * skill.AtkMultiplier;

            // Sem DEF do alvo aqui (alvo desconhecido até o impacto): o projétil
            // aplica via MonsterCombat.ServerTakeProjectileDamage, que NÃO reaplica DEF.
            // Para manter consistência, fazemos uma redução média conservadora = 0.
            float dmg = Mathf.Max(1f, baseAtk);
            if (crit) dmg *= _serverStats.CritDMG;
            return Mathf.Floor(dmg);
        }

        // ══════════════════════════════════════════════════════════════════
        // RPCs visuais (cosméticos — hooke VFX aqui)
        // ══════════════════════════════════════════════════════════════════

        // Resolve o efeito: 1) id da skill na biblioteca; 2) fallback padrão por modo.
        private RPG.Feedback.ProceduralEffect ResolveVfx(string vfxId, RPG.Feedback.ProceduralEffect fallback)
        {
            if (_vfxLibrary != null && !string.IsNullOrEmpty(vfxId))
            {
                var fx = _vfxLibrary.Get(vfxId);
                if (fx != null) return fx;
            }
            return fallback;
        }

        [ClientRpc]
        private void RpcSkillshotVisual(Vector3 origin, Vector3 dir, float range, string vfxId)
        {
            if (Application.isBatchMode) return;
            var fx = ResolveVfx(vfxId, _skillshotFx);
            if (fx != null) RPG.Feedback.ProceduralFx.Play(fx, origin, dir);
            else            SkillFx.SkillshotTrail(origin, dir, range);
        }

        [ClientRpc]
        private void RpcBeamVisual(Vector3 origin, Vector3 dir, float range, float duration, string vfxId)
        {
            // Usado apenas pelo beam de PULSO (duration 0). O sustentado usa Begin/Update/End.
            if (Application.isBatchMode) return;
            var fx = ResolveVfx(vfxId, _skillshotFx);
            if (fx != null) RPG.Feedback.ProceduralFx.Play(fx, origin, dir, range);
            else            SkillFx.SkillshotTrail(origin, dir, range);
            var mgr = CombatFeedbackManager.Instance;
            if (mgr != null) mgr.Shake(0.10f);
        }

        // ── Feixe sustentado: um único visual persistente que gira ──────────
        private RPG.Feedback.SustainedBeamView _beamView;

        [ClientRpc]
        private void RpcBeamBegin(float range, string vfxId)
        {
            if (Application.isBatchMode) return;
            if (_beamView == null)
                _beamView = RPG.Feedback.SustainedBeamView.Create();
            Color c = new Color(0.55f, 0.85f, 1f, 1f);
            _beamView.Begin(transform, Vector3.up * 1.0f, transform.forward, range, c);
        }

        [ClientRpc]
        private void RpcBeamAimUpdate(Vector3 dir)
        {
            if (Application.isBatchMode || _beamView == null) return;
            _beamView.SetDirection(dir);
        }

        [ClientRpc]
        private void RpcBeamEnd()
        {
            if (Application.isBatchMode || _beamView == null) return;
            _beamView.End();
        }

        [ClientRpc]
        private void RpcGroundTelegraph(Vector3 point, float radius, float delay, string vfxId)
        {
            if (Application.isBatchMode) return;
            // O telegraph usa o efeito dedicado de aviso (não o id da skill, que é o impacto).
            if (_groundTelegraphFx != null) RPG.Feedback.ProceduralFx.Play(_groundTelegraphFx, point, Vector3.forward, radius);
            else                            SkillFx.GroundTelegraph(point, radius, delay);
        }

        [ClientRpc]
        private void RpcGroundImpact(Vector3 point, float radius, string vfxId)
        {
            if (Application.isBatchMode) return;
            var fx = ResolveVfx(vfxId, _groundImpactFx);
            if (fx != null) RPG.Feedback.ProceduralFx.Play(fx, point, Vector3.forward, radius);
            else            SkillFx.GroundImpact(point, radius);

            // Tremor proporcional ao tamanho da área; só sacode se a câmera estiver perto.
            var cam = Camera.main;
            var mgr = CombatFeedbackManager.Instance;
            if (cam != null && mgr != null)
            {
                float d = Vector3.Distance(cam.transform.position, point);
                if (d <= 25f)
                    mgr.Shake(Mathf.Clamp(0.10f + radius * 0.03f, 0.1f, 0.45f) * (1f - d / 25f));
            }
        }

        // Visual para AroundSelf / SelfCast (cura, buff, nova). Chame do servidor.
        [ClientRpc]
        private void RpcSelfVisual(Vector3 point, float radius, string vfxId)
        {
            if (Application.isBatchMode) return;
            var fx = ResolveVfx(vfxId, null);
            if (fx != null) RPG.Feedback.ProceduralFx.Play(fx, point, Vector3.forward, Mathf.Max(1f, radius));
            else            SkillFx.GroundImpact(point, Mathf.Max(1f, radius));
        }
    }
}
