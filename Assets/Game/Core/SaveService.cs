using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        /// <summary>기존 진행을 우선 읽고, 필요하면 이전 정상 사본으로 복구한다.</summary>
        public GameSave LoadOrCreate()
        {
            LastLoadError = null;
            RecoveryRequired = false;
            UnsupportedFutureSchema = false;
            bool hasPrimary = _files.Exists(FileName);
            bool hasBackup = _files.Exists(FileName + ".bak");
            if (!hasPrimary && !hasBackup) return CreateNew();

            string primaryProblem = null;
            if (hasPrimary)
            {
                var primary = ReadSave(FileName, out primaryProblem, out bool newerSchema);
                if (primary != null) return primary;
                // A newer app may have written this save. An older backup must never roll it back.
                if (newerSchema)
                {
                    UnsupportedFutureSchema = true;
                    ThrowRecoveryRequired(primaryProblem);
                }
            }

            if (hasBackup)
            {
                var backup = ReadSave(FileName + ".bak", out string backupProblem, out bool _);
                if (backup != null)
                {
                    if (hasPrimary) Archive(FileName, FileName + ".corrupt");
                    string tmp = FileName + ".recovery.tmp";
                    _files.WriteAllText(tmp, _json.Serialize(backup));
                    // Primary was moved out first, so FileStore moves tmp into place and
                    // cannot overwrite the known-good backup with a corrupt primary.
                    _files.Replace(tmp, FileName);
                    LastLoadError = hasPrimary
                        ? "손상된 저장 파일을 이전 정상 사본에서 복구했습니다."
                        : "저장 파일이 없어 이전 정상 사본에서 복구했습니다.";
                    return backup;
                }
                ThrowRecoveryRequired(hasPrimary
                    ? primaryProblem + " / 백업: " + backupProblem
                    : "백업: " + backupProblem);
            }

            ThrowRecoveryRequired(primaryProblem);
            throw new InvalidOperationException("Unreachable save recovery state.");
        }

        public string LastLoadError { get; private set; }
        public bool RecoveryRequired { get; private set; }
        public string RecoveryFailure { get; private set; }
        public bool UnsupportedFutureSchema { get; private set; }

        /// <summary>플레이어가 복구 화면에서 명시적으로 선택한 경우에만 새 진행을 기록한다.</summary>
        public GameSave StartNewAfterRecoveryFailure()
        {
            if (!RecoveryRequired) throw new InvalidOperationException("A recovery decision is not pending.");
            if (_files.Exists(FileName)) Archive(FileName, FileName + ".corrupt");
            if (_files.Exists(FileName + ".bak")) Archive(FileName + ".bak", FileName + ".bak.corrupt");
            var fresh = CreateNew();
            Save(fresh);
            RecoveryRequired = false;
            RecoveryFailure = null;
            UnsupportedFutureSchema = false;
            return fresh;
        }

        private GameSave ReadSave(string path, out string problem, out bool newerSchema)
        {
            problem = null;
            newerSchema = false;
            GameSave save;
            string raw = _files.ReadAllText(path); // I/O failures are not malformed save data.
            try
            {
                var envelope = JObject.Parse(raw);
                JToken version = null;
                int versionFields = 0;
                foreach (var field in envelope.Properties())
                {
                    if (!string.Equals(field.Name, "SchemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                    versionFields++;
                    version = field.Value;
                    if (version.Type != JTokenType.Integer || version.Value<long>() <= CurrentSchemaVersion) continue;
                    newerSchema = true;
                    problem = "이 저장 데이터는 더 새로운 앱 버전에서 작성되었습니다. 앱을 업데이트하세요.";
                    return null;
                }
                var player = envelope.GetValue("Player", StringComparison.OrdinalIgnoreCase);
                var savedAt = envelope.GetValue("SavedAt", StringComparison.OrdinalIgnoreCase);
                if (versionFields != 1 || version == null || version.Type != JTokenType.Integer ||
                    version.Value<long>() <= 0 || player == null || player.Type != JTokenType.Object ||
                    savedAt == null || savedAt.Type == JTokenType.Null)
                {
                    problem = "저장 데이터의 필수 항목이 없습니다.";
                    return null;
                }
                save = _json.Deserialize<GameSave>(raw);
            }
            catch (JsonException e) { problem = e.Message; return null; }
            if (!HasRequiredShape(save))
            {
                problem = "저장 데이터가 불완전합니다.";
                return null;
            }
            return Migrate(save);
        }

        private static bool HasRequiredShape(GameSave save)
        {
            if (save == null || save.SchemaVersion <= 0 || save.SavedAt == default(DateTimeOffset) ||
                save.Player == null || save.Player.Equipment == null ||
                save.Warehouse?.Stacks == null || save.Factory?.Queue == null ||
                save.Factory.Workbench?.Scores == null || save.Factory.Production?.AssignedScavUids == null ||
                save.Scavs == null || save.Expeditions == null || save.Quests?.Active == null ||
                save.Quests.CompletedIds == null || save.Market?.Offers == null ||
                save.NpcTrust == null || save.Support == null ||
                (save.Mail != null && save.Mail.Outbox == null)) return false;
            if (save.Scavs.Exists(scav => scav == null || scav.TraitIds == null ||
                (save.SchemaVersion >= 2 && scav.Equipment == null))) return false;
            if (save.Expeditions.Exists(expedition => expedition == null || expedition.ScavUids == null) ||
                save.Factory.Queue.Exists(job => job == null) ||
                save.Quests.Active.Exists(quest => quest == null) ||
                save.Market.Offers.Exists(offer => offer == null || offer.TraitIds == null) ||
                (save.Mail != null && save.Mail.Outbox.Exists(shipment => shipment == null || shipment.Items == null)))
                return false;
            return true;
        }

        private void ThrowRecoveryRequired(string reason)
        {
            RecoveryRequired = true;
            RecoveryFailure = reason ?? "저장 파일을 읽을 수 없습니다.";
            throw new InvalidDataException(RecoveryFailure);
        }

        private void Archive(string source, string preferredDestination)
        {
            string destination = preferredDestination;
            while (_files.Exists(destination))
                destination = preferredDestination + "." + Guid.NewGuid().ToString("N");
            if (!_files.TryMove(source, destination))
                throw new IOException("저장 파일의 원본을 보존하지 못했습니다.");
        }

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

        /// <summary>Replace the reset backup without retaining the previous progression.</summary>
        public void RefreshBackup(GameSave save)
        {
            string tmp = FileName + ".reset.tmp";
            _files.WriteAllText(tmp, _json.Serialize(save));
            _files.Replace(tmp, FileName + ".bak");
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
            if (File.Exists(dst)) File.Replace(src, dst,
                destRel == SaveService.FileName + ".bak" ? null : dst + ".bak");
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
