using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public static class ProgressResetDialog
    {
        public static void Show(AppShell shell, GameSession session, Transform canvas, Action onReset)
        {
            if (canvas.Find("ProgressResetDialog") != null) return;
            RectTransform root = null;
            bool busy = false, closed = false;
            Action close = () => {
                if (busy || closed) return;
                closed = true;
                root.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
                else UnityEngine.Object.DestroyImmediate(root.gameObject);
            };
            root = Ui.Modal("ProgressResetDialog", canvas, Loc.Text("진행 초기화"), close, out var body);
            root.gameObject.AddComponent<SafeArea>();
            var text = Ui.Paragraph("ResetExplanation", body,
                Loc.Text("소지금, 창고, 스캐브, 파견, 의뢰, 신뢰도와 공장 진행을 지우고 고용주 선택과 튜토리얼부터 다시 시작합니다. 이전 진행은 복구할 수 없습니다.\n\n소리·언어·화면 설정, 계정 연결, 이미 발송한 화물과 발송 한도, 유료 지원 계약은 유지됩니다."), 28, Theme.Text);
            Ui.Size(text.gameObject, 370);
            var error = Ui.Paragraph("ResetError", body, "", 26, Theme.Danger);
            Ui.Size(error.gameObject, 100);
            var cancel = Ui.Button("CancelProgressReset", body, Loc.Text("취소 — 계속 플레이"), close, Theme.PanelAlt, 28);
            Ui.Size(cancel.gameObject, 90);
            Button confirm = null;
            confirm = Ui.Button("ConfirmProgressReset", body, Loc.Text("진행 삭제 후 처음부터 시작"), () => {
                if (busy || closed) return;
                busy = true; confirm.interactable = false; cancel.interactable = false;
                var previous = session.Save;
                try { session.ResetProgress(); }
                catch (Exception) {
                    busy = false;
                    if (ReferenceEquals(previous, session.Save)) {
                        error.text = Loc.Text("저장하지 못했습니다. 진행은 유지됩니다. 다시 시도하세요.");
                        confirm.interactable = true; cancel.interactable = true;
                        return;
                    }
                    // The new primary is durable; rebuild before reporting a backup failure.
                    close(); onReset?.Invoke();
                    shell.Toast(Loc.Text("새 진행은 저장됐지만 백업을 갱신하지 못했습니다."), 6);
                    return;
                }
                busy = false; close(); onReset?.Invoke();
            }, Theme.Danger, 28);
            Ui.Size(confirm.gameObject, 100);
        }
    }
}
