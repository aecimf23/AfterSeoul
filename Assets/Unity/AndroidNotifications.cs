using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;

#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID
using Unity.Notifications.Android;
#endif

namespace AfterSeoul.Unity
{
    /// <summary>
    /// 로컬 알림 (GDD §31). <b>무엇을 언제 알릴지는 <see cref="NotificationPlan"/> 이 정한다.</b>
    /// 여기는 그걸 안드로이드 API 로 옮기기만 한다.
    ///
    /// <para><b>패키지가 없으면 통째로 아무것도 하지 않는다.</b> asmdef 의 versionDefines 가
    /// <c>AFTERSEOUL_NOTIFICATIONS</c> 를 켜 주는데, 패키지 복원이 실패한 상태에서도
    /// 프로젝트가 컴파일은 돼야 한다 — 알림 하나 때문에 게임 전체가 안 열리면 안 된다.</para>
    ///
    /// <para>에디터와 PC 에서는 예약하지 않는다. 대신 로그로 남겨서 "무엇이 예약됐을지"를
    /// 실기기 없이도 볼 수 있게 한다.</para>
    /// </summary>
    public static class AndroidNotifications
    {
#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
        private const string ChannelId = "afterseoul_default";
        private static bool _channelReady;
        private static PermissionRequest _permissionRequest;
#endif

        /// <summary>
        /// 예약을 지금 상태로 다시 짠다. 전부 취소하고 새로 건다.
        ///
        /// <para>개별로 찾아 고치지 않는 이유: 파견을 하나 더 보내거나 제작을 취소했을 때
        /// 어느 예약이 살아 있는지 추적하는 코드는 반드시 어긋난다. 지우고 다시 그리면
        /// 어긋날 자리가 없다.</para>
        /// </summary>
        public static void Reschedule(GameSession session)
        {
            if (session == null || session.Save == null) return;

            var plan = NotificationPlan.Build(session.Save, session.Data, session.Clock.UtcNow);

#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            EnsureChannel();
            AndroidNotificationCenter.CancelAllScheduledNotifications();

            foreach (var p in plan)
            {
                var n = new AndroidNotification
                {
                    Title = p.Title,
                    Text = p.Body,
                    // 안드로이드는 기기 로컬 시각을 받는다. 게임 날짜 경계(KST 05:00)와는
                    // 무관하다 — 이건 "몇 시에 울릴 것인가"이지 게임 규칙이 아니다.
                    FireTime = p.At.ToLocalTime().DateTime,
                    ShouldAutoCancel = true,
                };
                AndroidNotificationCenter.SendNotification(n, ChannelId);
            }
#else
            // 에디터·PC: 실제로 걸지 않는다. 무엇이 걸렸을지는 보여준다.
            LogPlan(plan);
#endif
        }

        /// <summary>앱이 포그라운드로 돌아오면 예약을 전부 지운다. 이미 본 것을 또 울릴 이유가 없다.</summary>
        public static void CancelAll()
        {
#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#endif
        }

        /// <summary>
        /// 안드로이드 13(API 33)부터는 알림에 런타임 권한이 필요하다.
        /// 첫 파견을 보낸 뒤에 묻는다 — 시작하자마자 물으면 무엇에 대한 허락인지 모른다.
        /// </summary>
        public static void RequestPermissionIfNeeded()
        {
#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.NotRequested &&
                (_permissionRequest == null || _permissionRequest.Status != PermissionStatus.RequestPending))
                _permissionRequest = new PermissionRequest();
#endif
        }

#if AFTERSEOUL_NOTIFICATIONS && UNITY_ANDROID && !UNITY_EDITOR
        private static void EnsureChannel()
        {
            if (_channelReady) return;

            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = ChannelId,
                Name = Loc.Text("현장 보고"),
                Importance = Importance.Default,
                Description = Loc.Text("파견 복귀와 생산 완료를 알립니다."),
            });
            _channelReady = true;
        }
#endif

        private static void LogPlan(List<PlannedNotification> plan)
        {
            if (plan.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("[알림] 실기기라면 ").Append(plan.Count).Append("건 예약:");
            foreach (var p in plan)
                sb.Append("\n  ").Append(p.At.ToLocalTime().ToString("MM-dd HH:mm"))
                  .Append("  ").Append(p.Title).Append(" — ").Append(p.Body);
            Debug.Log(sb.ToString());
        }
    }
}
