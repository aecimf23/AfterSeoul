using System;
using System.Collections.Generic;
using AfterSeoul.Expedition;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;
using AfterSeoul.Quest;
using AfterSeoul.Scav;

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
        /// <summary>고용 시장. 이름을 Market 으로 두면 판매용 <see cref="Inventory.Market"/> 을 가린다.</summary>
        public ScavMarket Hiring { get; }

        /// <summary>보조 인력. 앱을 꺼 둔 동안에도 정해둔 것을 만든다.</summary>
        public AssistantSystem Assistants { get; }

        /// <summary>실종자 무전. 잃은 사람을 데려올 수 있게 한다 (GDD §15).</summary>
        public RescueSystem Rescues { get; }

        /// <summary>부상 회복. 자는 동안에도 나아야 하므로 타임라인에 얹는다 (GDD §15).</summary>
        public TreatmentSystem Treatments { get; }

        /// <summary>
        /// 정산에 참여하는 시스템 <b>전부</b>, 등록된 순서 그대로.
        ///
        /// <para><b>테스트가 이걸 그대로 쓴다.</b> 예전에는 정산 테스트가 자기 목록을 따로 들고
        /// 있었는데, 시스템이 넷 늘어나는 동안 그 목록은 셋에서 멈춰 있었다 — "GameSession 과
        /// 같은 순서여야 한다"고 적힌 주석 바로 밑에서. 그래서 결정론·멱등성 같은 핵심 보증이
        /// 나중에 붙은 시스템에는 걸려 있지 않았다.</para>
        ///
        /// <para>목록을 두 벌 두면 반드시 갈라진다. 한 벌만 두고 빌려 쓰게 한다.</para>
        /// </summary>
        public IReadOnlyList<ITimelineSystem> Systems { get; }

        /// <summary>
        /// 고용주를 아직 안 골랐는가. 새 세이브는 고르는 화면부터 시작한다 (GDD §4).
        /// </summary>
        public bool NeedsEmployerChoice =>
            Save != null && string.IsNullOrEmpty(Save.Player.EmployerNpcId);

        /// <summary>
        /// 고용주를 정한다. <b>한 번 고르면 바꾸지 않는다</b> — 신뢰도가 사람별로 쌓이는데
        /// 갈아탈 수 있으면 신뢰도가 의미를 잃는다. 다만 GDD §4 의 절대 규칙대로,
        /// 누구를 고르든 중후반에는 같은 지역·의뢰에 닿는다.
        /// </summary>
        public bool ChooseEmployer(string npcId)
        {
            if (Save == null || !NeedsEmployerChoice) return false;
            if (Employers.Find(Data, npcId) == null) return false;

            StartNewGame(npcId);
            // Boot may have marked today while no employer pool existed. Reinitialize
            // only at this guarded, one-time choice, then resolve through the current day.
            Save.Quests.ActiveGameDate = null;
            ResolveNow();
            Commit();
            return true;
        }

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
            Hiring = new ScavMarket();

            // 등록 순서는 우선순위가 아니다. 실제 적용 순서는 사건의 시각이 정한다.
            // 다만 같은 시각·같은 Order 일 때의 tie-break 에 영향을 주므로
            // 이 순서 자체는 결정론적으로 고정돼 있어야 한다.
            // 새 시스템은 목록 끝에 붙인다. 중간에 끼우면 같은 시각·같은 Order 사건의
            // tie-break 순서가 바뀌어 기존 세이브의 정산 결과가 달라질 수 있다.
            Assistants = new AssistantSystem();
            Rescues = new RescueSystem();
            Treatments = new TreatmentSystem();

            Systems = new List<ITimelineSystem>
                { Expeditions, Factory, Quests, Hiring, Assistants, Rescues, Treatments };
            _resolver = new OfflineResolver(clock, Systems);
        }

        /// <summary>앱 시작 시 한 번. 세이브를 읽고 오프라인 구간을 정산한다.</summary>
        public ResolveReport Boot()
        {
            Save = _saves.LoadOrCreate();
            if (Save.Mail == null) Save.Mail = new MailState();
            // Link readiness is session-scoped. A legacy debug flag is not authentication.
            Save.Mail.Linked = false;
            if (Save.ExploredMapIds == null) Save.ExploredMapIds = new List<string>();
            foreach (var exp in Save.Expeditions)
                if (!exp.IsOrientation && !string.IsNullOrEmpty(exp.MapId) && !Save.ExploredMapIds.Contains(exp.MapId))
                    Save.ExploredMapIds.Add(exp.MapId);

            // 고용주를 자동으로 배정하지 않는다 — 고르는 것이 첫 화면이다 (GDD §4).
            // 다만 데이터에 고용주가 하나뿐이면 물어볼 것이 없으므로 바로 정한다.
            if (string.IsNullOrEmpty(Save.Player.EmployerNpcId))
            {
                string only = SingleEmployerOrNull();
                if (only != null) StartNewGame(only);
            }
            // Older saves could choose an employer without ever receiving today's work.
            // Delivered entries remain in Active, so a nonempty board must never be reset.
            if (!NeedsEmployerChoice && Save.Quests.Active.Count == 0 && Quests.DailyQuestCount > 0)
            {
                var employer = Employers.Find(Data, Save.Player.EmployerNpcId);
                var pool = employer == null ? null : Data.GetQuestPool(Employers.QuestPoolId(Data, employer.NpcId));
                if (pool != null && pool.Count > 0) Save.Quests.ActiveGameDate = null;
            }
            Orientation.Initialize(Save, Data);
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
            // 정산 밖에서 경험치가 오르는 경로가 있다 — 직접 노동과 의뢰 납품.
            // 조작이 끝나는 자리는 전부 여기를 지나므로 한 곳에서 맞춰 준다.
            int gained = Leveling.Sync(Save, Data.Balance);
            if (gained > 0) PendingLevelUps += gained;

            _saves.Save(Save);
        }

        /// <summary>
        /// 아직 화면에 알리지 않은 레벨업 수. 화면이 <see cref="ConsumeLevelUps"/> 로 가져간다.
        ///
        /// <para>정산 중의 레벨업은 복귀 보고가 그리지만, 조작(노동·납품) 중의 레벨업은
        /// 보고서가 없다. 그 자리에서 알려주지 않으면 헤더의 숫자가 소리 없이 바뀔 뿐이다.</para>
        /// </summary>
        public int PendingLevelUps { get; private set; }

        /// <summary>쌓인 레벨업 수를 가져가고 비운다.</summary>
        public int ConsumeLevelUps()
        {
            int n = PendingLevelUps;
            PendingLevelUps = 0;
            return n;
        }

        // ── 플레이어 조작 ─────────────────────────────────────────
        // 전부 "정산으로 현재까지 따라잡기 → 조작 → 저장" 순서다. 따라잡지 않고 조작하면
        // 이미 만료된 어제 의뢰를 납품하거나, 복귀한 스캐브를 아직 파견 중으로 보고 거절하게 된다.

        /// <summary>작업대에서 만들 것을 고른다. 재료를 즉시 차감한다.</summary>
        public bool StartWork(string recipeId)
        {
            Tick();
            if (!Workbench.TryStart(Save, Data, recipeId)) return false;
            Commit();
            return true;
        }

        /// <summary>
        /// 미니게임 한 번의 결과를 작업대에 반영한다. 마지막 단계면 물건이 나온다.
        ///
        /// <para>매 단계마다 저장한다. 세 번 두드려 놓고 앱이 죽었을 때 처음부터 다시 하라고
        /// 하면 그게 제일 억울하다.</para>
        /// </summary>
        public WorkStepResult AdvanceWork(double score)
        {
            Tick();
            var result = Workbench.Advance(Save, Data, score);
            Commit();
            return result;
        }

        /// <summary>작업을 접는다. 재료는 돌려준다.</summary>
        public bool CancelWork()
        {
            Tick();
            if (!Workbench.Cancel(Save, Data)) return false;
            Commit();
            return true;
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

        // ── 작업대 성장 (GDD §6) ─────────────────────────────────

        /// <summary>작업대를 한 단계 올린다. 큐 칸·제작 속도·상위 도면·큐 품질이 같이 오른다.</summary>
        public bool UpgradeStation()
        {
            Tick();
            if (!Station.TryUpgrade(Save, Data)) return false;
            Commit();
            return true;
        }

        /// <summary>보조 인력을 한 명 더 쓴다. 앱을 꺼 둔 동안에도 정해둔 것을 만든다.</summary>
        public bool HireAssistant()
        {
            Tick();
            if (!Station.TryHireAssistant(Save, Data)) return false;
            Commit();
            return true;
        }

        /// <summary>보조 인력이 만들 것을 정한다. null 이면 손을 놓는다.</summary>
        public bool SetAutoRecipe(string recipeId)
        {
            Tick();
            if (!Station.TrySetAutoRecipe(Save, Data, recipeId)) return false;
            Commit();
            return true;
        }

        public ExpeditionState DepartOrientation(IReadOnlyList<string> team)
        {
            Tick();
            var exp = Orientation.Depart(Save, Data, team, Clock.UtcNow);
            if (exp != null) Commit();
            return exp;
        }

        public bool DeliverOrientation()
        {
            Tick();
            if (!Orientation.Deliver(Save)) return false;
            Commit();
            return true;
        }

        public ExpeditionState Depart(string mapId, IReadOnlyList<string> scavUids)
        {
            Tick();
            var exp = Expeditions.Depart(Save, Data, mapId, scavUids, Clock.UtcNow);
            if (exp != null) Commit();
            return exp;
        }

        /// <summary>
        /// 실종자를 데리러 간다 (GDD §15). 잃은 그 지역으로 가고, 신호가 살아 있을 때만 된다.
        /// </summary>
        public ExpeditionState DepartRescue(string missingScavUid, IReadOnlyList<string> teamUids)
        {
            Tick();

            var lost = FindScav(missingScavUid);
            if (lost == null || string.IsNullOrEmpty(lost.LostAtMapId)) return null;

            var exp = Expeditions.Depart(
                Save, Data, lost.LostAtMapId, teamUids, Clock.UtcNow, missingScavUid);

            if (exp != null) Commit();
            return exp;
        }

        /// <summary>지금 데리러 갈 수 있는 실종자들. 신호에는 시한이 있다.</summary>
        public List<ScavState> RescuableScavs()
        {
            Tick();
            return RescueSystem.Rescuable(Save, Clock.UtcNow);
        }

        private ScavState FindScav(string uid)
        {
            foreach (var s in Save.Scavs) if (s.Uid == uid) return s;
            return null;
        }

        // ── 치료 (GDD §15) ──────────────────────────────────────

        /// <summary>
        /// 부상자를 치료대에 올린다. 회복은 <see cref="TreatmentSystem"/> 이 시각에 맞춰 끝낸다.
        ///
        /// <para><paramref name="useSupplies"/> 가 참이고 창고에 의료품이 있으면 시간이 준다.
        /// 없으면 그냥 돈과 시간만 든다 — 의료품이 없다고 치료가 막히지는 않는다.</para>
        /// </summary>
        public bool TreatScav(string scavUid, bool useSupplies = true)
        {
            Tick();

            var scav = FindScav(scavUid);
            if (!Treatment.TryTreat(Save, Data, scav, Clock.UtcNow, useSupplies)) return false;

            Commit();
            return true;
        }

        /// <summary>치료가 막히는 이유. 없으면 null. 버튼이 조용히 안 먹지 않게 하려고 있다.</summary>
        public string TreatBlockReason(string scavUid) =>
            Treatment.BlockReason(Save, Data, FindScav(scavUid));

        // ── 지원계약과 보상 광고 (GDD §11) ───────────────────────

        /// <summary>
        /// 결제·광고를 실제로 처리하는 쪽. 붙이지 않으면 <see cref="NullStore"/> 가
        /// "이 빌드에는 상점이 없다"고 정직하게 거절한다 (R1 과 같은 이유).
        /// </summary>
        public IStore Store { get; set; } = new NullStore();

        /// <summary>
        /// 월간 지원계약을 산다.
        ///
        /// <para><b>영수증 검증은 여기 없다.</b> <see cref="IStore"/> 가 플랫폼에 물어보고
        /// 성공이라고 답한 뒤에야 <see cref="Support.Grant"/> 를 부른다. 이 순서를 뒤집으면
        /// 결제 실패에도 혜택이 들어간다.</para>
        /// </summary>
        public void PurchaseSupport(Action<bool, string> done)
        {
            Tick();

            Store.PurchaseSupport(result =>
            {
                if (result.Ok)
                {
                    Support.Grant(Save, Clock.UtcNow, result.Duration);
                    // 산 즉시 칸이 늘어야 한다. 다음 정산까지 기다리면 "돈은 냈는데 아무 일도
                    // 안 일어난" 몇 초가 생기고, 그 몇 초가 환불 문의가 된다.
                    SyncSupportBenefits();
                    Commit();
                }
                done?.Invoke(result.Ok, result.Message);
            });
        }

        /// <summary>
        /// 보상 광고를 보고 보상을 받는다. 하루 횟수는 <see cref="RewardedAd.MaxPerDay"/>.
        ///
        /// <para>보상을 <b>먼저 적용하지 않는다.</b> 광고를 끝까지 봤는지는 플랫폼만 알고,
        /// 여기서 미리 주면 중간에 닫아도 받게 된다.</para>
        /// </summary>
        public void WatchAd(RewardedAd.Reward reward, Action<bool, string> done)
        {
            Tick();

            if (RewardedAd.RemainingToday(Save, Clock.UtcNow) <= 0)
            {
                done?.Invoke(false, Loc.Text("오늘은 더 받을 수 없습니다"));
                return;
            }

            // 계약자는 광고를 보지 않고 같은 보상을 받는다 (GDD §11 — "운영 편의성 중심").
            // 하루 횟수는 그대로다. 이게 Support.Benefits 의 "광고 없이 같은 혜택" 줄이고,
            // 이 분기가 없던 동안 그 줄은 화면에만 적혀 있는 거짓말이었다.
            if (Support.IsActive(Save, Clock.UtcNow))
            {
                if (!RewardedAd.TryConsume(Save, Clock.UtcNow))
                {
                    done?.Invoke(false, Loc.Text("오늘은 더 받을 수 없습니다"));
                    return;
                }

                string granted = ApplyReward(reward);
                Commit();
                done?.Invoke(true, granted);
                return;
            }

            Store.ShowRewardedAd(result =>
            {
                if (!result.Ok) { done?.Invoke(false, result.Message); return; }

                // 횟수는 보상을 주기 전에 깎는다. 보상 적용이 실패해도 시청은 일어난 일이다.
                if (!RewardedAd.TryConsume(Save, Clock.UtcNow))
                {
                    done?.Invoke(false, Loc.Text("오늘은 더 볼 수 없습니다"));
                    return;
                }

                string message = ApplyReward(reward);
                Commit();
                done?.Invoke(true, message);
            });
        }

        /// <summary>
        /// 보상을 실제로 적용한다.
        ///
        /// <para><b>이것이 없으면 <see cref="RewardedAd.Reward"/> 는 이름만 있는 열거형이다.</b>
        /// 실제로 그랬다 — 값은 셋 다 선언돼 있었지만 누구도 쓰지 않았고, 그래서 광고를 봐도
        /// 아무 일도 일어나지 않는 상태였다.</para>
        /// </summary>
        private string ApplyReward(RewardedAd.Reward reward)
        {
            var now = Clock.UtcNow;

            switch (reward)
            {
                case RewardedAd.Reward.RerollHiringMarket:
                    Hiring.Reroll(Save, Data, now);
                    return Loc.Text("고용 시장을 다시 열었습니다");

                case RewardedAd.Reward.SpeedUpCraft:
                {
                    var job = SoonestJob();
                    if (job == null) return Loc.Text("단축할 제작이 없습니다");

                    var by = TimeSpan.FromMinutes(30);
                    var target = job.CompletesAt - by;
                    job.CompletesAt = target < now ? now : target;
                    return Loc.Text("제작 시간을 30분 당겼습니다");
                }

                default:
                {
                    var scav = SoonestTreating();
                    if (scav == null) return Loc.Text("치료 중인 사람이 없습니다");

                    Treatment.SpeedUp(scav, now, TimeSpan.FromHours(2));
                    return Loc.Text("{0} 의 회복을 2시간 당겼습니다" , Loc.Text(scav.Name));
                }
            }
        }

        /// <summary>가장 먼저 끝나는 제작. 여러 개를 한꺼번에 당기면 광고 한 번의 값이 너무 커진다.</summary>
        private CraftJob SoonestJob()
        {
            CraftJob best = null;
            foreach (var job in Save.Factory.Queue)
            {
                if (job.Collected) continue;
                if (best == null || job.CompletesAt < best.CompletesAt) best = job;
            }
            return best;
        }

        private ScavState SoonestTreating()
        {
            ScavState best = null;
            foreach (var s in Save.Scavs)
            {
                if (s.Status != ScavStatus.Treating) continue;
                if (best == null || s.RecoversAt < best.RecoversAt) best = s;
            }
            return best;
        }

        /// <summary>스캐브를 고용한다. 실패하면 null — 사유는 <see cref="ScavMarket.HireBlockReason"/>.</summary>
        public ScavState Hire(string offerId)
        {
            Tick();
            var scav = Hiring.TryHire(Save, offerId, Clock.UtcNow);
            if (scav != null) Commit();
            return scav;
        }

        /// <summary>창고의 장비를 스캐브에게 지급한다. 사유는 <see cref="Equipment.EquipBlockReason"/>.</summary>
        public bool Equip(string scavUid, string itemId)
        {
            Tick();
            if (!Equipment.TryEquip(Save, Data, scavUid, itemId)) return false;
            Commit();
            return true;
        }

        /// <summary>장비를 산다. 사유는 <see cref="Shop.BuyBlockReason"/>.</summary>
        public bool Buy(string itemId)
        {
            Tick();
            if (!Shop.TryBuy(Save, Data, itemId)) return false;
            Commit();
            return true;
        }

        /// <summary>장비를 벗겨 창고로 되돌린다. 창고가 꽉 찼으면 벗기지 않는다.</summary>
        public bool Unequip(string scavUid, string slot)
        {
            Tick();
            if (!Equipment.TryUnequip(Save, Data, scavUid, slot)) return false;
            Commit();
            return true;
        }

        /// <summary>
        /// PC 본편 발송함에 화물을 넣는다. 실제 전송은 P5 — 지금은 큐에만 쌓인다.
        /// 사유는 <see cref="Outbox.BlockReason"/>.
        /// </summary>
        public MailShipment QueueShipment(IReadOnlyList<ItemStack> items)
        {
            Tick();
            if (MailLink is IAccountMailLink live && !live.Connected) return null;
            var stacks = new List<ItemStack>(Save.Warehouse.Stacks);
            var mail = Save.Mail;
            int length = mail.Outbox.Count, used = mail.DailyShipmentsUsed;
            long value = mail.DailyQuotaUsedValue;
            string day = mail.DailyQuotaGameDate;
            try
            {
                var shipment = Outbox.TryQueue(Save, Data, items, Clock.UtcNow);
                if (shipment != null) _saves.Save(Save);
                return shipment;
            }
            catch
            {
                Save.Warehouse.Stacks = stacks;
                if (mail.Outbox.Count > length) mail.Outbox.RemoveRange(length, mail.Outbox.Count - length);
                mail.DailyShipmentsUsed = used; mail.DailyQuotaUsedValue = value; mail.DailyQuotaGameDate = day;
                throw;
            }
        }

        public bool BindMailAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId) || (!string.IsNullOrEmpty(Save.Mail.AccountId) && Save.Mail.AccountId != accountId)) return false;
            string previous = Save.Mail.AccountId;
            Save.Mail.AccountId = accountId;
            try { _saves.Save(Save); return true; }
            catch { Save.Mail.AccountId = previous; throw; }
        }

        /// <summary>
        /// 본편 연동 통로. 붙지 않은 빌드에서는 <see cref="NullMailLink"/> 가 연결을 거절한다.
        /// </summary>
        public IMailLink MailLink { get; set; } = new NullMailLink();

        /// <summary>
        /// 본편과 연결한다.
        ///
        /// <para><b>통로가 없으면 연결하지 않는다.</b> 예전에는 확인 없이 <c>Linked = true</c> 로
        /// 만들었는데, 그러면 창고에 발송 버튼이 생기고 누르는 순간 물건이 <b>차감</b>된다
        /// (LINK_CONTRACT §V7 — 복제를 막으려면 차감이 먼저다). 그런데 받을 쪽이 없으니
        /// 화물은 영영 안 가고, 화면은 "본편 접속 시 전달됩니다"라고 알린다.
        /// <b>안심시키는 문구와 함께 플레이어의 물건을 지우는 기능</b>이었다.</para>
        ///
        /// <para>연결이 안 켜지면 <c>Outbox.BlockReason</c> 이 이미 <c>Mail.Linked</c> 를 보고 있어서
        /// 발송 경로 전체가 저절로 닫힌다 — 화면마다 따로 막을 필요가 없다.</para>
        ///
        /// <para><b>연동은 선택이다</b> (GDD §12): 연결하지 않아도 게임 전 구간이 돌아간다.
        /// 그래서 못 여는 것이 미완료 과제처럼 보이면 안 되고, 화면은 사유 한 줄만 적는다.</para>
        /// </summary>
        public void LinkToMainline(string code, Action<bool, string> done)
        {
            Tick();

            if (MailLink == null || !MailLink.Available)
            {
                done?.Invoke(false, new NullMailLink().UnavailableReason);
                return;
            }

            MailLink.Connect(code, (ok, message) =>
            {
                if (ok)
                {
                    bool previous = Save.Mail.Linked;
                    string label = Save.Mail.LinkedProfileLabel;
                    Save.Mail.Linked = true;
                    Save.Mail.LinkedProfileLabel = message;
                    try { Commit(); }
                    catch
                    {
                        Save.Mail.Linked = previous;
                        Save.Mail.LinkedProfileLabel = label;
                        done?.Invoke(false, Loc.Text("연결 정보를 저장하지 못했습니다. 다시 시도하세요."));
                        return;
                    }
                }
                done?.Invoke(ok, message);
            });
        }

        /// <summary>연결을 푼다. 이미 발송함에 쌓인 것은 건드리지 않는다 — 보낸 기록은 기록이다.</summary>
        public void UnlinkFromMainline()
        {
            Tick();
            (MailLink as IAccountMailLink)?.Disconnect();
            Save.Mail.Linked = false;
            Save.Mail.LinkedProfileLabel = null;
            Commit();
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
            // 계약으로 얹힌 창고 칸을 먼저 맞춘다 (GDD §11).
            //
            // 파생값이라 세이브에 담지 않고 여기서 매번 다시 계산한다. 시간이 흐르는 곳이
            // 이 경로 하나뿐이라 어긋날 데가 없고, 계약이 만료되면 다음 정산에서 저절로 0 이 된다.
            // 정산보다 먼저 두는 이유: 정산 중에 전리품이 들어오는데, 그때 칸 수가 옛 값이면
            // 계약자가 "넘쳐서 버려짐"을 보게 된다.
            SyncSupportBenefits();

            LastReport = _resolver.Resolve(Save, Data);

            // 레벨은 경험치의 함수다. 정산이 경험치를 올렸으니 여기서 다시 계산한다.
            // 여러 번 불려도 두 번 오르지 않는다 (Leveling.Sync).
            LastReport.LevelsGained = Leveling.Sync(Save, Data.Balance);
            LastReport.NewLevel = Save.Player.Level;

            FactorySystem.PruneCollected(Save);
            PruneResolvedExpeditions();

            // 정산 중에 계약을 산 것은 아니지만, 시각이 지나 만료됐을 수는 있다.
            SyncSupportBenefits();

            if (!LastReport.IsEmpty) Resolved?.Invoke(LastReport);
            return LastReport;
        }

        /// <summary>
        /// 지원계약의 파생 혜택을 지금 시각 기준으로 다시 계산한다.
        ///
        /// <para>계약을 사거나 만료된 직후에도 불러야 한다 — 그래야 화면이 다음 정산까지
        /// 기다리지 않고 바뀐 칸 수를 본다.</para>
        /// </summary>
        internal void SyncSupportBenefits()
        {
            if (Save == null) return;
            Save.Warehouse.BonusCapacity = Support.WarehouseBonus(Save, Clock.UtcNow);
        }

        /// <summary>고를 것이 하나뿐이면 그 id, 아니면 null.</summary>
        private string SingleEmployerOrNull()
        {
            string only = null;
            int count = 0;
            foreach (var e in Data.AllEmployers) { only = e.NpcId; count++; }
            return count == 1 ? only : null;
        }

        private void StartNewGame(string employerNpcId)
        {
            Save.Player.EmployerNpcId = employerNpcId;
            Save.Player.Money = Data.Balance.StartingMoney;
            Orientation.Initialize(Save, Data);
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
