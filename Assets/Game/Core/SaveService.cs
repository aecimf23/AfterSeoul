using System;
using System.Collections.Generic;
using System.IO;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 세이브 파일 읽기/쓰기.
    ///
    /// <para><b>MVP 에서는 암호화하지 않는다.</b> 모바일은 서버 권위가 없어서 로컬 세이브를
    /// 암호화해도 결국 뚫린다. 대신 치팅의 이득 자체를 없애는 쪽으로 설계했다 —
    /// 결과는 출발 시점 시드로 고정돼 있고(<see cref="Rng"/>), 본편 전송은 별도 검증 계층을
    /// 거친다(LINK_CONTRACT.md). 난독화는 Phase 7 에서 검토한다.</para>
    ///
    /// <para><b>원자적 쓰기:</b> tmp 에 쓰고 → 교체. 저장 도중 앱이 죽어도 기존 세이브가
    /// 반쯤 덮인 상태로 남지 않는다. 본편 <c>SaveManager</c> 와 같은 방식이다.</para>
    ///
    /// <para>파일 I/O 는 <see cref="IFileStore"/> 뒤에 둔다. 테스트가 실제 디스크를
    /// 건드리지 않게 하려는 것뿐이고, 구현체는 실파일과 인메모리 둘뿐이다.</para>
    /// </summary>
    public sealed class SaveService
    {
        public const string FileName = "save.json";
        public const int CurrentSchemaVersion = 2;

        private readonly IFileStore _files;
        private readonly IJsonCodec _json;
        private readonly IClock _clock;

        public SaveService(IFileStore files, IJsonCodec json, IClock clock)
        {
            _files = files;
            _json = json;
            _clock = clock;
        }

        /// <summary>없거나 깨졌으면 새 세이브를 만든다. 예외를 위로 던지지 않는다.</summary>
        public GameSave LoadOrCreate()
        {
            if (_files.Exists(FileName))
            {
                try
                {
                    var save = _json.Deserialize<GameSave>(_files.ReadAllText(FileName));
                    if (save != null) return Migrate(save);
                }
                catch (Exception e)
                {
                    // 깨진 세이브로 게임을 못 켜게 만드는 것이 최악이다.
                    // 백업으로 옮겨두고 새로 시작한다. 플레이어에게는 UI 가 알린다.
                    _files.TryMove(FileName, FileName + ".corrupt");
                    LastLoadError = e.Message;
                }
            }
            return CreateNew();
        }

        public string LastLoadError { get; private set; }

        public GameSave CreateNew()
        {
            var now = _clock.UtcNow;
            return new GameSave
            {
                SchemaVersion = CurrentSchemaVersion,
                WelcomePage = 0,
                SavedAt = now,
                Player = new PlayerState { CreatedAt = now },
                Factory = new FactoryState { LastCollectedAt = now },
            };
        }

        public void Save(GameSave save)
        {
            save.SchemaVersion = CurrentSchemaVersion;
            string tmp = FileName + ".tmp";
            _files.WriteAllText(tmp, _json.Serialize(save));
            _files.Replace(tmp, FileName);
        }

        /// <summary>
        /// 구버전 세이브 올리기. 버전을 올릴 때마다 여기에 단계를 하나씩 추가한다 — 건너뛰지 않는다.
        ///
        /// <para><b>마이그레이션은 현재 데이터 파일을 읽지 않는다.</b> 아이템 id 를 직접 쓴다.
        /// 데이터는 계속 바뀌는데 마이그레이션은 과거의 한 시점을 고정해 두는 물건이라,
        /// 지금의 <c>scav_pool.json</c> 을 참조하면 1년 뒤에 전혀 다른 결과가 나온다.</para>
        /// </summary>
        private static GameSave Migrate(GameSave save)
        {
            if (save.SchemaVersion < 2)
            {
                // v2: 무기가 파견 필수가 됐다 (GDD §7 0.3). 그 전에 고용한 스캐브는 맨손이라
                // 파견할 방법이 없다 — 입문 지역 전리품에 장비가 없고 상점은 신뢰도가 필요하다.
                // 제일 싼 무기 하나를 쥐여 준다. "원래 자기 칼은 있었다"로 읽히고,
                // 지금 고용하는 티어 1 이 들고 오는 것과 같은 물건이라 이득이 되지도 않는다.
                foreach (var scav in save.Scavs)
                {
                    if (scav.Equipment == null)
                        scav.Equipment = new Dictionary<string, string>();

                    string weapon;
                    if (!scav.Equipment.TryGetValue("Weapon", out weapon) || string.IsNullOrEmpty(weapon))
                        scav.Equipment["Weapon"] = "MEL01";
                }
                save.SchemaVersion = 2;
            }

            return save;
        }
    }

    /// <summary>파일 접근 추상화. 구현체는 실파일과 인메모리(테스트) 둘뿐이다.</summary>
    public interface IFileStore
    {
        bool Exists(string relativePath);
        string ReadAllText(string relativePath);
        void WriteAllText(string relativePath, string content);
        void Replace(string sourceRelative, string destRelative);
        bool TryMove(string sourceRelative, string destRelative);
    }

    /// <summary>직렬화 추상화. Unity 쪽에서 Newtonsoft.Json 으로 구현한다.</summary>
    public interface IJsonCodec
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }

    /// <summary>
    /// 실제 디스크 구현. <c>Application.persistentDataPath</c> 를 생성자로 받는다.
    /// 이 클래스도 UnityEngine 을 참조하지 않는다 — 경로는 바깥에서 주입한다.
    /// </summary>
    public sealed class FileStore : IFileStore
    {
        private readonly string _root;

        public FileStore(string rootDirectory)
        {
            _root = rootDirectory;
            Directory.CreateDirectory(_root);
        }

        private string Full(string rel) => Path.Combine(_root, rel);

        public bool Exists(string rel) => File.Exists(Full(rel));
        public string ReadAllText(string rel) => File.ReadAllText(Full(rel));
        public void WriteAllText(string rel, string content) => File.WriteAllText(Full(rel), content);

        public void Replace(string sourceRel, string destRel)
        {
            string src = Full(sourceRel), dst = Full(destRel);
            if (File.Exists(dst)) File.Replace(src, dst, dst + ".bak");
            else File.Move(src, dst);
        }

        public bool TryMove(string sourceRel, string destRel)
        {
            try
            {
                string dst = Full(destRel);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(Full(sourceRel), dst);
                return true;
            }
            catch { return false; }
        }
    }
}
