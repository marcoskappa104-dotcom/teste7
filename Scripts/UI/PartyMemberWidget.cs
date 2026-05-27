using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPG.Network;

namespace RPG.UI
{
    public class PartyMemberWidget : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Slider   hpBar;
        [SerializeField] private Image    leaderIcon;

        public uint MemberNetId { get; private set; }

        public void UpdateInfo(PartyMemberData data)
        {
            MemberNetId = data.NetId;
            if (nameText != null) nameText.text = data.Name;
            if (levelText != null) levelText.text = $"Lv {data.Level}";
            if (hpBar != null) hpBar.value = data.HpPercent;
            if (leaderIcon != null) leaderIcon.enabled = data.IsLeader;
        }
    }
}
