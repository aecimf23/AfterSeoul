using System;
using System.Collections.Generic;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 게임의 단일 진입점.
    ///
    /// <para>UI 는 이 객체 하나만 안다. 시스템 클래스를 직접 잡지 않는다.
    /// 싱글턴을 만들지 않는 이유는 취향이 아니라 테스트다 — 싱글턴이 되는 순간
    /// 테스트끼리 상태가 샌다.</para>
    ///
    /// <para>Unity 쪽에서는 부트스트랩 MonoBehaviour 하나가 이걸 만들어 들고 있으면 된다.</para>
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>MVP 는 고용주가 황 상사 하나라 선택 화면 없이 바로 배정한다 (GDD §4).</summary>
        public const string DefaultEmployerNpcId = "HWANG";

        public GameSave Save { get; private set; }
        public IDataRegistry Data { get; }
        public IClock Clock { get; }

        public ExpeditionSystem Expeditions { get; }
        public FactorySystem Factory { get; }
        public DailyQuestSystem Quests { get; }

        private readonly SaveService _saves;
        private readonly OfflineResolver _resolver;

        /// <summary>
        /// 마지막 정산 결과. 홈 화면의 복귀 보고서가 이걸 그린다.
        /// UI 가 세이브를 뒤져서 변경분을 역산하지 않게 하려고 존재한다.
        /// </summary>
        public ResolveReport LastReport { get; private set; }

        /// <summary>
        /// 무언가 일어난 정산이 끝났을 때. 앱이 켜진 채로 파견이 돌아온 경우처럼
        /// UI 가 요청하지 않은 정산의 결과를 받는 통로다. 빈 정산에는 부르지 않는다.
        /// </summary>
        public event Action<ResolveReport> Resolved;

        public GameSession(SaveService saves, IDataRegistry data, IClock clock)
        {
            _saves = saves;
            Data = data;
            Clock = clock;

            Expeditions = new ExpeditionSystem();
            Factory = new FactorySystem();
            Quests = new DailyQuestSystem();

            // 등록 순서는 우선순위가 아니다. 실제 적용 순서는 사건의 시각이 정한다.
            // 다만 같은 시각·같은 Order 일 때의 tie-break 에 영향을 주므로
            // 이 순서 자체는 결정론적으로 고정돼 있어야 한다.
            var systems = new List<ITimelineSystem> { Expeditions, Factory, Quests };
            _resolver = new OfflineResolver(clock, systems);
        }

        /// <summary>앱 시작 시 한 번. 세이브를 읽고 오프라인 구간을 정산한다.</summary>
        public ResolveReport Boot()
        {
            Save = _saves.LoadOrCreate();
            if (string.IsNullOrEmpty(Save.Player.EmployerNpcId))
                StartNewGame(DefaultEmployerNpcId);
            return Resume();
        }

        /// <summary>
        /// 앱이 포그라운드로 돌아올 때마다. <b>정산 진입점은 이것(과 같은 경로를 타는 <see cref="Tick"/>) 뿐이다.</b>
        ///
        /// 각 시스템이 자기 화면에서 따로 시간을 따라잡지 않는다. 그렇게 하면
        /// "공장 화면은 정산됐는데 탐색 화면은 아직"인 상태가 생기고,
        /// 그 불일치는 화면 전환 순서에 따라 달라져서 재현이 거의 불가능해진다.
        /// </summary>
        public ResolveReport Resume()
        {
            var report = ResolveNow();
            _saves.Save(Save);
            return report;
        }

        /// <summary>
        /// 앱이 켜져 있는 동안 주기적으로. <see cref="Resume"/> 과 같은 정산이지만
        /// 아무 일도 없었으면 디스크에 쓰지 않는다 — 몇 초마다 파일을 쓸 이유가 없다.
        /// 쓰지 않은 진행분은 멱등이라 다음 정산이 다시 따라잡는다.
        /// </summary>
        public ResolveReport Tick()
        {
            var report = ResolveNow();
            if (!report.IsEmpty) _saves.Save(Save);
            return report;
        }

        /// <summary>앱이 백그라운드로 갈 때. 저장 실패는 게임을 막지 않는다.</summary>
        public void Suspend()
        {
            try { _saves.Save(Save); }
            catch { /* 다음 저장 때 다시 시도한다 */ }
        }

        /// <summary>
        /// 플레이어 조작 후 호출. 저장이 잦아지는 것을 감수한다 —
        /// 모바일은 앱이 예고 없이 죽고, 파견 하나가 통째로 사라지는 것보다는 낫다.
        ///
        /// <para><b><c>SavedAt</c> 을 건드리지 않는다.</b> <c>SavedAt</c> 은 "마지막으로 정산이 끝난
        /// 시각"이고 옮길 수 있는 건 정산기뿐이다. 여기서 now 로 밀면 그 사이의 날짜 경계가
        /// 정산되지 않은 채 사라져서, 앱을 켠 채로 새벽 5시를 넘긴 날은 의뢰가 갱신되지 않는다.</para>
        /// </summary>
        public void Commit()
        {
            _saves.Save(Save);
        }

        // ── 플레이어 조작 ─────────────────────────────────────────
        // 전부 "정산으로 현재까지 따라잡기 → 조작 → 저장" 순서다. 따라잡지 않고 조작하면
        // 이미 만료된 어제 의뢰를 납품하거나, 복귀한 스캐브를 아직 파견 중으로 보고 거절하게 된다.

        /// <summary>직접 노동 1회. 점수(0~1)로 품질을 매겨 보수를 준다.</summary>
        public long DoManualWork(double score, out CraftQuality quality)
        {
            Tick();
            quality = FactorySystem.GradeManualWork(Data, score);
            long pay = Factory.CompleteManualWork(Save, Data, quality);
            Commit();
            return pay;
        }

        public bool Sell(string itemId, int count)
        {
            Tick();
            if (!Market.TrySell(Save, Data, itemId, count)) return false;
            Commit();
            return true;
        }

        public CraftJob EnqueueCraft(string recipeId)
        {
            Tick();
            var job = Factory.Enqueue(Save, Data, recipeId, Clock.UtcNow);
            if (job != null) Commit();
            return job;
        }

        public ExpeditionState Depart(string mapId, IReadOnlyList<string> scavUids)
        {
            Tick();
            var exp = Expeditions.Depart(Save, Data, mapId, scavUids, Clock.UtcNow);
            if (exp != null) Commit();
            return exp;
        }

        public bool Deliver(string questId)
        {
            Tick();
            if (!Quests.TryDeliver(Save, Data, questId)) return false;
            Commit();
            return true;
        }

        // ── 내부 ─────────────────────────────────────────────────

        private ResolveReport ResolveNow()
        {
            LastReport = _resolver.Resolve(Save, Data);
            FactorySystem.PruneCollected(Save);
            PruneResolvedExpeditions();
            if (!LastReport.IsEmpty) Resolved?.Invoke(LastReport);
            return LastReport;
        }

        private void StartNewGame(string employerNpcId)
        {
            Save.Player.EmployerNpcId = employerNpcId;
            Save.Player.Money = Data.Balance.StartingMoney;
        }

        /// <summary>정산이 끝난 파견 기록을 정리한다. 최근 것 일부는 UI 이력용으로 남긴다.</summary>
        private void PruneResolvedExpeditions()
        {
            const int keepRecent = 20;
            var resolved = Save.Expeditions.FindAll(e => e.Resolved);
            if (resolved.Count <= keepRecent) return;

            resolved.Sort((a, b) => a.ReturnsAt.CompareTo(b.ReturnsAt));
            int removeCount = resolved.Count - keepRecent;
            for (int i = 0; i < removeCount; i++)
                Save.Expeditions.Remove(resolved[i]);
        }
    }
}
