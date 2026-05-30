using UnityEngine;
using Mirror;

namespace RPG.Network
{
    /// <summary>
    /// Payload de mira enviado pelo cliente ao usar uma skill.
    /// Mirror serializa structs automaticamente. Mantenha leve.
    ///
    /// O servidor NUNCA confia nestes valores cegamente — valida range,
    /// NaN/Infinity e linha de visão antes de aplicar dano.
    /// </summary>
    public struct SkillCastInfo : NetworkMessage
    {
        public int     SkillIndex;
        public uint    TargetNetId;   // usado em TargetEnemy (0 se nenhum)
        public Vector3 AimPoint;      // ponto no chão (GroundTarget) ou ponto-alvo (Skillshot)
        public Vector3 AimDirection;  // direção normalizada (Skillshot)

        public static SkillCastInfo ForTarget(int index, uint targetNetId)
            => new SkillCastInfo { SkillIndex = index, TargetNetId = targetNetId };

        public static SkillCastInfo ForGround(int index, Vector3 point)
            => new SkillCastInfo { SkillIndex = index, AimPoint = point };

        public static SkillCastInfo ForDirection(int index, Vector3 origin, Vector3 dir)
            => new SkillCastInfo { SkillIndex = index, AimPoint = origin, AimDirection = dir };

        public static SkillCastInfo Self(int index)
            => new SkillCastInfo { SkillIndex = index };

        public bool IsFinite()
        {
            return IsVecFinite(AimPoint) && IsVecFinite(AimDirection);
        }

        private static bool IsVecFinite(Vector3 v)
            => !(float.IsNaN(v.x) || float.IsInfinity(v.x)
              || float.IsNaN(v.y) || float.IsInfinity(v.y)
              || float.IsNaN(v.z) || float.IsInfinity(v.z));
    }
}
