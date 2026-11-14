using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>其他玩家的 avatar：位置從 <see cref="PoseFeed.Remote"/> 平滑跟上，頭上顯示名字，手上拿著他拿的零件。</summary>
    public sealed class RemoteAvatar : MonoBehaviour
    {
        public int playerId;

        AvatarRig _rig;
        WorldLabel _tag;
        string _lookKey, _heldId;
        Transform _held;

        /// <param name="kit">手上那個零件還沒組裝（拿的是一箱小零件）。</param>
        public void Apply(AvatarLook look, string held, GameData data, bool kit = false)
        {
            look = (look ?? new AvatarLook()).Sanitized();
            string key = $"{look.Suit}/{look.Helmet}/{look.Visor}/{look.Accessory}/{look.Accent}/{look.Lower}";
            if (key != _lookKey)
            {
                _lookKey = key;
                if (_rig != null) Destroy(_rig.gameObject);
                _rig = AvatarBuilder.Build(look, transform);
                _heldId = null;
                // 其他人的 avatar 不擋準星，也不擋路（CharacterController 才有碰撞）
                foreach (var t in _rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            }
            if (_tag == null)
            {
                _tag = gameObject.AddComponent<WorldLabel>();
                _tag.style = "tag";
                _tag.offset = new Vector3(0, 1.45f, 0);
                _tag.maxDistance = 30f;
            }
            _tag.text = string.IsNullOrEmpty(look.Name) ? "P" + playerId : look.Name;

            string heldKey = held == null ? null : held + (kit ? "#kit" : "");
            if (heldKey != _heldId)
            {
                _heldId = heldKey;
                if (_held != null) Destroy(_held.gameObject);
                _held = held != null ? FacilityDirector.HeldModel(held, kit, data, _rig.hold) : null;
            }
            _rig.holding = held != null;
        }

        void Update()
        {
            if (!PoseFeed.Remote.TryGetValue(playerId, out var pose) || _rig == null) return;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 12f);
            transform.position = Vector3.Lerp(transform.position, pose.Position, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, pose.Yaw, 0), k);
            _rig.speed = pose.Speed;
            _rig.installing = pose.Installing;
        }
    }
}
