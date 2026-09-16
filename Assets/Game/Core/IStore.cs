using System;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 결제·광고가 게임 규칙과 만나는 유일한 지점 (GDD §11).
    ///
    /// <para><b>왜 인터페이스 하나로 막아 두는가.</b> 결제 SDK 는 스토어 계정이 있어야 붙일 수
    /// 있고, 그 전까지 아무것도 못 만든다면 과금 경계는 검증되지 않은 채로 남는다. 반대로 SDK 를
    /// 게임 코드 안으로 들이면, 나중에 스토어가 바뀌거나 SDK 가 갈아엎어질 때 규칙 코드까지
    /// 같이 흔들린다.</para>
    ///
    /// <para>그래서 게임이 아는 것은 이 인터페이스뿐이다. <b>"플랫폼에 물어봤더니 됐다더라"</b>
    /// 한 줄. 영수증 검증도, 복원도, 지역별 가격도 전부 저쪽 일이다.
    /// <c>ITimelineSystem</c> 이 오프라인 정산에 대해 하는 일과 같은 종류의 경계다.</para>
    ///
    /// <para><b>이 인터페이스에 본편 배송 관련 메서드는 없고 앞으로도 없다.</b>
    /// 돈으로 본편에 더 보낼 수 있으면 그건 편의가 아니라 본편 경제를 파는 것이다 (GDD §11).</para>
    /// </summary>
    public interface IStore
    {
        /// <summary>이 빌드에서 결제·광고를 쓸 수 있는가. 없으면 화면이 버튼 자체를 안 띄운다.</summary>
        bool Available { get; }

        /// <summary>월간 지원계약. 성공하면 <see cref="StoreResult.Duration"/> 만큼 계약을 얹는다.</summary>
        void PurchaseSupport(Action<StoreResult> done);

        /// <summary>보상 광고. 끝까지 봤을 때만 Ok 다.</summary>
        void ShowRewardedAd(Action<StoreResult> done);
    }

    public readonly struct StoreResult
    {
        public readonly bool Ok;

        /// <summary>화면에 그대로 띄울 사유. 실패를 조용히 삼키지 않는다.</summary>
        public readonly string Message;

        /// <summary>계약 기간. 광고에는 뜻이 없다.</summary>
        public readonly TimeSpan Duration;

        public StoreResult(bool ok, string message, TimeSpan duration = default)
        {
            Ok = ok;
            Message = message;
            Duration = duration;
        }

        public static StoreResult Fail(string message) => new StoreResult(false, message);

        public static StoreResult Success(string message, TimeSpan duration = default) =>
            new StoreResult(true, message, duration);
    }

    /// <summary>
    /// 상점이 붙지 않은 빌드의 기본값.
    ///
    /// <para><b>조용히 성공시키지 않는다.</b> 개발 편의로 "일단 되는 척"을 넣어 두면 그게
    /// 스토어 빌드까지 따라가서 결제 없이 혜택이 들어가는 사고가 된다. 여기서는 항상 거절하고,
    /// <see cref="IStore.Available"/> 이 false 라서 화면은 애초에 버튼을 만들지 않는다.</para>
    /// </summary>
    public sealed class NullStore : IStore
    {
        public bool Available => false;

        public void PurchaseSupport(Action<StoreResult> done) =>
            done?.Invoke(StoreResult.Fail(Loc.Text("이 빌드에는 상점이 연결되어 있지 않습니다")));

        public void ShowRewardedAd(Action<StoreResult> done) =>
            done?.Invoke(StoreResult.Fail(Loc.Text("이 빌드에는 광고가 연결되어 있지 않습니다")));
    }

    /// <summary>
    /// 에디터에서 과금 경로를 실제로 걸어보기 위한 가짜 상점.
    ///
    /// <para><b>돈은 오가지 않는다.</b> 이게 있어야 스토어 계정 없이도
    /// "계약을 사면 큐가 한 칸 느는가", "광고 보상이 진짜로 적용되는가"를 확인할 수 있다.
    /// 붙이는 쪽(<c>Bootstrap</c>)이 <c>UNITY_EDITOR</c> 에서만 꽂는다 —
    /// 이 클래스가 스토어 빌드에 들어가도 <b>아무도 꽂지 않으면</b> 쓰이지 않는다.</para>
    /// </summary>
    public sealed class DebugStore : IStore
    {
        public bool Available => true;

        public void PurchaseSupport(Action<StoreResult> done) =>
            done?.Invoke(StoreResult.Success(Loc.Text("[개발용] 지원계약 30일"), TimeSpan.FromDays(30)));

        public void ShowRewardedAd(Action<StoreResult> done) =>
            done?.Invoke(StoreResult.Success(Loc.Text("[개발용] 광고 시청 완료")));
    }
}
