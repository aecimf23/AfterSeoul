using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 노치·펀치홀·제스처 바를 피해 자식을 안전 영역 안으로 밀어 넣는다.
    /// GDD §36 크로스플랫폼 원칙의 "Safe Area 지원".
    ///
    /// <para>화면 회전이나 접이식 기기의 폴드 상태 변화로 안전 영역이 바뀔 수 있어서
    /// 매 프레임 값만 비교하고, 달라졌을 때만 다시 계산한다.</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        private RectTransform _rt;
        private Rect _applied;
        private Vector2Int _appliedScreen;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            var area = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (area == _applied && size == _appliedScreen) return;
            Apply();
        }

        private void Apply()
        {
            var area = Screen.safeArea;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return;

            _applied = area;
            _appliedScreen = new Vector2Int(w, h);

            var min = new Vector2(area.xMin / w, area.yMin / h);
            var max = new Vector2(area.xMax / w, area.yMax / h);

            // 값이 이상하면(에디터 초기 프레임 등) 건드리지 않는다.
            if (float.IsNaN(min.x) || float.IsNaN(max.x)) return;

            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
