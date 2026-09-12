using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AfterSeoul.Core
{
    /// <summary>
    /// <see cref="IJsonCodec"/> 의 Newtonsoft 구현. 본편과 같은 라이브러리다 (DATA_SCHEMA 설계 방침).
    ///
    /// <para><b>enum 은 문자열로 쓴다.</b> 숫자로 쓰면 enum 순서를 바꾸는 순간 옛 세이브의 스캐브가
    /// 전부 다른 상태로 읽힌다. 그 버그는 세이브를 열어봐도 숫자라서 눈에 안 띈다.</para>
    ///
    /// <para><b>날짜는 DateTimeOffset 으로 바로 읽는다.</b> 기본 설정은 DateTime 을 한 번 거치는데,
    /// 그때 기기 타임존이 끼어들 수 있다. 이 게임은 시각이 곧 게임 내용이다.</para>
    /// </summary>
    public sealed class NewtonsoftJsonCodec : IJsonCodec
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // MVP 는 평문 세이브다(부록 B). 사람이 열어볼 수 있게 들여쓴다.
            Formatting = Formatting.Indented,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            Converters = { new StringEnumConverter() },
        };

        public string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);

        public T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
