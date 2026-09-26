using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AfterSeoul.Unity.UI
{
    /// <summary>A boot screen that works before AppShell has a GameSession.</summary>
    public static class SaveRecoveryDialog
    {
        private static GameObject visible;
        private static bool blocksBack;
        public static bool IsVisible => visible != null && visible.activeInHierarchy;

        public static bool TryHandleBack()
        {
            if (visible == null || !visible.activeInHierarchy)
            {
                visible = null;
                return false;
            }
            if (!blocksBack) Close(visible);
            return true;
        }

        public static void ShowBlocked(Transform owner, bool futureSchema, bool storageFailure,
            Action retry, Action startNew)
        {
            var body = Create(owner, Loc.Text("저장 데이터 확인 필요"), out var root);
            blocksBack = true;
            string explanation = storageFailure
                ? Loc.Text("저장 공간에 접근하지 못했습니다. 저장 공간과 권한을 확인한 뒤 다시 시도하세요. 기존 진행은 변경하지 않았습니다.")
                : futureSchema
                    ? Loc.Text("이 저장 데이터는 더 새로운 앱에서 작성되었습니다. 앱을 업데이트한 뒤 다시 시도하세요. 기존 파일은 그대로 보존했습니다.")
                    : Loc.Text("저장 파일과 이전 정상 사본을 열 수 없습니다. 기존 파일은 그대로 보존했습니다. 다시 시도하거나 명시적으로 새 게임을 시작할 수 있습니다.");
            AddExplanation(body, explanation);
            var status = Ui.Paragraph("RecoveryStatus", body, "", 26, Theme.Warn);
            Ui.Size(status.gameObject, 140);

            var retryButton = Ui.Button("RetrySaveRecovery", body, Loc.Text("다시 시도"), () =>
            {
                Close(root);
                retry?.Invoke();
            }, Theme.AccentDim, 30);
            Ui.Size(retryButton.gameObject, 100);

            if (startNew == null) return;
            bool confirming = false;
            Button newButton = null;
            newButton = Ui.Button("StartNewAfterSaveFailure", body, Loc.Text("새 게임 시작"), () =>
            {
                if (!confirming)
                {
                    confirming = true;
                    status.text = Loc.Text("기존 파일을 따로 보관한 뒤 새 게임을 시작합니다. 이전 진행은 앱 안에서 복구할 수 없습니다. 계속하려면 한 번 더 누르세요.");
                    Ui.SetButtonLabel(newButton, Loc.Text("새 게임 시작 확인"));
                    return;
                }
                try
                {
                    startNew();
                    Close(root);
                }
                catch (Exception e)
                {
                    status.text = Loc.Text("저장 파일을 보관하거나 새 진행을 저장하지 못했습니다. 다시 시도하세요.");
                    Debug.LogWarning("[SaveRecovery] " + e);
                }
            }, Theme.Danger, 30);
            Ui.Size(newButton.gameObject, 100);
        }

        public static void ShowRecovered(Transform owner)
        {
            var body = Create(owner, Loc.Text("저장 데이터 복구 완료"), out var root);
            blocksBack = false;
            AddExplanation(body, Loc.Text("저장 파일을 이전 정상 사본에서 복구했습니다. 마지막 저장 이후의 진행은 반영되지 않았을 수 있습니다."));
            var button = Ui.Button("ContinueAfterSaveRecovery", body, Loc.Text("계속"), () => Close(root), Theme.AccentDim, 30);
            Ui.Size(button.gameObject, 100);
        }

        private static RectTransform Create(Transform owner, string title, out GameObject root)
        {
            if (visible != null) Close(visible);
            var previous = owner.Find("[SaveRecovery]");
            if (previous != null) Close(previous.gameObject);
            EnsureEventSystem();

            root = new GameObject("[SaveRecovery]", typeof(RectTransform));
            root.transform.SetParent(owner, false);
            visible = root;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            root.AddComponent<GraphicRaycaster>();

            var backdrop = Ui.Panel("Background", root.transform, Theme.Bg);
            Ui.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            var safe = Ui.Rect("SafeArea", root.transform);
            Ui.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();

            var card = Ui.Surface("RecoveryCard", safe, Theme.Panel);
            card.anchorMin = new Vector2(.5f, .5f);
            card.anchorMax = new Vector2(.5f, .5f);
            card.pivot = new Vector2(.5f, .5f);
            card.sizeDelta = new Vector2(960, 850);
            card.anchoredPosition = Vector2.zero;
            Ui.Column(card, 22, new RectOffset(36, 36, 36, 36));
            var heading = Ui.Label("RecoveryTitle", card, title, 42, TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(heading.gameObject, 80);
            return card;
        }

        private static void AddExplanation(Transform body, string explanation)
        {
            var text = Ui.Paragraph("RecoveryExplanation", body, explanation, 30, Theme.Text);
            Ui.Size(text.gameObject, 300);
        }

        private static void Close(GameObject root)
        {
            if (root == null) return;
            if (visible == root) visible = null;
            root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureEventSystem()
        {
            if (!Application.isPlaying || EventSystem.current != null) return;
            var go = new GameObject("[EventSystem]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
