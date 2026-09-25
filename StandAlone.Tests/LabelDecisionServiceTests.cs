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
    public void SatoTemplateResolver_ChoosesMexicoTemplate_WhenIsMexicoItem()
    {
        // Mexico is priority-1 in dtplc067.p, checked before LabelTypeCode/CustomerChar --
        // confirm it wins even when LabelTypeCode looks like it should route elsewhere.
        var payload = new ThermalLabelPayload
        {
            IsMexicoItem = true,
            LabelTypeCode = 4,
            LabelSize = "45x3",
            LabelFormat = "CARTON_LABEL"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt_mexico_45x3.sato", fileName);
    }

    [Fact]
    public void Mexico45x3Template_RendersWithNoLeftoverPlaceholders()
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, "lt_mexico_45x3.sato");
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        // Mexico-specific fields (MexicoBarcodeSerial etc.) are normally computed by
        // ThermalPrinterCommandBuilder.ApplyMexicoFields (StandAlone.CartonUi), which
        // StandAlone.Tests intentionally doesn't reference -- set directly here, same
        // pattern as the CrossOver/F&D placeholder-rendering tests above.
        var payload = new ThermalLabelPayload
        {
            LabelFormat = "CARTON_LABEL",
            IsMexicoItem = true,
            ItemNumber = "M-0000MXTILE1",
            ItemNumberMasked = "M-00  0MXTILE1",
            ColorDesc = "BLANCO",
            ShapeDesc = "30X30 CM",
            Grade = 1,
            Inspector = "TB",
            Shift = 1,
            MfgDateCodeShort = "6266",
            MachineLineTerminalCode = "9036-01-45678",
            CartonUpcNumSys = "7",
            CartonUpcMfg = "50123",
            CartonUpcProd = "45600",
            CartonUpcChkdgt = "1",
            Quantity = 10,
            LisQty = 10,
            LisDesc = "SF",
            SalesQty = 10m,
            SalesUom = "SF",
            PackageWeight = 45.0m,
            MexicoBarcodeSerial = "M20262260900001200110100065000010",
            MexicoBarcodeCommand = ">HM2>C0262260900001200110100065000010",
            MexicoLotCode = "201",
            MexicoQtyString = "Contenido 10.00 SF/ 45.00 KG/ 10 SF",
            MexicoToneCalbrBlock = "\x1BH0310\x1BV0402\x1BL0101\x1BSTono/Calbr:\x1BH0310\x1BV0387\x1BL0101\x1BWB01200 1",
            MexicoIconBlock = string.Empty,
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.DoesNotContain("{", rendered);
        Assert.Contains(payload.MexicoBarcodeSerial, rendered);
        Assert.Contains("BLANCO", rendered);
    }

    [Theory]
    [InlineData("ref-first", "lt06_xover_2x725_ref-first.sato")]
    [InlineData("ref-last", "lt06_xover_2x725_ref-last.sato")]
    public void SatoTemplateResolver_ChoosesCrossOverTemplate_For2x725(string orientation, string expectedFileName)
    {
        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 6,
            LabelSize = "2x725",
            LabelFormat = "CARTON_LABEL",
            TemplateOrientation = orientation
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void SatoTemplateResolver_ChoosesCrossOverManualTemplate_ForManualPrint()
    {
        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 6,
            LabelSize = "2x725",
            LabelFormat = "CARTON_LABEL",
            IsManualPrint = true,
            // Manual mode takes precedence over orientation -- both should resolve the
            // same 2-up/kiss-cut filename regardless of TemplateOrientation.
            TemplateOrientation = "ref-last"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt06_xover_2x725_man-2upkiss.sato", fileName);
    }

    [Theory]
    [InlineData("lt06_xover_2x725_ref-first.sato")]
    [InlineData("lt06_xover_2x725_ref-last.sato")]
    [InlineData("lt06_xover_2x725_man-2upkiss.sato")]
    public void CrossOver2x725Template_RendersWithNoLeftoverPlaceholders(string templateFileName)
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, templateFileName);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        // Field values normally computed generically in ThermalPrinterCommandExporter.
        // ResolveCommandText (StandAlone.CartonUi) right before rendering — set directly here
        // since that computation lives in the WinForms project, which StandAlone.Tests
        // intentionally does not reference.
        var payload = new ThermalLabelPayload
        {
            LabelFormat = "CARTON_LABEL",
            LabelTypeCode = 6,
            ItemNumber = "FL9036MOD1P4",
            ItemNumberPart1 = "FL90",
            ItemNumberPart2 = "36MOD1P4   ",
            ColorDesc = "FL90-WHITE",
            ColorDescFrench = "FL90-BLANC",
            ColorDescSpanish = "FL90-BLANCO",
            ShapeDesc = "3X6",
            Grade = 1,
            Plant = 610,
            MfgDateCodeShort = "6224",
            InspectorDisplay = "TB 01",
            ShadeLotCode = "05550",
            CartonBarcodeSerial = "%20262230615229555001016100001",
            CartonBarcodeCommand = ">H%2>C0262230615229555001016100001",
            CartonUpcNumSys = "0",
            CartonUpcMfg = "81516",
            CartonUpcProd = "63098",
            CartonUpcChkdgt = "1",
            LisQty = 1,
            LisDesc = "SF",
            SalesQtyFormatted = "12.50",
            SalesUom = "SF",
            PackageWeightFormatted = "37.50",
            MachineLineTerminalCode = "9036-01-45678",
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.DoesNotContain("{", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("FL90-WHITE", rendered);
        Assert.Contains("FL90-BLANC", rendered);
        Assert.Contains("FL90-BLANCO", rendered);
        Assert.Contains("12.50 SF", rendered);
        Assert.Contains("37.50 LBS", rendered);
    }

    [Theory]
    [InlineData("45x3", false, "lt04_default_45x3.sato")]
    [InlineData("45x3", true, "lt04_default_45x3_man.sato")]
    [InlineData("2x725", false, "lt04_default_2x725.sato")]
    [InlineData("2x725", true, "lt04_default_2x725_man.sato")]
    [InlineData("175x838", false, "lt04_default_175x838.sato")]
    [InlineData("175x838", true, "lt04_default_175x838_man.sato")]
    [InlineData("3x45", false, "lt04_default_3x45.sato")]
    // 3x4.5 has no manual layout in the real source -- manual mode falls back to the auto template.
    [InlineData("3x45", true, "lt04_default_3x45.sato")]
    public void SatoTemplateResolver_ChoosesFndTemplate_ForLabelTypeFour(string labelSize, bool isManualPrint, string expectedFileName)
    {
        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 4,
            LabelSize = labelSize,
            LabelFormat = "CARTON_LABEL",
            IsManualPrint = isManualPrint
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void SatoTemplateResolver_ChoosesFndInvertedTemplate_For3x45RefLast()
    {
        var payload = new ThermalLabelPayload
        {
            LabelTypeCode = 4,
            LabelSize = "3x45",
            LabelFormat = "CARTON_LABEL",
            TemplateOrientation = "ref-last"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt04_default_3x45_inverted.sato", fileName);
    }

    [Theory]
    [InlineData("lt04_default_45x3.sato")]
    [InlineData("lt04_default_45x3_man.sato")]
    [InlineData("lt04_default_2x725.sato")]
    [InlineData("lt04_default_2x725_man.sato")]
    [InlineData("lt04_default_175x838.sato")]
    [InlineData("lt04_default_175x838_man.sato")]
    [InlineData("lt04_default_3x45.sato")]
    [InlineData("lt04_default_3x45_inverted.sato")]
    public void FndTemplate_RendersWithNoLeftoverPlaceholders(string templateFileName)
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, templateFileName);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        // Field values normally computed generically in ThermalPrinterCommandExporter.
        // ResolveCommandText (StandAlone.CartonUi) right before rendering — set directly here
        // since that computation lives in the WinForms project, which StandAlone.Tests
        // intentionally does not reference.
        var payload = new ThermalLabelPayload
        {
            LabelFormat = "CARTON_LABEL",
            LabelTypeCode = 4,
            ColorDesc = "FL90-WHITE",
            CustomerPartNumber = "#1234567890 ",
            LisQty = 1200,
            ShadeLotCode = "05550",
            MfgDateCodeShort = "6224",
            CartonUpcNumSys = "0",
            CartonUpcMfg = "81516",
            CartonUpcProd = "63098",
            CartonUpcChkdgt = "1",
            CartonBarcodeSerial = "%20262230615229555001016100001",
            NominalSizeUs = "12.00in x 24.00in",
            NominalSizeMetric = "30.48cm x 60.96cm",
            ActualSizeUs = "11.75in x 23.75in",
            ActualSizeMetric = "29.85cm x 60.33cm",
            Thickness = "0.31in",
            SalesQtyFormatted = "12.50",
            SalesUom = "SF",
            CoverageMetricFormatted = "(1.16sqm)",
            PackageWeightFormatted = "37.50",
            PackageWeightMetricFormatted = " (17.01kg)",
            Quantity = 1,
            FndReferenceMoveOffset = "H0420V0000",
            Fnd3x45Footer = "\x02\x1BA\x1BA114240832\x1BZ\x03",
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.DoesNotContain("{", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("FL90-WHITE", rendered);
        Assert.Contains("SKU# #1234567890 ", rendered);
        Assert.Contains("Nominal Size: 12.00in x 24.00in", rendered);
        Assert.Contains("Coverage Area Per Carton: 12.50 SF(1.16sqm)", rendered);
        Assert.Contains("Weight Per Carton: 37.50 LBS (17.01kg)", rendered);
    }

    [Theory]
    [InlineData("45x3", "lt_default_45x3.sato")]
    [InlineData("2x725", "lt_default_2x725.sato")]
    [InlineData("175x838", "lt_default_175x838.sato")]
    [InlineData("2x6", "lt_default_2x6.sato")]
    [InlineData("3x45", "lt_default_3x45.sato")]
    public void SatoTemplateResolver_ChoosesManufacturingTemplate_ForBlankCustomerCharAndSize(string labelSize, string expectedFileName)
    {
        var payload = new ThermalLabelPayload
        {
            CustomerChar = "",
            LabelTypeCode = 0,
            LabelSize = labelSize,
            LabelFormat = "CARTON_LABEL"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void SatoTemplateResolver_ChoosesManufacturingInvertedTemplate_For3x45RefLast()
    {
        var payload = new ThermalLabelPayload
        {
            CustomerChar = "",
            LabelSize = "3x45",
            LabelFormat = "CARTON_LABEL",
            TemplateOrientation = "ref-last"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt_default_3x45_inverted.sato", fileName);
    }

    [Theory]
    [InlineData("lt_default_45x3.sato")]
    [InlineData("lt_default_2x725.sato")]
    [InlineData("lt_default_175x838.sato")]
    [InlineData("lt_default_2x6.sato")]
    [InlineData("lt_default_3x45.sato")]
    [InlineData("lt_default_3x45_inverted.sato")]
    public void ManufacturingTemplate_RendersWithNoLeftoverPlaceholders(string templateFileName)
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, templateFileName);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        // Field values normally computed generically in ThermalPrinterCommandExporter.
        // ResolveCommandText (StandAlone.CartonUi) right before rendering — set directly here
        // since that computation lives in the WinForms project, which StandAlone.Tests
        // intentionally does not reference.
        var payload = new ThermalLabelPayload
        {
            LabelFormat = "CARTON_LABEL",
            LabelTypeCode = 0,
            CustomerChar = "",
            ItemNumberMasked = "FL90  36MOD1P4   ",
            ColorDesc = "FL90-WHITE",
            ShapeDesc = "3X6",
            ColorDescFrench = "FL90-BLANC",
            ColorDescSpanish = "FL90-BLANCO",
            ShapeDescFrench = "3X6",
            SeriesDesc = "FINISH LINE",
            BrandName = "DALTILE",
            Grade = 1,
            Plant = 610,
            MfgDateCodeShort = "6224",
            InspectorDisplay = "TB 01",
            ShadeLotCode = "05550",
            CartonBarcodeSerial = "%20262230615229555001016100001",
            CartonBarcodeCommand = ">H%2>C0262230615229555001016100001",
            CartonUpcNumSys = "0",
            CartonUpcMfg = "81516",
            CartonUpcProd = "63098",
            CartonUpcChkdgt = "1",
            LisQty = 1,
            LisDesc = "SF",
            SalesQtyFormatted = "12.50",
            SalesUom = "SF",
            PackageWeightFormatted = "37.50",
            MachineLineTerminalCode = "9036-01-45678",
            Quantity = 1,
            EngineReferenceMove = "H-17V0005",
            GradeHighlightBlock = string.Empty,
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.DoesNotContain("{", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("FL90-WHITE", rendered);
        Assert.Contains("FL90-BLANC", rendered);
        Assert.Contains("FL90-BLANCO", rendered);
        Assert.Contains("DALTILE", rendered);
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
        // Pins down exact dtmlb001.i coordinate fragments (complements
        // ManufacturingTemplate_RendersWithNoLeftoverPlaceholders' broader, generic-value check).
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
            CartonBarcodeCommand = ">H%2>C0262230615229555001016100001",
            MfgDateCode = "6224:1441",
            MfgDateCodeShort = "6224",
            ItemNumberMasked = "FL90  36MOD1P4   ",
            EngineReferenceMove = "H-17V0005",
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.Contains("\\x1b%2", rendered);
        Assert.Contains("\\x1bH840\\x1bV0520\\x1bL0202\\x1bS\\x1bWB1FL90  36MOD1P4", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("SF", rendered);
        Assert.Contains("\\x1bH280\\x1bV0069\\x1bL0102\\x1bS81516", rendered);
        Assert.Contains("\\x1bH190\\x1bV0069\\x1bL0102\\x1bS63098", rendered);
        Assert.DoesNotContain("{", rendered);
    }

    [Theory]
    [InlineData("H", "45x3", "lt_retail_45x3.sato")]
    [InlineData("L", "4x3", "lt_retail_4x3.sato")]
    [InlineData("B", "2x725", "lt_retail_2x725.sato")]
    [InlineData("D", "2x6", "lt_retail_2x6.sato")]
    [InlineData("F", "3x45", "lt_retail_3x45.sato")]
    [InlineData("H", "175x838", "lt_retail_175x838.sato")]
    public void SatoTemplateResolver_ChoosesRetailTemplate_ForNonBlankCustomerChar(
        string customerChar, string labelSize, string expectedFileName)
    {
        var payload = new ThermalLabelPayload
        {
            CustomerChar = customerChar,
            LabelSize = labelSize,
            LabelFormat = "CARTON_LABEL"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void SatoTemplateResolver_KeepsManufacturingTemplate_ForBlankCustomerChar()
    {
        var payload = new ThermalLabelPayload
        {
            CustomerChar = "",
            LabelSize = "45x3",
            LabelFormat = "CARTON_LABEL"
        };

        var fileName = SatoTemplateResolver.ResolveTemplateFileName(payload);

        Assert.Equal("lt_default_45x3.sato", fileName);
    }

    [Theory]
    [InlineData("lt_retail_45x3.sato")]
    [InlineData("lt_retail_4x3.sato")]
    [InlineData("lt_retail_2x725.sato")]
    [InlineData("lt_retail_2x6.sato")]
    [InlineData("lt_retail_3x45.sato")]
    [InlineData("lt_retail_175x838.sato")]
    public void RetailCartonTemplate_RendersWithNoLeftoverPlaceholders(string templateFileName)
    {
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var templatePath = Path.Combine(templatesDirectory, templateFileName);
        Assert.True(File.Exists(templatePath), $"Template not found at {templatePath}");
        var template = File.ReadAllText(templatePath);

        // Field values normally computed by ThermalPrinterCommandBuilder.ApplyStandardRetailFields
        // (StandAlone.CartonUi) right before rendering — set directly here since that computation
        // lives in the WinForms project, which StandAlone.Tests intentionally does not reference.
        var payload = new ThermalLabelPayload
        {
            LabelFormat = "CARTON_LABEL",
            CustomerChar = "H",
            PanelType = 2,
            ItemNumber = "FL9036MOD1P4",
            ItemNumberMasked = "FL90  36MOD1P4   ",
            ColorDesc = "FL90-WHITE",
            ColorDescDisplay = "#ABC1234567 FL90-WHITE",
            ShapeDesc = "3X6",
            ColorDescFrench = "FL90-BLANC",
            ShapeDescFrench = "3X6",
            ColorDescSpanish = "FL90-BLANCO",
            ShapeDescSpanish = "3X6",
            SeriesDesc = "FINISH LINE",
            Grade = 1,
            Plant = 610,
            MfgDateCode = "6224:1441",
            InspectorDisplay = "TB 01",
            ShadeLotCode = "05550",
            CartonBarcodeSerial = "%20262230615229555001016100001",
            CartonBarcodeCommand = ">H%2>C0262230615229555001016100001",
            CartonUpc = "081516630981",
            CartonUpcNumSys = "0",
            CartonUpcMfg = "81516",
            CartonUpcProd = "63098",
            CartonUpcChkdgt = "1",
            Quantity = 1,
            LisQty = 1,
            LisDesc = "SF",
            SalesQty = 12.5m,
            SalesUom = "SF",
            PackageWeight = 37.5m,
            MachineLineTerminalCode = "9036-01-45678",
            CustomerPartNumber = "ABC1234567",
            BrandName = "DALTILE",
            SidePanelBlock = "\\x1b%3\\x1bH0120\\x1bV0020\\x1bL0408\\x1bSABC1234567",
            CustomerHighlightBlock = string.Empty,
            GradeHighlightBlock = string.Empty,
            CaseBarcodeBlock = string.Empty,
            QtyWeightBlock = "\\x1bH290\\x1bV0218\\x1bL0101\\x1bM1 SF",
        };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.DoesNotContain("{", rendered);
        Assert.Contains(payload.CartonBarcodeSerial, rendered);
        Assert.Contains("FL90-WHITE", rendered);
    }

    [Theory]
    [InlineData("lt_retail_45x3.sato")]
    [InlineData("lt_retail_4x3.sato")]
    public void RetailCartonTemplate_SubstitutesEngineReferenceMove(string templateFileName)
    {
        // EngineReferenceMove is normally computed by ThermalPrinterCommandBuilder.
        // ComputeEngineReferenceMove (StandAlone.CartonUi) from the station's configured
        // Printer Model — only the 45x3/4x3 sizes have a real per-hand coordinate difference
        // (dtlbl060b.i's ws-dev-model branch). This just confirms the template actually
        // substitutes whatever value it's given, in place of the old hardcoded reference move.
        var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
        var template = File.ReadAllText(Path.Combine(templatesDirectory, templateFileName));

        var payload = new ThermalLabelPayload { EngineReferenceMove = "\\x1bA3H100V0005" };

        var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);

        Assert.Contains("\\x1bA3H100V0005", rendered);
        Assert.DoesNotContain("\\x1bA3H-17V0005", rendered);
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
