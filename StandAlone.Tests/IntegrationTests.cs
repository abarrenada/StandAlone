using StandAlone.Integration.Services;

namespace StandAlone.Tests;

public class IntegrationTests
{
    [Fact]
    public void PlcPayloadParser_ParsesEolPayload()
    {
        var parser = new PlcPayloadParser();
        var payload = parser.Parse("EOL|SER12345678901234567890|ABC123|1");

        Assert.NotNull(payload);
        Assert.Equal("EOL", payload!.DeviceType);
        Assert.Equal("SER12345678901234567890", payload.SerialNumber);
        Assert.Equal("ABC123", payload.ItemNumber);
        Assert.Equal(1, payload.Plant);
    }

    [Fact]
    public void FileLabelPrinter_WritesXmlLabelFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        const string serial = "SER12345678901234567890";

        try
        {
            var printer = new FileLabelPrinter(tempDir);
            var result = printer.PrintAsync("ABC123", 1, "PALLET_LABEL", CancellationToken.None, serial).GetAwaiter().GetResult();

            Assert.True(result.Success);
            var files = Directory.GetFiles(tempDir, "*.xml");
            Assert.Single(files);
            Assert.Contains(serial, Path.GetFileName(files[0]));

            var content = File.ReadAllText(files[0]);
            Assert.Contains("<PalletLabelDocument>", content);
            Assert.Contains("<LabelFormat>PALLET_LABEL</LabelFormat>", content);
            Assert.Contains($"<PalletId>{serial}</PalletId>", content);
            Assert.Contains($"<SerialNumber>{serial}</SerialNumber>", content);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FileSapIntegrationService_WritesSapXmlFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new FileSapIntegrationService(tempDir);
            var payload = new PalletIntegrationPayload
            {
                SerialNumber = "SER12345678901234567890",
                ItemNumber = "ABC123",
                Plant = 1,
                Source = "EOL",
                ReceivedAt = DateTimeOffset.UtcNow
            };

            var result = service.SendPalletIntegrationAsync(payload, CancellationToken.None).GetAwaiter().GetResult();

            Assert.True(result.Success);
            var files = Directory.GetFiles(tempDir, "*.xml");
            Assert.Single(files);
            var content = File.ReadAllText(files[0]);
            Assert.Contains("<SerialNumber>SER12345678901234567890</SerialNumber>", content);
            Assert.Contains("<ItemNumber>ABC123</ItemNumber>", content);
            Assert.Contains("<Plant>1</Plant>", content);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FileWmsIntegrationService_WritesWmsXmlFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new FileWmsIntegrationService(tempDir);
            var payload = new WmsIntegrationPayload
            {
                SerialNumber = "SER12345678901234567890",
                ItemNumber = "ABC123",
                Plant = 1,
                Source = "EOL",
                ReceivedAt = DateTimeOffset.UtcNow,
                Location = "SHRWRAP",
                BoxesPerPallet = 48,
                LisQty = 10,
            };

            var result = service.SendPalletIntegrationAsync(payload, CancellationToken.None).GetAwaiter().GetResult();

            Assert.True(result.Success);
            var files = Directory.GetFiles(tempDir, "*.xml");
            Assert.Single(files);
            var content = File.ReadAllText(files[0]);
            Assert.Contains("<SerialNumber>SER12345678901234567890</SerialNumber>", content);
            Assert.Contains("<ItemNumber>ABC123</ItemNumber>", content);
            Assert.Contains("<Plant>1</Plant>", content);
            Assert.Contains("<Location>SHRWRAP</Location>", content);
            Assert.Contains("<BoxesPerPallet>48</BoxesPerPallet>", content);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task OracleSapIntegrationService_BuildsFixedWidth300CharDataIn_AndCallsReceiptProcedure()
    {
        var fake = new FakeOracleInterfaceService();
        var service = new OracleSapIntegrationService(fake);

        var payload = new PalletIntegrationPayload
        {
            SerialNumber = "042-000012345",
            ItemNumber   = "ABC123",
            Plant        = 42,
            Source       = "EOL",
            ShopOrder    = "SO123",
            LineNumber   = 3,
            Shift        = 2,
            ConfirmedQty = 480,
            Inspector    = "jdoe",
            ReceivedAt   = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero),
            ConfirmedAt  = new DateTimeOffset(2026, 9, 17, 8, 5, 30, TimeSpan.Zero),
            Location     = "SHRWRAP",
            Cartons      = 40,
            Shade        = "0750",
            Size         = "3x45",
            Grade        = 1,
        };

        var result = await service.SendPalletIntegrationAsync(payload, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("p_insert_interface_receipt", fake.CapturedProcedureName);
        Assert.NotNull(fake.CapturedDataIn);
        Assert.Equal(300, fake.CapturedDataIn!.Length);
        Assert.StartsWith("FA042", fake.CapturedDataIn); // "F" literal + trans-type "A" + 3-digit plant
        Assert.Contains("ABC123", fake.CapturedDataIn);
    }

    [Fact]
    public async Task OracleSapIntegrationService_NonSuccessResultCode_ReturnsFailure()
    {
        var fake = new FakeOracleInterfaceService { ResultToReturn = "ERR item not found" };
        var service = new OracleSapIntegrationService(fake);

        var result = await service.SendPalletIntegrationAsync(new PalletIntegrationPayload(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ERR", result.ErrorMessage);
    }

    private class FakeOracleInterfaceService : IOracleInterfaceService
    {
        public string? CapturedProcedureName;
        public string? CapturedDataIn;
        public string ResultToReturn = "SUC000000000000000000";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<int> ExecuteAsync(
            string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<string> CallInterfaceProcedureAsync(string procedureName, string dataIn, CancellationToken cancellationToken = default)
        {
            CapturedProcedureName = procedureName;
            CapturedDataIn = dataIn;
            return Task.FromResult(ResultToReturn);
        }
    }
}
