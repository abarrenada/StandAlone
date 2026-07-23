using LabelDecisionApp.Integration.Services;

namespace LabelDecisionApp.Tests;

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

        try
        {
            var printer = new FileLabelPrinter(tempDir);
            var result = printer.PrintAsync("ABC123", 1, "PALLET_LABEL", CancellationToken.None, "SER12345678901234567890").GetAwaiter().GetResult();

            Assert.True(result.Success);
            var files = Directory.GetFiles(tempDir, "*.xml");
            Assert.Single(files);
            var content = File.ReadAllText(files[0]);
            Assert.Contains("<LabelFormat>PALLET_LABEL</LabelFormat>", content);
            Assert.Contains("<SerialNumber>SER12345678901234567890</SerialNumber>", content);
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
}
