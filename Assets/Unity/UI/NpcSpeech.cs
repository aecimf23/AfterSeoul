using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Explicitly ticked speech: hidden panels never keep talking in the background.</summary>
    public sealed class NpcSpeech
    {
        readonly Text _label;
        readonly string _npc, _line;
        float _elapsed;
        int _shown, _blips;
        public bool IsComplete => _shown >= _line.Length;

        public NpcSpeech(Text label, string npc, string line)
        {
            _label = label; _npc = npc; _line = line ?? "";
            _label.supportRichText = false;
            _label.text = "";
        }

        public void Tick(float delta)
        {
            if (_label == null || IsComplete || delta <= 0 || float.IsNaN(delta) || float.IsInfinity(delta)) return;
            _elapsed += Mathf.Min(delta, .25f);
            int count = Mathf.Min(_line.Length, Mathf.FloorToInt(_elapsed * 32));
            if (count == _shown) return;
            // Do not split a UTF-16 surrogate pair when a translated line contains an emoji.
            if (count > 0 && count < _line.Length && char.IsHighSurrogate(_line[count - 1])) count--;
            _shown = count;
            _label.text = _line.Substring(0, count);
            int blips = count / 3;
            if (blips > _blips && count > 0 && !char.IsWhiteSpace(_line[count - 1])) Sfx.NpcBlip(_npc);
            _blips = blips;
        }

        public void Finish() { _shown = _line.Length; if (_label != null) _label.text = _line; }
    }

    public static class EmployerGreeting
    {
        public static string Line(string npc, int variant = 0)
        {
            switch (npc)
            {
                case "HWANG": return AfterSeoul.Core.Loc.Text(variant % 2 == 0
                    ? "돌아왔군. 인원부터 확인해. 오늘도 할 일이 있다."
                    : "왔나. 보급은 준비됐다. 사람 잃지 말고 다녀와.");
                case "DR_CHOI": return AfterSeoul.Core.Loc.Text(variant % 2 == 0
                    ? "돌아오셨군요. 다친 곳은 없으세요? 잠깐 숨부터 돌리세요."
                    : "무사해서 다행이에요. 동료들 상태도 함께 살펴봐 주세요.");
                case "YONGSAN_KIM": return AfterSeoul.Core.Loc.Text(variant % 2 == 0
                    ? "왔군. 작업대는 비워 뒀어. 손 좀 풀고 시작하지."
                    : "돌아왔나. 물건도 중요하지만, 일할 사람이 먼저지.");
                default: return "";
            }
        }
    }
}
