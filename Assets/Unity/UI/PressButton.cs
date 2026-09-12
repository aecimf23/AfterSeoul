using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 누름과 뗌을 따로 알려주는 버튼 부속. <see cref="UnityEngine.UI.Button"/> 과 같이 붙인다.
    ///
    /// <para>Button 의 onClick 은 <b>뗄 때</b> 한 번만 온다. 그걸로는 '누르고 있다가 정확한 때 뗀다'
    /// 같은 동작을 만들 수 없어서, 작업대 미니게임은 이걸 통해 손가락을 직접 받는다.</para>
    ///
    /// <para>누름을 쓰는 게임(타이밍·골라내기)은 뗄 때가 아니라 <b>누르는 순간</b> 판정한다 —
    /// 한 프레임이라도 늦으면 손해 본 기분이 든다.</para>
    /// </summary>
    public sealed class PressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action Pressed;
        public Action Released;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Pressed != null) Pressed();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Released != null) Released();
        }
    }
}
