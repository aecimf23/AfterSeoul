using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 탭 하나에 대응하는 화면.
    ///
    /// <para>MonoBehaviour 가 아니다. 화면은 Unity 수명 주기가 필요 없고 —
    /// 갱신 시점을 <see cref="AppShell"/> 이 전부 정한다 — 평범한 객체로 두면
    /// 생성 순서나 Awake/Start 순서를 신경 쓸 일이 없다.</para>
    /// </summary>
    public abstract class ScreenBase
    {
        protected GameSession Session { get; private set; }
        protected AppShell Shell { get; private set; }

        /// <summary>화면 내용이 들어가는 곳. 헤더와 탭 바 사이 영역.</summary>
        protected RectTransform Root { get; private set; }

        /// <summary>탭에 표시할 이름.</summary>
        public abstract string TabName { get; }

        /// <summary>헤더에 표시할 제목. 기본은 탭 이름과 같다.</summary>
        public virtual string Title => TabName;

        /// <summary>
        /// 탭에 붙는 그림.
        ///
        /// <para>글자 다섯 개가 폭을 똑같이 나눠 가지면 어디가 어딘지 안 읽힌다 —
        /// 실제로 "창고가 어디 있는지 모르겠다"는 말을 들었다. 모양이 있으면 위치를 외우게 된다.</para>
        /// </summary>
        public virtual IconSet.TabGlyph Glyph => IconSet.TabGlyph.Home;

        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        internal void Create(AppShell shell, GameSession session, Transform parent)
        {
            Shell = shell;
            Session = session;
            Root = Ui.Rect("Screen_" + TabName, parent);
            Ui.Stretch(Root);
            Root.gameObject.SetActive(false);
            Build();
        }

        internal void SetVisible(bool visible)
        {
            if (Root == null) return;
            Root.gameObject.SetActive(visible);
            if (visible) Refresh();
        }

        /// <summary>
        /// 탭이 바뀌어 이 화면이 올라올 때의 움직임.
        ///
        /// <para><paramref name="direction"/> 은 탭 이동 방향(+1 오른쪽, -1 왼쪽)이다.
        /// 방향을 따라 들어와야 "옆으로 넘겼다"가 되고, 늘 같은 쪽에서 들어오면
        /// 다섯 탭이 한 줄로 놓여 있다는 감각이 사라진다.</para>
        ///
        /// <para>거리는 48px 뿐이다. 화면 폭만큼 밀면 그건 전환이 아니라 대기 시간이 된다 —
        /// 탭은 하루에 수백 번 누른다.</para>
        /// </summary>
        internal void PlayEnter(int direction)
        {
            if (Root == null) return;

            Tween.FadeIn(Root, 0.13f);
            Tween.SlideIn(Root, new Vector2(48f * Mathf.Sign(direction == 0 ? 1 : direction), 0f), 0.2f);
        }

        /// <summary>한 번만. 바뀌지 않는 뼈대를 만든다.</summary>
        protected abstract void Build();

        /// <summary>
        /// 보일 때마다, 그리고 정산이 끝날 때마다 호출된다.
        /// <b>여기서 세이브를 변경하면 안 된다.</b> 읽어서 그리기만 한다.
        /// </summary>
        public virtual void Refresh() { }

        /// <summary>매 프레임. 미니게임처럼 애니메이션이 필요한 화면만 쓴다.</summary>
        public virtual void Tick(float deltaTime) { }
    }
}
