using System;
using UnityEngine;

namespace AfterSeoul.Unity
{
    /// <summary>
    /// 효과음. <b>오디오 파일이 하나도 없다</b> — 파형을 코드로 만든다.
    ///
    /// <para>아트가 아직 없는 단계에서 임팩트를 가장 싸게 회복하는 수단이다. 에셋을 구하거나
    /// 만들 필요가 없고, 리포지토리에 바이너리가 늘지 않고, 음높이·길이를 숫자로 조정할 수 있다.
    /// P6 폴리싱에서 진짜 음원으로 갈아끼워도 호출부는 그대로다.</para>
    ///
    /// <para>소리가 <b>정보를 담게</b> 만든다. 정확히 맞히면 높고 맑은 두 음, 가장자리로 맞히면
    /// 한 음, 빗나가면 낮은 잡음. 화면을 안 봐도 방금 잘했는지가 들린다 — 그게 리듬이 되고,
    /// 리듬이 생겨야 작업이 놀이가 된다.</para>
    /// </summary>
    public static class Sfx
    {
        private const int SampleRate = 44100;

        private static AudioSource _source;
        private static AudioClip _perfect, _good, _edge, _miss, _step, _complete, _tap;

        /// <summary>소리를 끌 수 있게 둔다. 모바일에서 음소거는 기본 기대다.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>부트스트랩이 한 번 부른다. AudioSource 하나를 붙이고 파형을 미리 굽는다.</summary>
        public static void Attach(GameObject host)
        {
            if (_source != null) return;

            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D
            _source.volume = 0.55f;

            // 미니게임 판정음. 위로 갈수록 높고 맑다.
            _perfect = Chord("sfx_perfect", new[] { 1320f, 1760f }, 0.16f, 0.55f);
            _good = Tone("sfx_good", 990f, 0.11f, 0.5f);
            _edge = Tone("sfx_edge", 660f, 0.10f, 0.45f);
            _miss = Noise("sfx_miss", 150f, 0.14f, 0.4f);

            // 공정 한 단계가 끝났을 때의 짧은 "딱".
            _step = Tone("sfx_step", 520f, 0.07f, 0.4f, square: true);

            // 물건이 완성됐을 때. 세 음 아르페지오 — 여기만 길게 준다.
            _complete = Arpeggio("sfx_complete", new[] { 880f, 1108f, 1320f }, 0.10f, 0.5f);

            _tap = Tone("sfx_tap", 420f, 0.04f, 0.3f, square: true);
        }

        public static void Perfect() => Play(_perfect);
        public static void Good() => Play(_good);
        public static void Edge() => Play(_edge);
        public static void Miss() => Play(_miss);
        public static void Step() => Play(_step);
        public static void Complete() => Play(_complete);
        public static void Tap() => Play(_tap);

        /// <summary>미니게임 점수를 그대로 넘기면 알맞은 소리가 난다.</summary>
        public static void ForScore(double score)
        {
            if (score <= 0.0) Miss();
            else if (score >= 0.92) Perfect();
            else if (score >= 0.7) Good();
            else Edge();
        }

        private static void Play(AudioClip clip)
        {
            if (!Enabled || _source == null || clip == null) return;
            // PlayOneShot 이라 겹쳐 울려도 서로 자르지 않는다. 연타 판정이 뭉개지면 안 된다.
            _source.PlayOneShot(clip);
        }

        // ── 파형 만들기 ──────────────────────────────────────────

        /// <summary>
        /// 감쇠 포락선. 시작이 제일 크고 끝에서 0 이 된다.
        /// 이게 없으면 파형이 뚝 끊기면서 "틱" 하는 잡음이 같이 난다.
        /// </summary>
        private static float Envelope(int i, int total)
        {
            float t = (float)i / total;
            float attack = t < 0.02f ? t / 0.02f : 1f;      // 아주 짧은 상승
            float decay = (1f - t) * (1f - t);
            return attack * decay;
        }

        private static AudioClip Tone(string name, float hz, float seconds, float gain, bool square = false)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float phase = 2f * Mathf.PI * hz * i / SampleRate;
                float wave = square ? (Mathf.Sin(phase) >= 0f ? 1f : -1f) : Mathf.Sin(phase);
                data[i] = wave * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Chord(string name, float[] hz, float seconds, float gain)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                foreach (var f in hz) sum += Mathf.Sin(2f * Mathf.PI * f * i / SampleRate);
                data[i] = sum / hz.Length * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Arpeggio(string name, float[] hz, float noteSeconds, float gain)
        {
            int per = Mathf.Max(1, (int)(SampleRate * noteSeconds));
            var data = new float[per * hz.Length];

            for (int note = 0; note < hz.Length; note++)
            {
                for (int i = 0; i < per; i++)
                {
                    float wave = Mathf.Sin(2f * Mathf.PI * hz[note] * i / SampleRate);
                    data[note * per + i] = wave * Envelope(i, per) * gain;
                }
            }
            return Bake(name, data);
        }

        /// <summary>
        /// 잡음 + 낮은 톤. 빗나갔을 때 쓴다 — 음정이 없어야 "틀렸다"로 들린다.
        /// 난수는 고정 시드로 만든다. 소리가 실행마다 달라질 이유가 없다.
        /// </summary>
        private static AudioClip Noise(string name, float hz, float seconds, float gain)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];
            var rng = new System.Random(1);

            for (int i = 0; i < n; i++)
            {
                float tone = Mathf.Sin(2f * Mathf.PI * hz * i / SampleRate);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = (tone * 0.6f + noise * 0.4f) * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Bake(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
