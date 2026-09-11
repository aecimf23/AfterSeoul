using System;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 결정론적 난수.
    ///
    /// <para><b>System.Random 을 쓰지 않는 이유:</b> .NET 구현체마다 알고리즘이 다르고
    /// 실제로 .NET Core 에서 한 번 바뀐 적이 있다. 같은 시드로 같은 결과가 나온다는
    /// 보장이 문서화돼 있지 않다. 이 게임은 "출발 시점에 고정한 시드로 나중에 결과를
    /// 재현한다"가 핵심 규칙이라, 난수 알고리즘이 흔들리면 세이브가 깨진다.
    /// 그래서 직접 구현한다.</para>
    ///
    /// <para>xorshift32. 게임 밸런스용으로 충분하고, 암호용이 아니다.</para>
    /// </summary>
    public struct Rng
    {
        private uint _state;

        public Rng(uint seed)
        {
            // 0 은 xorshift 의 흡수 상태다. 절대 0 이 되면 안 된다.
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, maxExclusive) 정수. maxExclusive 는 1 이상.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // 모듈로 편향은 게임 밸런스 수준에서 무시 가능(2^32 대비 범위가 매우 작다).
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>[minInclusive, maxInclusive] 정수.</summary>
        public int NextIntRange(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxInclusive));
            return minInclusive + NextInt(maxInclusive - minInclusive + 1);
        }

        /// <summary>[0, 1) 실수.</summary>
        public double NextDouble() => NextUInt() / 4294967296.0;

        /// <summary>확률 p 로 true.</summary>
        public bool Chance(double p) => NextDouble() < p;

        /// <summary>
        /// 하나의 마스터 시드에서 용도별 하위 시드를 파생한다.
        ///
        /// 파견 하나에 여러 난수 소비처(전리품 추첨 / 사고 판정 / 전투)가 있을 때,
        /// 한 스트림을 공유하면 나중에 "전투 판정을 한 번 더 굴리는" 식의 사소한 코드
        /// 변경이 뒤따르는 모든 결과를 바꿔버린다. 용도별로 스트림을 갈라두면
        /// 한쪽을 고쳐도 다른 쪽 결과가 유지된다.
        /// </summary>
        public static uint Derive(uint masterSeed, string purpose)
        {
            // FNV-1a 32bit
            uint hash = 2166136261u;
            foreach (char c in purpose)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return masterSeed ^ hash;
        }

        public static Rng For(uint masterSeed, string purpose) => new Rng(Derive(masterSeed, purpose));
    }
}
