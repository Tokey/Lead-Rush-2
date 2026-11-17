using LR2.Core;
using UnityEngine;

namespace LR2.FX
{
    [RequireComponent(typeof(LineRenderer))]
    public class Tracer : MonoBehaviour
    {
        [SerializeField] private float speed = 260f;
        [SerializeField] private float length = 11f;
        [SerializeField] private float width = 0.055f;

        private LineRenderer _line;
        private Vector3 _from, _to;
        private float _travelled, _total;

        public void Launch(Vector3 from, Vector3 to)
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            _from = from;
            _to = to;
            _total = Vector3.Distance(from, to);
            _travelled = 0f;
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = width;
            _line.endWidth = width * 0.45f;
            _line.startColor = new Color(1f, 0.94f, 0.72f, 1f);
            _line.endColor = new Color(1f, 0.68f, 0.22f, 0.35f);
            _line.SetPosition(0, from);
            _line.SetPosition(1, from);
            _line.enabled = true;
        }

        private void Update()
        {
            if (_line == null) return;
            _travelled += speed * Time.deltaTime;
            if (_travelled - length > _total)
            {
                _line.enabled = false;
                LR2Pool.Despawn(gameObject);
                return;
            }
            Vector3 dir = _total > 0.001f ? (_to - _from) / _total : Vector3.forward;
            float head = Mathf.Min(_travelled, _total);
            float tail = Mathf.Clamp(_travelled - length, 0f, _total);
            _line.SetPosition(0, _from + dir * head);
            _line.SetPosition(1, _from + dir * tail);
        }
    }
}
