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
