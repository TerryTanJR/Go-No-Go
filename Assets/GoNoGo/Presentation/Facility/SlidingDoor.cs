using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>組裝區和中控室之間的門：有人靠近就往兩邊滑開。兩區永遠連通，沒有上鎖的時候。</summary>
    public sealed class SlidingDoor : MonoBehaviour
    {
        public Transform left, right;
        public float openDistance = 1.2f;
        public float triggerRange = 2.6f;

        Vector3 _l0, _r0;
        float _open;
        bool _wasOpen;

        void Start()
        {
            if (left != null) _l0 = left.localPosition;
            if (right != null) _r0 = right.localPosition;
        }

        void Update()
        {
            bool someone = Near(PoseFeed.Local.Position);
            foreach (var kv in PoseFeed.Remote) someone |= Near(kv.Value.Position);
            _open = Mathf.MoveTowards(_open, someone ? 1f : 0f, Time.deltaTime * 2.8f);
            float e = _open * _open * (3 - 2 * _open);
            if (left != null) left.localPosition = _l0 + Vector3.left * openDistance * e;
            if (right != null) right.localPosition = _r0 + Vector3.right * openDistance * e;
            if (someone != _wasOpen)
            {
                _wasOpen = someone;
                Sfx.Door();
            }
        }

        bool Near(Vector3 p) => p != Vector3.zero && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(transform.position.x, 0, transform.position.z)) < triggerRange;
    }
}
