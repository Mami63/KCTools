using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using KCTools.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KCTools.Protocol;

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

// ---------------------------------------------------------------- CRC
var crc = QcdmCrc16.Compute(Encoding.ASCII.GetBytes("123456789"));
Check(crc == 0x2189, "CRC-16/Kermit bilinen vektör (0x2189)");
Check(QcdmCrc16.Verify(Encoding.ASCII.GetBytes("123456789"), crc), "CRC doğrulama");

// ---------------------------------------------------------------- QCDM framing
var payload = new byte[] { 0x7E, 0x7D, 0x01, 0x41, 0x00 };
var frame = QcdmFramer.Encode(0x4B, payload);

Check(frame[0] == 0x7E && frame[frame.Length - 1] == 0x7E, "QCDM: bayrak baytları");
Check(frame.Length > payload.Length + 4, "QCDM: kaçış nedeniyle büyüdü");
Check(QcdmFramer.TryDecode(frame, out var decoded, out var error), "QCDM: decode başarılı");
Check(decoded != null && decoded.Command == 0x4B, "QCDM: komut doğru");
Check(decoded != null && Convert.ToHexString(decoded.Payload) == Convert.ToHexString(payload),
    "QCDM: yük aynen geri alındı");
Check(decoded != null && decoded.CrcValid, "QCDM: CRC geçerli");

var corrupted = (byte[])frame.Clone();
corrupted[corrupted.Length - 2] ^= 0xFF;
Check(!QcdmFramer.TryDecode(corrupted, out _, out _), "QCDM: bozuk CRC reddedildi");

// ---------------------------------------------------------------- rawprogram / patch
var rom = Path.Combine(Path.GetTempPath(), "kctools-protocol-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(rom);

try
{
    File.WriteAllBytes(Path.Combine(rom, "boot.img"), new byte[1024]);
    File.WriteAllBytes(Path.Combine(rom, "empty.img"), Array.Empty<byte>());

    const string rawprogram = """
        <data>
          <program label="boot" filename="boot.img" start_sector="2048"
                   num_partition_sectors="4096" physical_partition_number="0"
                   start_byte_hex="0x100000" />
          <program label="missing" filename="missing.img" start_sector="0"
                   num_partition_sectors="1" physical_partition_number="0" />
          <program label="empty" filename="empty.img" start_sector="0"
                   num_partition_sectors="1" physical_partition_number="0" />
          <program label="" filename="" start_sector="0"
                   num_partition_sectors="1" physical_partition_number="0" />
        </data>
        """;

    var programs = RawProgramParser.ParsePrograms(rawprogram);
    Check(programs.Count == 4, "rawprogram: 4 program");
    Check(programs[0].Label == "boot" && programs[0].StartSector == 2048 &&
          programs[0].NumSectors == 4096 && programs[0].PhysicalPartition == 0,
        "rawprogram: alanlar ayrıştırıldı");

    var plan = RawProgramParser.BuildPlan(rawprogram, rom);
    Check(plan.Files.Count == 1, "rawprogram: yalnızca mevcut dosya planlandı");
    Check(plan.Skipped.Count == 3, "rawprogram: eksik/boş/adsız atlandı");
    Check(plan.Files.Count == 1 && plan.Files[0].Length == 1024, "rawprogram: dosya boyutu");

    const string patchXml = """
        <data>
          <patch what="CRC" filename="boot.img" start_sector="2048"
                 physical_partition_number="0" size_in_bytes="4"
                 byte_offset="0" value="DEADBEEF" />
        </data>
        """;

    var patches = RawProgramParser.ParsePatches(patchXml);
    Check(patches.Count == 1 && patches[0].IsCrc, "patch: CRC kaydı");
    Check(patches[0].StartSector == 2048 && patches[0].SizeInBytes == 4,
        "patch: alanlar ayrıştırıldı");

    // ------------------------------------------------------------ scatter
    const string scatter = """
        - partition_index: SYS0
          partition_name: preloader
          file_name: preloader.bin
          is_download: true
          type: SV5_BL_BIN
          linear_start_addr: 0x0
          physical_start_addr: 0x0
          partition_size: 0x40000
          region: EMMC_BOOT_1
          storage: HW_STORAGE_EMMC
          boundary_check: true
          is_reserved: false
          operation_type: BOOTLOADERS
        - partition_index: SYS1
          partition_name: boot
          file_name: boot.img
          is_download: true
          type: EXT4_IMG
          linear_start_addr: 0x100000
          physical_start_addr: 0x100000
          partition_size: 0x2000000
          region: EMMC_USER
          storage: HW_STORAGE_EMMC
          boundary_check: false
          is_reserved: false
          operation_type: UPDATE
        """;

    var mtk = MtkScatterParser.Parse(scatter);
    Check(mtk.Partitions.Count == 2, "scatter: 2 bölüm");
    Check(mtk.Partitions[0].Name == "preloader" && mtk.Partitions[0].Region == "EMMC_BOOT_1",
        "scatter: preloader alanları");
    Check(mtk.Partitions[1].LinearStartAddress == 0x100000 &&
          mtk.Partitions[1].Size == 0x2000000, "scatter: adres/boyut");
    Check(mtk.Partitions[1].Type == "EXT4_IMG", "scatter: tür");
    Check(mtk.DownloadablePartitions is not null, "scatter: indirilebilir liste");

    File.WriteAllBytes(Path.Combine(rom, "preloader.bin"), new byte[512]);
    var resolved = new List<MtkPartition>(mtk.ResolveImages(rom));
    Check(resolved.Count == 2 &&
          resolved.Any(item => item.Name == "preloader") &&
          resolved.Any(item => item.Name == "boot"),
        "scatter: mevcut imajlar çözüldü");

    // ------------------------------------------------------------ MTK BROM DA
    var da = new byte[5000];
    new Random(7).NextBytes(da);
    using var brom = new MtkBromEngine(new MtkBromSimulatorTransport());
    var bromProgress = new SyncProgress();
    var bromResult = await brom.UploadDaAsync(da, bromProgress);

    Check(bromResult.Success && bromResult.Blocks == 2 && bromResult.Bytes == da.Length,
        "MTK: DA iki blok halinde yüklendi");
    Check(bromProgress.Values.Count > 0 && bromProgress.Values[^1] == 100,
        "MTK: DA ilerleme %100");

    // ------------------------------------------------------------ Firehose ROM akışı
    File.WriteAllBytes(Path.Combine(rom, "system.img"), new byte[2048]);

    const string flashXml = """
        <data>
          <program label="boot" filename="boot.img" start_sector="2048"
                   num_partition_sectors="2048" physical_partition_number="0" />
          <program label="system" filename="system.img" start_sector="4096"
                   num_partition_sectors="4096" physical_partition_number="0" />
          <program label="missing" filename="missing.img" start_sector="0"
                   num_partition_sectors="1" physical_partition_number="0" />
        </data>
        """;

    using var loopback = new LoopbackFirehoseTransport();
    using var firehose = new FirehoseClient(loopback);
    var flasher = new FirehoseRomFlasher(firehose);
    var flashProgress = new SyncProgress();
    var flashReport = await flasher.FlashAsync(flashXml, rom, "ufs", flashProgress);

    Check(flashReport.Success, "Firehose: ROM akışı başarılı");
    Check(flashReport.FilesWritten == 2, "Firehose: 2 dosya yazıldı");
    Check(flashReport.BytesWritten == 3072, "Firehose: toplam bayt");
    Check(flashReport.Skipped.Count == 1, "Firehose: eksik dosya atlandı");
    Check(flashProgress.Values.Count > 0 && flashProgress.Values[^1] == 100,
        "Firehose: ilerleme %100");}
finally
{
    try { Directory.Delete(rom, true); } catch { }
}


// ---------------------------------------------------------------- Sahara
var hello = new SaharaHello { Version = 2, MinVersion = 2, MaxCommandLength = 4096, Mode = 0 };
var helloBytes = hello.ToPacket();
Check(helloBytes.Length == 48, "Sahara: HELLO 48 bayt");
Check(SaharaPacket.TryParse(helloBytes, helloBytes.Length, out var helloPacket, out var consumed, out _),
    "Sahara: HELLO ayrıştırıldı");
Check(helloPacket.CommandType == SaharaCommand.Hello && consumed == 48,
    "Sahara: komut ve uzunluk");
Check(SaharaHello.FromPacket(helloPacket).Version == 2, "Sahara: sürüm alanı");

var image = new byte[2500];
new Random(42).NextBytes(image);
var simulator = new SaharaSimulatorTransport(image, 1024);
var sahara = new SaharaProtocol(simulator);
var deviceHello = await sahara.HandshakeAsync();
Check(deviceHello.Version == 2, "Sahara: el sıkışma");

var progress = new SyncProgress();
await sahara.UploadImageAsync(image, 1, progress);
Check(simulator.ReceivedData.Length == image.Length, "Sahara: imaj boyutu korundu");
Check(Convert.ToHexString(simulator.ReceivedData) == Convert.ToHexString(image),
    "Sahara: imaj içeriği aynı");
Check(progress.Values.Count > 0 && progress.Values[^1] == 100, "Sahara: ilerleme %100");
Check(simulator.ReadRequests >= 2, "Sahara: parçalı READ_DATA istekleri");

// ---------------------------------------------------------------- QCDM NV okuma
var nvTransport = new FakeQcdmTransport(0x00, new byte[] { 0xAA, 0xBB, 0xCC });
using var nvClient = new QcdmNvClient(nvTransport);
var nv = await nvClient.ReadNvAsync(0x1234);
Check(nv.Success && nv.Item == 0x1234 && nv.Status == 0, "NV: okuma başarılı");
Check(Convert.ToHexString(nv.Data) == "AABBCC", "NV: veri doğru");
Check(nvTransport.LastItem == 0x1234, "NV: istek öğe numarası");

var nvError = new FakeQcdmTransport(0x10, Array.Empty<byte>());
using var nvErrorClient = new QcdmNvClient(nvError);
var failed = await nvErrorClient.ReadNvAsync(0x0001);
Check(!failed.Success && failed.Status == 0x10, "NV: hata durumu raporlandı");

try
{
    await nvClient.WriteNvAsync(0x1234, new byte[] { 1 });
    Check(false, "NV: servis modu kapalıyken yazma reddedildi");
}
catch (InvalidOperationException)
{
    Check(true, "NV: servis modu kapalıyken yazma reddedildi");
}

nvClient.IsAuthorizedServiceModeEnabled = true;
await nvClient.WriteNvAsync(0x1234, new byte[] { 0x11, 0x22 });

Check(nvClient.LastBackup != null &&
      Convert.ToHexString(nvClient.LastBackup.Data) == "AABBCC",
    "NV: yazma öncesi yedek alındı");
Check(Convert.ToHexString(nvTransport.LastWritePayload) == "34121122",
    "NV: 0x27 paketi ItemID + veri sırası doğru");

// ---------------------------------------------------------------- loader deposu
var loaderBytes = Encoding.UTF8.GetBytes("firehose-loader-binary");
var loaderSha = Convert.ToHexString(SHA256.HashData(loaderBytes)).ToLowerInvariant();
var loaderJson = "[{\"chipset\":\"SM8650\",\"fileName\":\"prog_firehose_ddr.elf\"," +
                 "\"platform\":\"qualcomm\",\"sizeBytes\":" + loaderBytes.Length +
                 ",\"sha256\":\"" + loaderSha + "\"}]";

var loaderCache = Path.Combine(Path.GetTempPath(), "kctools-loaders-" + Guid.NewGuid().ToString("N"));
var loaderService = new LoaderRepositoryService(
    "https://license.example", loaderCache, new HttpClient(new FakeLoaderHandler(loaderJson, loaderBytes)));

var list = await loaderService.ListAsync("SM8650");
Check(list.Count == 1 && list[0].FileName == "prog_firehose_ddr.elf", "Loader: katalog listelendi");

var downloaded = await loaderService.GetLoaderAsync("SM8650");
Check(Convert.ToHexString(downloaded) == Convert.ToHexString(loaderBytes), "Loader: indirildi");

var cachedPath = Path.Combine(loaderCache, "SM8650", "prog_firehose_ddr.elf");
Check(File.Exists(cachedPath), "Loader: önbelleğe yazıldı");
loaderService.Dispose();

var badJson = loaderJson.Replace(loaderSha, new string('0', 64));
var badService = new LoaderRepositoryService(
    "https://license.example", loaderCache, new HttpClient(new FakeLoaderHandler(badJson, loaderBytes)));

try
{
    await badService.GetLoaderAsync("SM8650");
    Check(false, "Loader: hatalı SHA reddedildi");
}
catch (InvalidDataException)
{
    Check(true, "Loader: hatalı SHA reddedildi");
}

badService.Dispose();
Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
sealed class SyncProgress : IProgress<int>
{
    public List<int> Values { get; } = new();

    public void Report(int value)
    {
        Values.Add(value);
    }
}
sealed class FakeQcdmTransport : IQcdmFrameTransport
{
    private readonly byte _status;
    private readonly byte[] _data;
    private byte[] _response;
    private bool _open;

    public FakeQcdmTransport(byte status, byte[] data)
    {
        _status = status;
        _data = data;
    }

    public ushort LastItem { get; private set; }

    public byte[] LastWritePayload { get; private set; } = Array.Empty<byte>();

    public bool IsOpen => _open;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        _open = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] frame, CancellationToken cancellationToken)
    {
        if (!QcdmFramer.TryDecode(frame, out var request, out _))
        {
            return Task.CompletedTask;
        }

        if (request.Command == QcdmNvClient.NvReadCommand)
        {
            LastItem = (ushort)(request.Payload[0] | (request.Payload[1] << 8));

            var payload = new byte[1 + _data.Length];
            payload[0] = _status;
            Buffer.BlockCopy(_data, 0, payload, 1, _data.Length);
            _response = QcdmFramer.Frame(QcdmNvClient.NvReadCommand, payload);
        }
        else if (request.Command == QcdmNvClient.NvWriteCommand)
        {
            LastItem = (ushort)(request.Payload[0] | (request.Payload[1] << 8));
            LastWritePayload = request.Payload;
            _response = QcdmFramer.Frame(QcdmNvClient.NvWriteCommand, new byte[] { 0x00 });
        }

        return Task.CompletedTask;
    }

    public Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }

    public async Task<byte[]> SendAndReceiveAsync(
        byte[] frame, CancellationToken cancellationToken)
    {
        await WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        return await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _open = false;
    }
}
sealed class FakeLoaderHandler : HttpMessageHandler
{
    private readonly string _json;
    private readonly byte[] _bytes;

    public FakeLoaderHandler(string json, byte[] bytes)
    {
        _json = json;
        _bytes = bytes;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("SM8650", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_bytes)
        });
    }
}