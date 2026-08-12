using StandAlone.Console.Services;
using StandAlone.Integration.Services;

namespace StandAlone.Tests;

public class LabelDecisionServiceTests
{
    [Fact]
    public void SatoTemplateResolver_ChoosesCrossOverTemplate_ForLabelTypeSix()
    {
        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 6,
            LabelSize = "2x6",
            LabelFormat = "CARTON_LABEL"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt06_xover_2x6_ref-first.sato", fileName);
    }

    [Fact]
    public void SatoTemplateRenderer_ReplacesPlaceholders_WithCurrentValues()
    {
        const string template = "ITEM: {ItemNumber}\nFORMAT: {LabelFormat}\nPLANT: {Plant}";
        var payload = new ThermalLabelPayload
        {
            ItemNumber = "ABC123",
            Plant = 99,
            LabelFormat = "CARTON_LABEL"
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.Contains("ITEM: ABC123", rendered);
        Assert.Contains("FORMAT: CARTON_LABEL", rendered);
        Assert.Contains("PLANT: 99", rendered);
    }

    [Fact]
    public void DefaultCartonTemplate_45x3_RendersLegacyRotationAndFieldLayout()
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, "lt_default_45x3.sato");
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 0,
            LabelFormat = "CARTON_LABEL",
            ItemNumber = "FL9036MOD1P4",
            PartDescription = "3 X 6 X 0.31 IN | FINISH LINE",
            ColorDesc = "FL90-WHITE",
            ShapeDesc = "3X6",
            SeriesDesc = "FINISH LINE",
            Shade = "555",
            Size = "0",
            Grade = 1,
            Plant = 610,
            Inspector = "tony",
            StackNumber = "ORD-2026-1001",
            SalesQty = 12.5m,
            SalesUom = "SF",
            PackageWeight = 37.5m,
            LisQty = 1,
            Quantity = 1,
            UccBarcode = "20081516630981",
            CartonUpc = "081516630981",
            CartonUpcNumSys = "0",
            CartonUpcMfg = "81516",
            CartonUpcProd = "63098",
            CartonUpcChkdgt = "1",
            CartonBarcodeSerial = "%20262230615229555001016100001",
            MfgDateCode = "6224:1441",
            ItemNumberMasked = "FL90  36MOD1P4   ",
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.Contains("\\x1b%2", rendered);
        Assert.Contains("\\x1bL0203\\x1bS\\x1bWB1FL90  36MOD1P4", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("SF", rendered);
        Assert.Contains("\\x1bH335\\x1bV0038\\x1bS81516", rendered);
        Assert.Contains("\\x1bH243\\x1bV0038\\x1bL0102\\x1bS63098", rendered);
        Assert.DoesNotContain("{", rendered);
    }

    [Fact]
    public void ResolveLabelType_ReturnsExpectedFormat_ForKnownCode()
    {
        var service = new LabelDecisionService();

        var decision = service.ResolveLabelType(0);

        Assert.Equal("DALTILE", decision);
    }

    [Fact]
    public void ResolveLabelType_ReturnsUnknown_ForUnmappedCode()
    {
        var service = new LabelDecisionService();

        var decision = service.ResolveLabelType(999);

        Assert.Equal("UNKNOWN", decision);
    }

    [Fact]
    public void BuildDecisionRows_ParsesCsvAndMapsRows()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "item_nbr,plant,label_type_code\nABC123,1,2\nXYZ999,2,7\n");

            var service = new LabelDecisionService();
            var rows = service.ReadInputCsv(tempFile);

            Assert.Single(rows.Where(r => r.ItemNumber == "ABC123"));
            Assert.Equal("LOWES", rows.First(r => r.ItemNumber == "ABC123").LabelFormat);
            Assert.Equal("UNKNOWN", rows.First(r => r.ItemNumber == "XYZ999").LabelFormat);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadItemRunningCsv_ParsesSerialItemPlantRows()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempFile, "serial_number,item_nbr,plant\nSER123,ABC123,1\nSER456,XYZ999,2\n");

            var service = new LabelDecisionService();
            var rows = service.ReadItemRunningCsv(tempFile);

            Assert.Equal(2, rows.Count);
            Assert.Equal("SER123", rows[0].SerialNumber);
            Assert.Equal("ABC123", rows[0].ItemNumber);
            Assert.Equal(1, rows[0].Plant);
            Assert.Equal("SER456", rows[1].SerialNumber);
            Assert.Equal("XYZ999", rows[1].ItemNumber);
            Assert.Equal(2, rows[1].Plant);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
