using AfterSeoul.Core;
using System;

namespace AfterSeoul.Mail
{
    public interface IAccountMailLink : IMailLink
    {
        bool Connected { get; }
        void Sync(Action<bool, string> done);
        void Disconnect();
    }
    /// <summary>
    /// 본편과의 연결 통로 (GDD §12, LINK_CONTRACT).
    ///
    /// <para><b>왜 막아 두는가.</b> 연결을 확인 없이 켜면 이런 일이 벌어진다:
    /// 플레이어가 "본편과 연결"을 누른다 → 창고에 발송 버튼이 생긴다 → 누르면
    /// <b>물건이 창고에서 차감된다</b>(LINK_CONTRACT §V7, 복제를 막으려면 차감이 먼저다)
    /// → 화면은 "본편 접속 시 전달됩니다"라고 알린다 → <b>받을 쪽이 없어서 영영 안 간다.</b></para>
    ///
    /// <para>즉 지금 상태로 내보내면 그 기능은 <b>안심시키는 문구와 함께 플레이어의 물건을
    /// 삼킨다.</b> 되돌릴 방법도 없다 — 차감은 이미 저장됐고 화물은 아무도 안 가져간다.
    /// 미완성 기능이 조용히 실패하는 것과 데이터를 지우는 것은 다른 문제다.</para>
    ///
    /// <para>그래서 <see cref="NullMailLink"/> 를 기본값으로 두고 <b>연결 자체를 거절한다.</b>
    /// 아래쪽 검사(<c>Outbox.BlockReason</c> 의 "본편과 연결되지 않았습니다")가 이미 전부
    /// <c>Mail.Linked</c> 를 보고 있으므로, 연결이 안 켜지면 발송 경로 전체가 자동으로 닫힌다 —
    /// 화면마다 따로 막을 필요가 없다.</para>
    ///
    /// <para><see cref="Core.IStore"/> 와 같은 모양이다. 붙지 않은 바깥 세계는 인터페이스 하나로
    /// 막고, 기본 구현은 <b>조용히 성공하지 않는다</b>.</para>
    /// </summary>
    public interface IMailLink
    {
        /// <summary>이 빌드에서 본편 연동을 쓸 수 있는가. false 면 화면이 버튼 자체를 안 만든다.</summary>
        bool Available { get; }

        /// <summary>연동이 왜 아직 안 되는지. 화면에 그대로 띄운다.</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// 본편에서 발급받은 코드로 연결한다. 성공하면 프로필 라벨을 돌려준다.
        /// <paramref name="done"/> 의 첫 인자가 false 면 둘째가 사유다.
        /// </summary>
        void Connect(string code, Action<bool, string> done);
    }

    /// <summary>
    /// 연동이 아직 붙지 않은 빌드의 기본값. <b>항상 거절한다.</b>
    ///
    /// <para>개발 편의로 "일단 연결된 척"을 기본값에 넣으면 그게 스토어 빌드까지 따라가고,
    /// 그 순간부터 발송은 물건을 지우는 버튼이 된다.</para>
    /// </summary>
    public sealed class NullMailLink : IMailLink
    {
        public bool Available => false;

        public string UnavailableReason =>
            Loc.Text("본편 연동은 아직 준비 중입니다. 연동하지 않아도 이 게임은 전부 즐길 수 있습니다.");

        public void Connect(string code, Action<bool, string> done) =>
            done?.Invoke(false, UnavailableReason);
    }

    /// <summary>
    /// 에디터에서 연동 화면을 실제로 눌러보기 위한 가짜 연결.
    ///
    /// <para><b>실제로 보내지지는 않는다.</b> 발송함에 쌓이는 것까지가 전부이고 그 뒤는 P5 다.
    /// 그래서 이걸 꽂으면 <b>물건이 사라진다</b> — 에디터에서만 꽂는 이유가 그것이다.</para>
    /// </summary>
    public sealed class DebugMailLink : IMailLink
    {
        public bool Available => true;

        public string UnavailableReason => null;

        public void Connect(string code, Action<bool, string> done) =>
            done?.Invoke(true, Loc.Text("[개발용] 본편 프로필"));
    }
}
