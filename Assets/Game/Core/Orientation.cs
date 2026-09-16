using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    public enum OrientationStage { Pending, Outbound, ReadyToDeliver, Completed, Skipped }

    [Serializable]
    public sealed class OrientationState
    {
        public OrientationStage Stage;
        public string ExpeditionUid;
        // Mission cargo is not ordinary inventory: capacity, selling and daily reset cannot consume it.
        public bool HasCargo;
    }

    public static class Orientation
    {
        public const int DurationMinutes = 3;
        public const long DeliveryReward = 50000;
        public const string MapId = "MYEONGDONG";

        public static void Initialize(GameSave save, IDataRegistry data)
        {
            if (save.Orientation != null || string.IsNullOrEmpty(save.Player.EmployerNpcId)) return;
            bool eligible = Employers.Find(data, save.Player.EmployerNpcId) != null
                && save.Player.Level <= 2 && save.Expeditions.Count == 0;
            foreach (var scav in save.Scavs)
                if (scav.ExpeditionCount > 0) eligible = false;
            save.Orientation = new OrientationState {
                Stage = eligible ? OrientationStage.Pending : OrientationStage.Skipped };
        }

        public static string BlockReason(GameSave save, IDataRegistry data, IReadOnlyList<string> team)
        {
            if (save.Orientation == null || save.Orientation.Stage != OrientationStage.Pending)
                return Loc.Text("초도 보급 파견은 처음 한 번만 가능합니다");
            if (save.Expeditions.Count > 0) return Loc.Text("이미 일반 파견을 시작했습니다");
            foreach (var s in save.Scavs)
                if (s.ExpeditionCount > 0) return Loc.Text("이미 파견 경험이 있습니다");
            if (data.GetMap(MapId) == null) return Loc.Text("보급 경로 정보를 찾을 수 없습니다");
            if (team == null || team.Count != 1) return Loc.Text("무기를 든 대기 인원 1명을 선택하세요");
            var scav = save.Scavs.Find(s => s.Uid == team[0]);
            if (scav == null || scav.Status != ScavStatus.Idle) return Loc.Text("대기 중인 인원을 선택하세요");
            if (!Scav.Equipment.EffectsOf(scav, data).HasWeapon) return Loc.Text("인원 화면에서 무기를 지급하세요");
            return null;
        }

        public static ExpeditionState Depart(GameSave save, IDataRegistry data,
            IReadOnlyList<string> team, DateTimeOffset now)
        {
            if (BlockReason(save, data, team) != null) return null;
            var exp = new ExpeditionState {
                Uid = "ex_" + save.RngCounter.ToString("x8"), MapId = MapId,
                DepartedAt = now, ReturnsAt = now.AddMinutes(DurationMinutes),
                IsOrientation = true, Seed = save.TakeSeed()
            };
            exp.ScavUids.Add(team[0]);
            var scav = save.Scavs.Find(s => s.Uid == team[0]);
            scav.Status = ScavStatus.OnExpedition;
            scav.ExpeditionCount++;
            save.Expeditions.Add(exp);
            save.Orientation.Stage = OrientationStage.Outbound;
            save.Orientation.ExpeditionUid = exp.Uid;
            return exp;
        }

        public static void Return(ResolveContext ctx, ExpeditionState exp)
        {
            if (exp.Resolved) return;
            foreach (var uid in exp.ScavUids)
            {
                var scav = ctx.Save.Scavs.Find(s => s.Uid == uid);
                if (scav != null) scav.Status = ScavStatus.Idle;
            }
            exp.Resolved = true;
            var state = ctx.Save.Orientation;
            if (state != null && state.Stage == OrientationStage.Outbound && state.ExpeditionUid == exp.Uid)
            {
                state.Stage = OrientationStage.ReadyToDeliver;
                state.HasCargo = true;
            }
            ctx.Report.Expeditions.Add(new ExpeditionResult {
                ExpeditionUid = exp.Uid, MapId = exp.MapId, ReturnedAt = exp.ReturnsAt,
                IsOrientation = true
            });
        }

        public static bool Deliver(GameSave save)
        {
            var state = save.Orientation;
            if (state == null || state.Stage != OrientationStage.ReadyToDeliver || !state.HasCargo) return false;
            state.HasCargo = false;
            state.Stage = OrientationStage.Completed;
            save.Player.Money += DeliveryReward;
            return true;
        }

        public static void SkipPending(GameSave save)
        {
            if (save.Orientation != null && save.Orientation.Stage == OrientationStage.Pending)
                save.Orientation.Stage = OrientationStage.Skipped;
        }
    }
}
